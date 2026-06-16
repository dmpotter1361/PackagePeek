using System.Text.Json;

namespace AmazonTracker;

/// <summary>
/// Remembers which packages the user has chosen to hide (e.g. a dead/backordered item
/// they don't care about). Keyed by shipment key (order id + item). Persisted as JSON.
/// </summary>
public sealed class HiddenStore
{
    private readonly HashSet<string> _hidden;
    private static string FilePath => Path.Combine(AppSettings.ConfigDir, "hidden.json");

    public HiddenStore()
    {
        HashSet<string>? loaded = null;
        try
        {
            if (File.Exists(FilePath))
                loaded = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath));
        }
        catch { /* fall back to empty */ }
        _hidden = loaded ?? new();
    }

    public bool Any => _hidden.Count > 0;

    public bool IsHidden(string key) => !string.IsNullOrEmpty(key) && _hidden.Contains(key);

    public void Hide(string key)
    {
        if (!string.IsNullOrEmpty(key) && _hidden.Add(key)) Save();
    }

    public void Clear()
    {
        if (_hidden.Count == 0) return;
        _hidden.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_hidden, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
