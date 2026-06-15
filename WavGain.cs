using System.Text;

namespace AmazonTracker;

/// <summary>
/// Normalizes and volume-scales 16-bit PCM WAV audio. Windows' bundled sounds are
/// recorded at wildly different levels (alarms loud, chimes/ding very quiet), so we
/// bring each one's peak up toward full scale, then apply the user's volume. This is
/// what lets the volume slider actually make quiet sounds audible.
/// </summary>
public static class WavGain
{
    public static bool TryNormalizeAndScale(string path, int volumePercent, out byte[] result)
    {
        result = Array.Empty<byte>();
        try { return TryNormalizeAndScale(File.ReadAllBytes(path), volumePercent, out result); }
        catch { return false; }
    }

    public static bool TryNormalizeAndScale(byte[] wav, int volumePercent, out byte[] result)
    {
        result = Array.Empty<byte>();
        if (wav.Length < 44 || Ascii(wav, 0, 4) != "RIFF" || Ascii(wav, 8, 4) != "WAVE") return false;

        // Walk the RIFF chunks to find "fmt " and "data".
        int pos = 12, fmtBody = -1, dataOff = -1, dataLen = 0;
        while (pos + 8 <= wav.Length)
        {
            string id = Ascii(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (size < 0) break;
            int body = pos + 8;
            if (id == "fmt ") fmtBody = body;
            else if (id == "data") { dataOff = body; dataLen = size; }
            int next = body + size + (size & 1); // chunks are word-aligned
            if (next <= pos) break;
            pos = next;
        }
        if (fmtBody < 0 || dataOff < 0 || fmtBody + 16 > wav.Length) return false;

        int audioFormat = BitConverter.ToInt16(wav, fmtBody + 0);
        int bits = BitConverter.ToInt16(wav, fmtBody + 14);
        if (audioFormat != 1 || bits != 16) return false; // only handle 16-bit PCM

        dataLen = Math.Min(dataLen, wav.Length - dataOff);
        dataLen -= dataLen & 1;
        int sampleCount = dataLen / 2;
        if (sampleCount == 0) return false;

        int peak = 1;
        for (int i = 0; i < sampleCount; i++)
        {
            int a = Math.Abs((int)BitConverter.ToInt16(wav, dataOff + i * 2));
            if (a > peak) peak = a;
        }

        double normalize = 0.95 * 32767.0 / peak;                         // bring peak near full scale
        double gain = normalize * (Math.Clamp(volumePercent, 0, 100) / 100.0);

        result = (byte[])wav.Clone();
        for (int i = 0; i < sampleCount; i++)
        {
            int v = (int)Math.Round(BitConverter.ToInt16(wav, dataOff + i * 2) * gain);
            v = Math.Clamp(v, short.MinValue, short.MaxValue);
            var b = BitConverter.GetBytes((short)v);
            result[dataOff + i * 2] = b[0];
            result[dataOff + i * 2 + 1] = b[1];
        }
        return true;
    }

    private static string Ascii(byte[] b, int off, int len) => Encoding.ASCII.GetString(b, off, len);
}
