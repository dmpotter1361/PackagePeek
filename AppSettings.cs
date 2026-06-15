using System.Text.Json;

namespace AmazonTracker;

/// <summary>
/// User-tunable settings, persisted as JSON under %APPDATA%\PackagePeek\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>How often to silently refresh the orders page, in minutes.</summary>
    public int RefreshMinutes { get; set; } = 20;

    /// <summary>How many orders pages to scan each refresh (older in-transit items can be on page 2+).</summary>
    public int PagesToScan { get; set; } = 3;

    /// <summary>Fire a popup when stops-away drops to this number or below (when Amazon shows it).</summary>
    public int StopsAwayThreshold { get; set; } = 3;

    /// <summary>Notify when a package goes out for delivery / "arriving today".</summary>
    public bool NotifyOutForDelivery { get; set; } = true;

    /// <summary>Notify when a package is delivered.</summary>
    public bool NotifyDelivered { get; set; } = true;

    /// <summary>Which Amazon storefront to use (the orders page host).</summary>
    public string AmazonBaseUrl { get; set; } = "https://www.amazon.com";

    /// <summary>Start the app automatically when Windows starts.</summary>
    public bool LaunchAtStartup { get; set; } = true;

    /// <summary>Suppress popups during quiet hours.</summary>
    public bool QuietHoursEnabled { get; set; } = false;

    /// <summary>Quiet hours start hour (0-23).</summary>
    public int QuietStartHour { get; set; } = 22;

    /// <summary>Quiet hours end hour (0-23).</summary>
    public int QuietEndHour { get; set; } = 8;

    /// <summary>Play a chime with each popup.</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>Which sound to play: "default" (Windows chime), a Windows\Media path, or a custom file path.</summary>
    public string SoundChoice { get; set; } = "default";

    /// <summary>Notification sound volume, 0-100 (applies to the chosen sound file; "Windows default" uses system volume).</summary>
    public int SoundVolume { get; set; } = 80;

    /// <summary>Read the alert aloud (text-to-speech).</summary>
    public bool SpeakAloud { get; set; } = false;

    /// <summary>True when the clock is currently inside the quiet-hours window.</summary>
    public bool IsQuietNow()
    {
        if (!QuietHoursEnabled) return false;
        int h = DateTime.Now.Hour;
        // Handles windows that wrap past midnight (e.g. 22 -> 8).
        return QuietStartHour <= QuietEndHour
            ? h >= QuietStartHour && h < QuietEndHour
            : h >= QuietStartHour || h < QuietEndHour;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PackagePeek");

    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Corrupt/unreadable settings shouldn't stop the app — fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
