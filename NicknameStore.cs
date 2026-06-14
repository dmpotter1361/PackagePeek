using System.Text.Json;

namespace AmazonTracker;

/// <summary>
/// Local map of Amazon order id -> friendly name, so cryptic listings like
/// "BQLXBABLT Pink Tarot…" can be shown as "Mom's tarot deck". Persisted as JSON.
/// </summary>
public sealed class NicknameStore
{
    private readonly Dictionary<string, string> _map;
    private static string FilePath => Path.Combine(AppSettings.ConfigDir, "nicknames.json");

    public NicknameStore()
    {
        Dictionary<string, string>? loaded = null;
        try
        {
            if (File.Exists(FilePath))
                loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
        }
        catch { /* fall back to empty */ }
        _map = loaded ?? new();
    }

    public string? Get(string orderId) =>
        !string.IsNullOrEmpty(orderId) && _map.TryGetValue(orderId, out var v) ? v : null;

    public void Set(string orderId, string? name)
    {
        if (string.IsNullOrEmpty(orderId)) return;
        if (string.IsNullOrWhiteSpace(name)) _map.Remove(orderId);
        else _map[orderId] = name.Trim();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}

/// <summary>Minimal single-line text prompt. Returns the entered text, or null if cancelled.</summary>
public static class PromptDialog
{
    public static string? Show(string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 120),
            MaximizeBox = false,
            MinimizeBox = false
        };
        var lbl = new Label { Text = label, AutoSize = true, Location = new Point(12, 14) };
        var box = new TextBox { Text = initial, Location = new Point(14, 38), Width = 352 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(196, 78), Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(286, 78), Width = 80 };
        form.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        box.SelectAll();
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }
}
