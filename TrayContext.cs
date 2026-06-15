using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AmazonTracker;

/// <summary>
/// The app's brain. Lives in the system tray, drives the refresh timer, owns the
/// hidden browser and the dashboard, and decides when a package is worth a popup.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly BrowserHost _browser;
    private readonly DashboardForm _dashboard;

    private readonly Icon _appIcon;
    private readonly Bitmap _baseBitmap;
    private IntPtr _badgeHandle = IntPtr.Zero;
    private Icon? _badgeIcon;
    private System.Speech.Synthesis.SpeechSynthesizer? _synth;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    // Remembers which popups we've already fired per shipment so we don't nag every cycle.
    private readonly Dictionary<string, HashSet<string>> _firedTriggers = new();
    private List<OrderInfo> _lastOrders = new();
    private bool _busy;

    public TrayContext(AppSettings settings)
    {
        _settings = settings;

        _browser = new BrowserHost(settings);
        _browser.SignInRequired += async (_, _) => await PromptSignInAsync();
        _browser.LoginWindowClosed += async (_, _) => await RefreshAsync(userInitiated: true);

        _dashboard = new DashboardForm();
        _dashboard.RefreshRequested += async (_, _) => await RefreshAsync(userInitiated: true);
        _dashboard.SettingsRequested += (_, _) => ShowSettings();
        _dashboard.ShowHelpRequested += (_, _) => ShowHelp();

        var appIcon = LoadAppIcon();
        if (appIcon is not null)
        {
            _dashboard.Icon = appIcon;
            _browser.Icon = appIcon;
        }
        _appIcon = appIcon ?? SystemIcons.Information;
        _baseBitmap = _appIcon.ToBitmap();

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Package Peek",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        // A single left-click opens the dashboard (right-click still shows the menu).
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };
        _tray.BalloonTipClicked += OnBalloonClicked;

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, _settings.RefreshMinutes) * 60_000 };
        _timer.Tick += async (_, _) => await RefreshAsync(userInitiated: false);

        ApplyStartupShortcut();

        // Kick off the first cycle shortly after launch.
        var startup = new System.Windows.Forms.Timer { Interval = 1500 };
        startup.Tick += async (s, _) =>
        {
            startup.Stop();
            startup.Dispose();
            _timer.Start();
            await RefreshAsync(userInitiated: false);
        };
        startup.Start();
    }

    /// <summary>Overlay a small red count badge on the tray icon (or restore the plain icon at 0).</summary>
    private void UpdateTrayBadge(int count)
    {
        if (count <= 0)
        {
            _tray.Icon = _appIcon;
            ReleaseBadge();
            return;
        }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawImage(_baseBitmap, 0, 0, 32, 32);

            int d = 18, x = 32 - d, y = 32 - d;
            g.FillEllipse(Brushes.Crimson, x, y, d - 1, d - 1);
            using var ring = new Pen(Color.White, 1.5f);
            g.DrawEllipse(ring, x, y, d - 1, d - 1);

            var s = count > 9 ? "9+" : count.ToString();
            using var f = new Font("Segoe UI", count > 9 ? 7f : 8.5f, FontStyle.Bold, GraphicsUnit.Point);
            var sz = g.MeasureString(s, f);
            g.DrawString(s, f, Brushes.White, x + (d - 1 - sz.Width) / 2, y + (d - 1 - sz.Height) / 2);
        }

        var newHandle = bmp.GetHicon();
        var newIcon = Icon.FromHandle(newHandle);
        _tray.Icon = newIcon;

        // Free the previous generated icon only after the new one is in place.
        ReleaseBadge();
        _badgeIcon = newIcon;
        _badgeHandle = newHandle;
    }

    private void ReleaseBadge()
    {
        _badgeIcon?.Dispose();
        _badgeIcon = null;
        if (_badgeHandle != IntPtr.Zero) { DestroyIcon(_badgeHandle); _badgeHandle = IntPtr.Zero; }
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { /* fall back to a system icon */ }
        return null;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync(userInitiated: true));
        menu.Items.Add("Sign in to Amazon...", null, async (_, _) => await PromptSignInAsync());
        menu.Items.Add("Add today's arrivals to calendar", null, (_, _) => ExportTodayToCalendar());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Help", null, (_, _) => ShowHelp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        return menu;
    }

    private void ShowDashboard()
    {
        _dashboard.Show();
        _dashboard.WindowState = FormWindowState.Normal;
        _dashboard.BringToFront();
        _dashboard.Activate();
        _dashboard.ShowOrders(_lastOrders, DateTime.Now);

        // If we don't have orders yet (just opened, or right after sign-in), pull them now.
        if (_lastOrders.Count == 0 && !_busy)
            _ = RefreshAsync(userInitiated: true);
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings) { Icon = LoadAppIcon() };
        form.TestNotificationRequested += (_, _) => ShowTestNotification();
        if (form.ShowDialog() != DialogResult.OK) return;

        // Apply the live bits immediately — no restart needed.
        _timer.Interval = Math.Max(1, _settings.RefreshMinutes) * 60_000;
        ApplyStartupShortcut();
    }

    private HelpForm? _help;

    private void ShowHelp()
    {
        if (_help is null || _help.IsDisposed)
            _help = new HelpForm { Icon = LoadAppIcon() };
        _help.Show();
        _help.WindowState = FormWindowState.Normal;
        _help.BringToFront();
        _help.Activate();
    }

    private async Task PromptSignInAsync()
    {
        _tray.Text = "Package Peek - signing in";
        await _browser.ShowForLoginAsync();
    }

    private async Task RefreshAsync(bool userInitiated)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (userInitiated) _dashboard.SetStatus("Checking Amazon...");

            var orders = await _browser.RefreshAsync();
            if (orders is null)
            {
                // Sign-in required; BrowserHost already raised SignInRequired.
                _dashboard.SetStatus("Please sign in to Amazon to continue.");
                return;
            }

            // Drop canceled orders (never arriving) and anything delivered before today
            // (assumed already picked up).
            orders = orders
                .Where(o => o.Stage != DeliveryStage.Canceled)
                .Where(o => o.Stage != DeliveryStage.Delivered || o.DeliveredToday)
                .ToList();

            _lastOrders = orders;
            EvaluateNotifications(orders);

            if (_dashboard.Visible)
                _dashboard.ShowOrders(orders, DateTime.Now);

            var active = orders.Count(o => o.Stage != DeliveryStage.Delivered);
            var arrivingToday = orders.Count(o => o.Stage == DeliveryStage.OutForDelivery);
            _tray.Text = arrivingToday > 0
                ? $"Package Peek - {arrivingToday} arriving today, {active} in transit"
                : $"Package Peek - {active} in transit";
            UpdateTrayBadge(arrivingToday);
        }
        catch (Exception ex)
        {
            _dashboard.SetStatus("Couldn't read Amazon: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Decide which packages deserve a popup, firing each trigger at most once.</summary>
    private void EvaluateNotifications(List<OrderInfo> orders)
    {
        foreach (var o in orders)
        {
            if (string.IsNullOrEmpty(o.ShipmentKey)) continue;
            var fired = _firedTriggers.TryGetValue(o.ShipmentKey, out var set) ? set : (_firedTriggers[o.ShipmentKey] = new());

            if (_settings.NotifyDelivered && o.Stage == DeliveryStage.Delivered && fired.Add("delivered"))
            {
                NotifyOrder("Delivered", $"{o.Title}\n{o.EtaText}".Trim(), o);
                continue;
            }

            if (_settings.StopsAwayThreshold > 0 && o.StopsAway is int stops && stops <= _settings.StopsAwayThreshold
                && fired.Add($"stops<={_settings.StopsAwayThreshold}"))
            {
                NotifyOrder("Almost here!", $"{o.Title}\nOnly {stops} stop(s) away", o);
                continue;
            }

            if (_settings.NotifyOutForDelivery && o.Stage == DeliveryStage.OutForDelivery && fired.Add("ofd"))
            {
                NotifyOrder("Out for delivery", $"{o.Title}\n{o.EtaText}".Trim(), o);
            }
        }
    }

    private string? _lastBalloonUrl;

    private static readonly HttpClient ToastHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Rich notification for a package: a Windows toast with the product photo and
    /// Track / View-order buttons, falling back to a tray balloon if toasts aren't available.</summary>
    private void NotifyOrder(string title, string message, OrderInfo o, bool force = false)
    {
        if (!force && _settings.IsQuietNow()) return;

        if (!TryShowToast(title, message, o.ImageUrl, o.OrderUrl, BuildTrackUrl(o)))
            ShowBalloon(title, message, o.OrderUrl);

        // The toast is muted (see TryShowToast), so we always play the user's chosen
        // sound here — exactly one sound, never a Windows + custom double.
        PlayChime();
        Speak(title, message);
    }

    /// <summary>Simple notification (no order context) — always a balloon.</summary>
    private void Notify(string title, string message, string url, bool force = false)
    {
        if (!force && _settings.IsQuietNow()) return;
        ShowBalloon(title, message, url);
        PlayChime();
        Speak(title, message);
    }

    private void ShowBalloon(string title, string message, string url)
    {
        _lastBalloonUrl = url;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = message;
        _tray.ShowBalloonTip(10_000);
    }

    private void PlayChime()
    {
        if (_settings.PlaySound)
            Sounds.Play(_settings.SoundChoice, _settings.SoundVolume);
    }

    private void Speak(string title, string message)
    {
        if (!_settings.SpeakAloud) return;
        try
        {
            _synth ??= new System.Speech.Synthesis.SpeechSynthesizer();
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync($"{title.Replace("!", "")}. {message.Replace("\n", ", ")}");
        }
        catch { /* speech engine unavailable */ }
    }

    private bool TryShowToast(string title, string message, string? imageUrl, string orderUrl, string trackUrl)
    {
        try
        {
            var b = new ToastContentBuilder().AddText(title).AddText(message);

            // Mute the toast's own audio; we play the user's chosen sound ourselves so
            // there's never a double (Windows sound + custom sound).
            b.AddAudio(new Uri("ms-winsoundevent:Notification.Default"), silent: true);

            var localImg = TryCacheImage(imageUrl);
            if (localImg is not null) b.AddAppLogoOverride(new Uri(localImg));

            if (!string.IsNullOrEmpty(trackUrl))
                b.AddButton(new ToastButton().SetContent("Track").SetProtocolActivation(new Uri(trackUrl)));
            if (!string.IsNullOrEmpty(orderUrl))
            {
                b.AddButton(new ToastButton().SetContent("View order").SetProtocolActivation(new Uri(orderUrl)));
                b.SetProtocolActivation(new Uri(orderUrl)); // clicking the toast body opens the order
            }

            b.Show();
            return true;
        }
        catch
        {
            return false; // unpackaged toast can fail on some setups — caller falls back to balloon
        }
    }

    private static string? TryCacheImage(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var file = Path.Combine(Path.GetTempPath(), $"pp-toast-{Math.Abs(url.GetHashCode())}.jpg");
            if (!File.Exists(file))
                File.WriteAllBytes(file, ToastHttp.GetByteArrayAsync(url).GetAwaiter().GetResult());
            return file;
        }
        catch { return null; }
    }

    private static string BuildTrackUrl(OrderInfo o)
    {
        if (string.IsNullOrEmpty(o.TrackingId)) return o.OrderUrl;
        var t = o.TrackingId.ToUpperInvariant();
        var carrier = o.Carrier.ToUpperInvariant();
        if (t.StartsWith("1Z") || carrier.Contains("UPS")) return $"https://www.ups.com/track?loc=en_US&tracknum={t}";
        if (carrier.Contains("USPS") || (t.StartsWith("9") && t.Length >= 20)) return $"https://tools.usps.com/go/TrackConfirmAction?tLabels={t}";
        if (carrier.Contains("FEDEX")) return $"https://www.fedex.com/fedextrack/?trknbr={t}";
        return o.OrderUrl; // Amazon Logistics / unknown → order page has the live tracker
    }

    /// <summary>Write today's arrivals to a .ics file and open it, so the OS calendar imports them.</summary>
    private void ExportTodayToCalendar()
    {
        var today = DateTime.Now.Date;
        var items = _lastOrders.Where(o =>
            o.Stage != DeliveryStage.Delivered && o.Stage != DeliveryStage.Canceled
            && (o.Stage == DeliveryStage.OutForDelivery || o.EtaDate?.Date == today)).ToList();

        if (items.Count == 0)
        {
            Notify("Nothing arriving today", "No packages are scheduled to arrive today.", "", force: true);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//Package Peek//EN");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStr = today.ToString("yyyyMMdd");
        foreach (var o in items)
        {
            var title = o.Title.Length > 60 ? o.Title[..57] + "..." : o.Title;
            var detail = string.Join(" ", new[] { o.StatusText, o.EtaText, o.DeliveryWindow }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{o.OrderId}-{dateStr}@packagepeek");
            sb.AppendLine($"DTSTAMP:{stamp}");
            sb.AppendLine($"DTSTART;VALUE=DATE:{dateStr}");
            sb.AppendLine($"DTEND;VALUE=DATE:{today.AddDays(1):yyyyMMdd}");
            sb.AppendLine($"SUMMARY:{Escape("Amazon: " + title)}");
            sb.AppendLine($"DESCRIPTION:{Escape(detail + "  " + o.OrderUrl)}");
            sb.AppendLine("END:VEVENT");
        }
        sb.AppendLine("END:VCALENDAR");

        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"package-peek-{dateStr}.ics");
            File.WriteAllText(path, sb.ToString());
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notify("Couldn't open calendar", ex.Message, "", force: true);
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\n", " ");
    }

    private void ShowTestNotification()
    {
        var sample = _lastOrders.FirstOrDefault(o => !string.IsNullOrEmpty(o.ImageUrl))
            ?? new OrderInfo { Title = "Teamoy Cloth Panty Liners", OrderUrl = _settings.AmazonBaseUrl };
        NotifyOrder("Out for delivery", $"{sample.Title}\nArriving today, by 3 PM", sample, force: true);
    }

    private void OnBalloonClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastBalloonUrl))
        {
            try { Process.Start(new ProcessStartInfo(_lastBalloonUrl) { UseShellExecute = true }); }
            catch { /* ignore */ }
        }
    }

    /// <summary>Add/remove a Startup-folder shortcut so the app launches with Windows.</summary>
    private void ApplyStartupShortcut()
    {
        try
        {
            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var linkPath = Path.Combine(startupDir, "Package Peek.lnk");
            var exe = Environment.ProcessPath ?? "";

            if (_settings.LaunchAtStartup && !string.IsNullOrEmpty(exe))
            {
                // Use a .url-style shortcut via WScript.Shell COM to avoid extra deps.
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                var sc = shell.CreateShortcut(linkPath);
                sc.TargetPath = exe;
                sc.WorkingDirectory = Path.GetDirectoryName(exe);
                sc.Description = "Package Peek - Amazon delivery tracker";
                sc.Save();
            }
            else if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
        }
        catch
        {
            // Startup registration is a nicety, not worth crashing over.
        }
    }

    private void Quit()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        ReleaseBadge();
        _synth?.Dispose();
        ExitThread();
    }
}
