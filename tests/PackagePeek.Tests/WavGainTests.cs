using System.Text;
using AmazonTracker;
using Xunit;

namespace PackagePeek.Tests;

public class WavGainTests
{
    // Build a 16-bit PCM mono WAV (data at offset 44) alternating +/- peak.
    private static byte[] MakeWav(short peak, int samples = 200)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        int dataLen = samples * 2;
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataLen);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);      // PCM
        bw.Write((short)1);      // mono
        bw.Write(44100);
        bw.Write(44100 * 2);
        bw.Write((short)2);
        bw.Write((short)16);     // bits
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLen);
        for (int i = 0; i < samples; i++) bw.Write((short)(i % 2 == 0 ? peak : (short)-peak));
        return ms.ToArray();
    }

    private static int PeakFrom44(byte[] wav)
    {
        int peak = 0;
        for (int i = 44; i + 1 < wav.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        return peak;
    }

    [Fact]
    public void Normalize_BoostsQuietSoundTowardFullScale()
    {
        var quiet = MakeWav(1000); // very quiet, like ding/chimes
        Assert.True(WavGain.TryNormalizeAndScale(quiet, 100, out var loud));
        Assert.InRange(PeakFrom44(loud), 30000, 32767);
    }

    [Fact]
    public void Volume_HalvingRoughlyHalvesAmplitude()
    {
        var w = MakeWav(1000);
        WavGain.TryNormalizeAndScale(w, 100, out var full);
        WavGain.TryNormalizeAndScale(w, 50, out var half);
        int pf = PeakFrom44(full), ph = PeakFrom44(half);
        Assert.InRange(ph, (int)(pf * 0.4), (int)(pf * 0.6));
    }

    [Fact]
    public void LoudSound_NotPushedPastFullScale()
    {
        var loud = MakeWav(30000);
        Assert.True(WavGain.TryNormalizeAndScale(loud, 100, out var outw));
        Assert.True(PeakFrom44(outw) <= 32767);
    }

    [Fact]
    public void NonWavBytes_Rejected()
    {
        Assert.False(WavGain.TryNormalizeAndScale(new byte[] { 1, 2, 3, 4 }, 100, out _));
    }
}
