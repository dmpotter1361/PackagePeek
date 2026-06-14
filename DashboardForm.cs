using System.Diagnostics;

namespace AmazonTracker;

/// <summary>
/// The window your wife actually looks at: a clean list of in-flight orders grouped
/// by how close they are, each row opening its Amazon order page on double-click.
/// </summary>
public sealed class DashboardForm : Form
{
    private readonly ListView _list = new();
    private readonly Label _status = new();
    private readonly Button _refreshBtn = new();
    private readonly Button _settingsBtn = new();
    private readonly Button _helpBtn = new();

    private readonly Label _banner = new();
    private readonly ImageList _thumbs = new();
    private readonly HashSet<string> _loadedKeys = new();
    private readonly NicknameStore _nicknames = new();
    private readonly Dictionary<string, Image> _fullImages = new();
    private int _sortCol = -1;
    private SortOrder _sortOrder = SortOrder.Ascending;
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PackagePeek/1.0");
        return http;
    }

    /// <summary>Raised when the user clicks Refresh; the app does the actual scrape.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Raised when the user clicks Settings; the app opens the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user clicks Help; the app opens the help window.
    /// (Named to avoid colliding with the built-in Control.HelpRequested event.)</summary>
    public event EventHandler? ShowHelpRequested;

    public DashboardForm()
    {
        Text = "Package Peek - your Amazon deliveries";
        Width = 900;   // fits all columns without a horizontal scrollbar at default size
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 360);

        _thumbs.ImageSize = new Size(48, 48);
        _thumbs.ColorDepth = ColorDepth.Depth32Bit;

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = false;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.SmallImageList = _thumbs;   // shows the thumbnail at the start of each row
        _list.Columns.Add("Item", 280);
        _list.Columns.Add("Status", 150);
        _list.Columns.Add("ETA", 190);
        _list.Columns.Add("Stops", 50);
        _list.Columns.Add("Tracking", 160);
        _list.MouseDoubleClick += OnItemDoubleClick;
        _list.ColumnClick += OnColumnClick;

        // Right-click a row to rename it to something friendly.
        var ctx = new ContextMenuStrip();
        ctx.Items.Add("Rename...", null, (_, _) => RenameSelected());
        ctx.Items.Add("Clear name", null, (_, _) => ClearNameSelected());
        _list.ContextMenuStrip = ctx;
        _list.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var h = _list.HitTest(e.Location);
                if (h.Item != null) h.Item.Selected = true;
            }
        };
        _list.Groups.Add(new ListViewGroup("out", "Almost here"));
        _list.Groups.Add(new ListViewGroup("transit", "On the way"));
        _list.Groups.Add(new ListViewGroup("processing", "Processing"));
        _list.Groups.Add(new ListViewGroup("delivered", "Delivered today"));
        // Always visible like the other groups — no collapse chevron to hunt for at the far right.

        _status.AutoSize = true;
        _status.Anchor = AnchorStyles.Left;
        _status.Text = "Loading...";

        ConfigureBarButton(_helpBtn, "Help", (_, _) => ShowHelpRequested?.Invoke(this, EventArgs.Empty));
        ConfigureBarButton(_settingsBtn, "Settings", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        ConfigureBarButton(_refreshBtn, "Refresh now", (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));

        // Buttons auto-size to their text (survives any font/DPI) and sit on the right.
        var btns = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Right
        };
        btns.Controls.Add(_helpBtn);
        btns.Controls.Add(_settingsBtn);
        btns.Controls.Add(_refreshBtn);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8, 4, 8, 4)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(btns, 1, 0);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "  Double-click a package to open its order page - or double-click its Tracking ID to track the carrier.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText
        };

        _banner.Dock = DockStyle.Top;
        _banner.Height = 32;
        _banner.TextAlign = ContentAlignment.MiddleCenter;
        _banner.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        _banner.BackColor = Color.FromArgb(225, 240, 225);
        _banner.ForeColor = Color.FromArgb(20, 110, 50);
        _banner.Visible = false;

        // Header stacks banner above the hint line.
        var header = new Panel { Dock = DockStyle.Top, Height = 58 };
        header.Controls.Add(hint);
        header.Controls.Add(_banner);
        _banner.BringToFront();

        Controls.Add(_list);
        Controls.Add(header);
        Controls.Add(bottom);
    }

    public void ShowOrders(IReadOnlyList<OrderInfo> orders, DateTime lastUpdated)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var o in orders)
        {
            var display = _nicknames.Get(o.OrderId) ?? o.Title;
            var item = new ListViewItem(display) { Tag = o, Group = GroupFor(o.Stage) };
            if (!string.IsNullOrEmpty(o.ImageUrl)) item.ImageKey = o.ImageUrl;
            item.UseItemStyleForSubItems = false;
            var statusSub = item.SubItems.Add(string.IsNullOrWhiteSpace(o.StatusText) ? StageLabel(o.Stage) : o.StatusText);
            var etaSub = item.SubItems.Add(EtaWithWindow(o));
            item.SubItems.Add(o.StopsAway?.ToString() ?? "");
            var trackSub = item.SubItems.Add(TrackingLabel(o));

            // Color-grade the status + ETA by stage / how overdue it is.
            var (color, bold) = StyleFor(o);
            statusSub.ForeColor = color;
            etaSub.ForeColor = color;
            if (bold)
            {
                var b = new Font(_list.Font, FontStyle.Bold);
                statusSub.Font = b;
                etaSub.Font = b;
            }

            // The tracking cell is always a link: the number when we have it,
            // otherwise a "Track >" that opens the order's live tracker page.
            if (o.Stage != DeliveryStage.Delivered)
            {
                trackSub.ForeColor = Color.FromArgb(0, 102, 204);
                trackSub.Font = new Font(_list.Font, FontStyle.Underline);
            }

            _list.Items.Add(item);
        }
        _list.EndUpdate();

        _ = LoadThumbnailsAsync(orders);

        _status.Text = orders.Count == 0
            ? $"No active orders found.  Updated {lastUpdated:t}."
            : $"{orders.Count} package(s).  Updated {lastUpdated:t}.";

        // Banner: how many are landing today.
        var today = DateTime.Now.Date;
        int arrivingToday = orders.Count(o =>
            o.Stage is DeliveryStage.OutForDelivery
            || (o.Stage != DeliveryStage.Delivered && o.Stage != DeliveryStage.Canceled
                && o.EtaDate is DateTime d && d.Date == today));
        if (arrivingToday > 0)
        {
            _banner.Text = arrivingToday == 1 ? "1 package arriving today" : $"{arrivingToday} packages arriving today";
            _banner.Visible = true;
        }
        else
        {
            _banner.Visible = false;
        }
    }

    public void SetStatus(string message) => _status.Text = message;

    private void ConfigureBarButton(Button b, string text, EventHandler onClick)
    {
        b.Text = text;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Padding = new Padding(8, 3, 8, 3);
        b.Margin = new Padding(4, 3, 0, 3);
        b.Click += onClick;
    }

    private static string EtaWithWindow(OrderInfo o)
    {
        if (string.IsNullOrEmpty(o.DeliveryWindow)) return o.EtaText;
        return string.IsNullOrEmpty(o.EtaText) ? o.DeliveryWindow : $"{o.EtaText}, {o.DeliveryWindow}";
    }

    /// <summary>Download each product thumbnail once and slot it into the list when it arrives.</summary>
    private async Task LoadThumbnailsAsync(IReadOnlyList<OrderInfo> orders)
    {
        var urls = orders.Select(o => o.ImageUrl)
                         .Where(u => !string.IsNullOrEmpty(u) && !_loadedKeys.Contains(u))
                         .Distinct();
        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (IsDisposed) return;
                using var ms = new MemoryStream(bytes);
                var img = Image.FromStream(ms);
                if (InvokeRequired) Invoke(() => AddThumb(url, img));
                else AddThumb(url, img);
            }
            catch
            {
                _loadedKeys.Add(url); // a broken image shouldn't be retried every cycle
            }
        }
    }

    private void AddThumb(string url, Image img)
    {
        if (_thumbs.Images.ContainsKey(url)) return;
        _thumbs.Images.Add(url, img);
        _fullImages[url] = img;   // keep the full-res copy for the enlarge preview
        _loadedKeys.Add(url);
        // Re-stamp the key on matching rows so the list repaints with the new image.
        foreach (ListViewItem it in _list.Items)
            if (string.Equals(it.ImageKey, url, StringComparison.Ordinal))
                it.ImageKey = url;
        _list.Invalidate();
    }

    private ListViewGroup GroupFor(DeliveryStage stage) => stage switch
    {
        DeliveryStage.OutForDelivery => _list.Groups[0],
        DeliveryStage.Shipped => _list.Groups[1],
        DeliveryStage.Processing => _list.Groups[2],
        DeliveryStage.Delivered => _list.Groups[3],
        _ => _list.Groups[1]
    };

    /// <summary>Color + weight for an order's status/ETA based on its stage and how overdue it is.</summary>
    private static (Color color, bool bold) StyleFor(OrderInfo o)
    {
        // Overdue takes priority (red at 3+ days late, amber at 1–2). A future ETA is
        // never overdue; only known-in-transit items qualify. See OrderLogic.Overdue.
        switch (OrderLogic.Overdue(o.Stage, o.EtaDate, DateTime.Now))
        {
            case OrderLogic.Urgency.VeryLate: return (Color.FromArgb(200, 30, 30), true);
            case OrderLogic.Urgency.Late: return (Color.FromArgb(214, 128, 0), true);
        }

        return o.Stage switch
        {
            DeliveryStage.Processing     => (Color.Black, false),
            DeliveryStage.Shipped        => (Color.FromArgb(70, 150, 210), false), // light blue
            DeliveryStage.OutForDelivery => (Color.FromArgb(20, 70, 170), true),   // darker blue
            DeliveryStage.Delivered      => (Color.FromArgb(30, 140, 60), false),  // green
            _                            => (SystemColors.ControlText, false)
        };
    }

    private static string StageLabel(DeliveryStage stage) => stage switch
    {
        DeliveryStage.OutForDelivery => "Out for delivery",
        DeliveryStage.Shipped => "In transit",
        DeliveryStage.Processing => "Processing",
        DeliveryStage.Delivered => "Delivered",
        _ => "Status pending"
    };

    private const int ColTracking = 4;

    private void OnItemDoubleClick(object? sender, MouseEventArgs e)
    {
        var hit = _list.HitTest(e.Location);
        if (hit.Item?.Tag is not OrderInfo o) return;

        int col = hit.Item.SubItems.IndexOf(hit.SubItem);

        // Double-click the thumbnail (far left of the Item cell) → enlarge it.
        if (col == 0 && e.X < 52 && !string.IsNullOrEmpty(o.ImageUrl)
            && _fullImages.TryGetValue(o.ImageUrl, out var full))
        {
            ShowImagePreview(hit.Item.Text, full);
            return;
        }

        // Double-click the Tracking cell → carrier/live tracking; anywhere else → order page.
        Open(col == ColTracking ? TrackingUrl(o) : o.OrderUrl);
    }

    private void RenameSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not OrderInfo o) return;
        var item = _list.SelectedItems[0];
        var current = _nicknames.Get(o.OrderId) ?? o.Title;
        var input = PromptDialog.Show("Rename package", "Show this package as:", current);
        if (input is null) return;
        _nicknames.Set(o.OrderId, input);
        item.Text = string.IsNullOrWhiteSpace(input) ? o.Title : input.Trim();
    }

    private void ClearNameSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not OrderInfo o) return;
        _nicknames.Set(o.OrderId, null);
        _list.SelectedItems[0].Text = o.Title;
    }

    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == _sortCol)
            _sortOrder = _sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        else { _sortCol = e.Column; _sortOrder = SortOrder.Ascending; }
        _list.ListViewItemSorter = new RowComparer(_sortCol, _sortOrder);
        _list.Sort();
    }

    private static void ShowImagePreview(string title, Image image)
    {
        using var f = new Form
        {
            Text = title.Length > 60 ? title[..57] + "..." : title,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 320),
            MaximizeBox = false,
            MinimizeBox = false
        };
        var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image };
        pb.Click += (_, _) => f.Close();
        f.Controls.Add(pb);
        f.ShowDialog();
    }

    /// <summary>Sorts rows by a column; ETA chronologically, Stops numerically, else by text.</summary>
    private sealed class RowComparer : System.Collections.IComparer
    {
        private readonly int _col;
        private readonly int _dir;
        public RowComparer(int col, SortOrder order) { _col = col; _dir = order == SortOrder.Descending ? -1 : 1; }

        public int Compare(object? a, object? b)
        {
            var ia = (ListViewItem)a!;
            var ib = (ListViewItem)b!;
            int c;
            if (_col == 2) // ETA
                c = Nullable.Compare((ia.Tag as OrderInfo)?.EtaDate, (ib.Tag as OrderInfo)?.EtaDate);
            else if (_col == 3) // Stops
                c = ParseInt(ia.SubItems[3].Text).CompareTo(ParseInt(ib.SubItems[3].Text));
            else
                c = string.Compare(ia.SubItems[_col].Text, ib.SubItems[_col].Text, StringComparison.OrdinalIgnoreCase);
            return c * _dir;
        }

        private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : int.MaxValue;
    }

    private static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* nothing we can do if the shell refuses */ }
    }

    private static string TrackingLabel(OrderInfo o)
    {
        // Delivered items don't need a tracking action.
        if (o.Stage == DeliveryStage.Delivered) return "";
        // No number on the list card → an actionable link to the live tracker instead of a blank.
        if (string.IsNullOrEmpty(o.TrackingId)) return "Track >";
        var carrier = string.IsNullOrEmpty(o.Carrier) ? InferCarrier(o.TrackingId) : o.Carrier;
        return string.IsNullOrEmpty(carrier) ? o.TrackingId : $"{carrier} - {o.TrackingId}";
    }

    /// <summary>Best-guess carrier from the tracking-number shape when Amazon didn't name one.</summary>
    private static string InferCarrier(string t)
    {
        t = t.ToUpperInvariant();
        if (t.StartsWith("TBA")) return "Amazon";
        if (t.StartsWith("1Z")) return "UPS";
        if (t.StartsWith("FDX") || (t.Length is 12 or 15 && t.All(char.IsDigit))) return "FedEx";
        if (t.StartsWith("9") && t.Length >= 20) return "USPS";
        return "";
    }

    /// <summary>Build a carrier tracking URL. Amazon's own numbers (TBA…) aren't on a public
    /// carrier site, so we fall back to the Amazon order page for those.</summary>
    private static string TrackingUrl(OrderInfo o)
    {
        var t = o.TrackingId.ToUpperInvariant();
        var carrier = (string.IsNullOrEmpty(o.Carrier) ? InferCarrier(t) : o.Carrier).ToUpperInvariant();

        if (carrier.Contains("UPS")) return $"https://www.ups.com/track?loc=en_US&tracknum={t}";
        if (carrier.Contains("USPS")) return $"https://tools.usps.com/go/TrackConfirmAction?tLabels={t}";
        if (carrier.Contains("FEDEX")) return $"https://www.fedex.com/fedextrack/?trknbr={t}";
        if (carrier.Contains("DHL")) return $"https://www.dhl.com/us-en/home/tracking.html?tracking-id={t}";

        // Amazon Logistics (TBA…) or unknown → the order page has the live tracker.
        return o.OrderUrl;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X just hides the dashboard back to the tray; the app keeps running.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            base.OnFormClosing(e);
        }
    }
}
