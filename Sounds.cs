using System.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace AmazonTracker;

/// <summary>
/// Notification sounds. Choices are Windows' own bundled sounds (in C:\Windows\Media,
/// present on every PC and licensed with Windows — no copyright concern) plus a custom
/// file the user picks. Playback uses MCI so it handles both .wav and .mp3.
/// </summary>
public static class Sounds
{
    public const string Default = "default";

    public sealed record SoundOption(string Name, string Value)
    {
        public override string ToString() => Name; // shown in the combo box
    }

    private static readonly string MediaDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    // Friendly name -> file in C:\Windows\Media. Filtered to those that actually exist.
    private static readonly (string Name, string File)[] Catalog =
    {
        ("Notify",         "Windows Notify System Generic.wav"),
        ("Chimes",         "chimes.wav"),
        ("Ding",           "ding.wav"),
        ("Ring (classic)", "Ring01.wav"),
        ("Ring (soft)",    "Ring05.wav"),
        ("Ring (long)",    "Windows Ringin.wav"),
        ("Alarm",          "Alarm01.wav"),
        ("Tada",           "tada.wav"),
    };

    /// <summary>Windows default + the built-in sounds present on this PC.</summary>
    public static List<SoundOption> BuiltIns()
    {
        var list = new List<SoundOption> { new("Windows default", Default) };
        foreach (var (name, file) in Catalog)
        {
            var path = Path.Combine(MediaDir, file);
            if (File.Exists(path)) list.Add(new SoundOption(name, path));
        }
        return list;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    private static SoundPlayer? _player; // kept alive so async playback isn't cut off

    /// <summary>
    /// Play the chosen sound once (async) at the given volume (0-100). Falls back to the
    /// system chime on any problem. Volume only applies to file-based sounds; "Windows
    /// default" plays at the system level.
    /// </summary>
    public static void Play(string? choice, int volumePercent = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(choice) || choice == Default || !File.Exists(choice))
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            // WAV: normalize (so quiet sounds like ding/chimes come up to a usable level)
            // then scale by the volume, and play. This is the path for all built-ins.
            if (choice.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                && WavGain.TryNormalizeAndScale(choice, volumePercent, out var pcm))
            {
                var tmp = Path.Combine(Path.GetTempPath(), "pp-sound.wav");
                File.WriteAllBytes(tmp, pcm);
                _player?.Dispose();
                _player = new SoundPlayer(tmp);
                _player.Play();
                return;
            }

            // Non-WAV (e.g. a custom .mp3): MCI can only attenuate, but still honors volume.
            mciSendString("close ppsound", null, 0, IntPtr.Zero);
            if (mciSendString($"open \"{choice}\" alias ppsound", null, 0, IntPtr.Zero) != 0)
            {
                SystemSounds.Asterisk.Play();
                return;
            }
            mciSendString($"setaudio ppsound volume to {Math.Clamp(volumePercent, 0, 100) * 10}", null, 0, IntPtr.Zero);
            mciSendString("play ppsound", null, 0, IntPtr.Zero);
        }
        catch
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }
    }
}
