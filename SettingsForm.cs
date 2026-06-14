namespace AmazonTracker;

/// <summary>
/// A friendly settings window. Layout is fully auto-sizing (TableLayoutPanel) so it
/// adapts to any system font size / DPI instead of overlapping at hardcoded positions.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _s;

    private readonly NumericUpDown _refresh = new() { Minimum = 1, Maximum = 720, Width = 60 };
    private readonly NumericUpDown _pages = new() { Minimum = 1, Maximum = 10, Width = 60 };
    private readonly NumericUpDown _stops = new() { Minimum = 1, Maximum = 20, Width = 60 };
    private readonly NumericUpDown _quietStart = new() { Minimum = 0, Maximum = 23, Width = 55 };
    private readonly NumericUpDown _quietEnd = new() { Minimum = 0, Maximum = 23, Width = 55 };
    private readonly CheckBox _notifyOfd = new() { Text = "When a package is out for delivery / arriving today", AutoSize = true };
    private readonly CheckBox _notifyStops = new() { Text = "When it's within this many stops:", AutoSize = true };
    private readonly CheckBox _notifyDelivered = new() { Text = "When a package is delivered", AutoSize = true };
    private readonly CheckBox _quiet = new() { Text = "Don't pop up during quiet hours:", AutoSize = true };
    private readonly CheckBox _playSound = new() { Text = "Play a chime with each popup", AutoSize = true };
    private readonly CheckBox _speak = new() { Text = "Read the alert aloud (text-to-speech)", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Start automatically when Windows starts", AutoSize = true };
    private readonly Button _testBtn = new() { Text = "Send test popup", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };

    /// <summary>Raised when the user clicks "Send test popup" (settings already committed).</summary>
    public event EventHandler? TestNotificationRequested;

    public SettingsForm(AppSettings settings)
    {
        _s = settings;

        Text = "Package Peek - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        ok.Click += (_, _) => Commit();
        _testBtn.Click += (_, _) => { Commit(); TestNotificationRequested?.Invoke(this, EventArgs.Empty); };
        AcceptButton = ok;
        CancelButton = cancel;

        // --- main grid: column 0 = labels/checkboxes, column 1 = inputs ---
        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        int row = 0;

        AddPair(grid, ref row, L("Check Amazon every"), Inline(_refresh, L("minutes")));
        AddPair(grid, ref row, L("Scan this many order pages"), _pages);

        AddSpan(grid, ref row, Bold("Notifications"));
        AddSpan(grid, ref row, _notifyOfd);
        AddPair(grid, ref row, _notifyStops, _stops);
        AddSpan(grid, ref row, _notifyDelivered);
        AddPair(grid, ref row, _quiet, Inline(_quietStart, L("to"), _quietEnd, L("(24h)")));

        AddSpan(grid, ref row, Bold("Sound"));
        AddSpan(grid, ref row, _playSound);
        AddSpan(grid, ref row, _speak);

        AddSpan(grid, ref row, _startup);

        // --- button row spans the full width: test on the left, Save/Cancel on the right ---
        var buttons = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _testBtn.Anchor = AnchorStyles.Left;
        buttons.Controls.Add(_testBtn, 0, 0);
        var okCancel = Inline(ok, cancel);
        okCancel.Anchor = AnchorStyles.Right;
        buttons.Controls.Add(okCancel, 1, 0);

        var rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        buttons.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        rootLayout.Controls.Add(grid, 0, 0);
        rootLayout.Controls.Add(buttons, 0, 1);
        Controls.Add(rootLayout);

        _notifyStops.CheckedChanged += (_, _) => _stops.Enabled = _notifyStops.Checked;
        _quiet.CheckedChanged += (_, _) => { _quietStart.Enabled = _quietEnd.Enabled = _quiet.Checked; };

        LoadValues();
    }

    // --- layout helpers -------------------------------------------------------

    private static Label L(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };

    private Label Bold(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) };

    /// <summary>A single auto-sizing row of controls laid left-to-right, vertically centered.</summary>
    private static TableLayoutPanel Inline(params Control[] cs)
    {
        var p = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = cs.Length, RowCount = 1, Margin = new Padding(0) };
        for (int i = 0; i < cs.Length; i++)
        {
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cs[i].Anchor = AnchorStyles.Left;          // anchoring left-only vertically centers in the cell
            cs[i].Margin = new Padding(0, 0, 6, 0);
            p.Controls.Add(cs[i], i, 0);
        }
        return p;
    }

    private static void AddPair(TableLayoutPanel grid, ref int row, Control left, Control right)
    {
        left.Anchor = AnchorStyles.Left;
        right.Anchor = AnchorStyles.Left;
        left.Margin = new Padding(0, 4, 8, 4);
        right.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(left, 0, row);
        grid.Controls.Add(right, 1, row);
        row++;
    }

    private static void AddSpan(TableLayoutPanel grid, ref int row, Control c)
    {
        c.Anchor = AnchorStyles.Left;
        c.Margin = new Padding(0, 4, 0, 2);
        grid.Controls.Add(c, 0, row);
        grid.SetColumnSpan(c, 2);
        row++;
    }

    // --- values ---------------------------------------------------------------

    private void LoadValues()
    {
        _refresh.Value = Math.Clamp(_s.RefreshMinutes, (int)_refresh.Minimum, (int)_refresh.Maximum);
        _pages.Value = Math.Clamp(_s.PagesToScan, (int)_pages.Minimum, (int)_pages.Maximum);
        _stops.Value = Math.Clamp(_s.StopsAwayThreshold, (int)_stops.Minimum, (int)_stops.Maximum);
        _notifyOfd.Checked = _s.NotifyOutForDelivery;
        _notifyDelivered.Checked = _s.NotifyDelivered;
        _notifyStops.Checked = _s.StopsAwayThreshold > 0;
        _stops.Enabled = _notifyStops.Checked;
        _quiet.Checked = _s.QuietHoursEnabled;
        _quietStart.Value = Math.Clamp(_s.QuietStartHour, 0, 23);
        _quietEnd.Value = Math.Clamp(_s.QuietEndHour, 0, 23);
        _quietStart.Enabled = _quietEnd.Enabled = _quiet.Checked;
        _playSound.Checked = _s.PlaySound;
        _speak.Checked = _s.SpeakAloud;
        _startup.Checked = _s.LaunchAtStartup;
    }

    private void Commit()
    {
        _s.RefreshMinutes = (int)_refresh.Value;
        _s.PagesToScan = (int)_pages.Value;
        _s.StopsAwayThreshold = _notifyStops.Checked ? (int)_stops.Value : 0;
        _s.NotifyOutForDelivery = _notifyOfd.Checked;
        _s.NotifyDelivered = _notifyDelivered.Checked;
        _s.QuietHoursEnabled = _quiet.Checked;
        _s.QuietStartHour = (int)_quietStart.Value;
        _s.QuietEndHour = (int)_quietEnd.Value;
        _s.PlaySound = _playSound.Checked;
        _s.SpeakAloud = _speak.Checked;
        _s.LaunchAtStartup = _startup.Checked;
        _s.Save();
    }
}
