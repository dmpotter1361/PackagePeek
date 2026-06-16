using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AmazonTracker;

/// <summary>
/// Hosts an embedded Edge/WebView2 browser with a PERSISTENT profile, so once the
/// user signs into Amazon the session sticks across runs. Normally hidden; we only
/// surface the window when Amazon needs an interactive sign-in (first run or when
/// the session expires / hits a 2FA challenge).
/// </summary>
public sealed class BrowserHost : Form
{
    private readonly WebView2 _web = new();
    private readonly AppSettings _settings;
    private readonly string _extractScript;
    private bool _ready;

    // Parked far off-screen when running in the background. We keep the window SHOWN
    // (not minimized/hidden) at full size so WebView2 renders and — critically —
    // routes mouse clicks correctly. A WebView2 created while minimized eats clicks.
    private static readonly Point OffScreen = new(-32000, -32000);

    public event EventHandler? SignInRequired;

    /// <summary>Raised when the user closes the sign-in window, so we can resync right away.</summary>
    public event EventHandler? LoginWindowClosed;

    public BrowserHost(AppSettings settings)
    {
        _settings = settings;

        Text = "Package Peek - Amazon sign-in";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.Manual;
        Location = OffScreen;
        Size = new Size(1000, 800);
        ShowInTaskbar = false;

        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Resources", "extract.js");
        _extractScript = File.Exists(scriptPath) ? File.ReadAllText(scriptPath) : "[]";
    }

    public async Task EnsureReadyAsync()
    {
        if (_ready) return;

        // Show the window off-screen first so the control gets a real handle and a
        // non-zero size — WebView2 needs both to initialize input/rendering properly.
        if (!Visible) Show();

        // Keep the WebView2 user data (cookies, session) in our own app folder so it
        // persists and is isolated from the user's normal Edge profile.
        var userDataFolder = Path.Combine(AppSettings.ConfigDir, "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _web.EnsureCoreWebView2Async(env);

        // Look like a normal desktop browser; some Amazon flows behave oddly otherwise.
        _web.CoreWebView2.Settings.UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0";

        _ready = true;
    }

    private string OrdersUrl => _settings.AmazonBaseUrl.TrimEnd('/') + "/gp/css/order-history?ref_=nav_orders_first";

    /// <summary>True when the current page is an Amazon sign-in / auth challenge.</summary>
    private bool IsSignInPage()
    {
        var url = _web.CoreWebView2?.Source ?? "";
        return url.Contains("/ap/signin", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/ap/mfa", StringComparison.OrdinalIgnoreCase)
            || url.Contains("signin", StringComparison.OrdinalIgnoreCase) && !url.Contains("order", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walk the first few orders pages and scrape them. Returns null when an
    /// interactive sign-in is required (caller should surface the login window).
    /// </summary>
    public async Task<List<OrderInfo>?> RefreshAsync(CancellationToken ct = default)
    {
        await EnsureReadyAsync();
        await NavigateAsync(OrdersUrl, ct);

        if (IsSignInPage())
        {
            SignInRequired?.Invoke(this, EventArgs.Empty);
            return null;
        }

        var all = new List<OrderInfo>();
        var seen = new HashSet<string>();
        string? nextUrl = null;
        var pages = Math.Max(1, _settings.PagesToScan);

        for (var page = 0; page < pages; page++)
        {
            // Page 0 is already loaded (we navigated to OrdersUrl above); after that,
            // follow the Next link the previous page handed us.
            if (page > 0)
            {
                if (string.IsNullOrEmpty(nextUrl)) break;
                await NavigateAsync(nextUrl, ct);
                if (IsSignInPage()) break;
            }

            // Give lazy-loaded order cards a moment to render.
            await Task.Delay(1200, ct);

            var raw = await _web.CoreWebView2.ExecuteScriptAsync(_extractScript);
            var pageData = ParseExtractPage(raw);

            // Persist the first page's raw diagnostic for extractor tuning — only when
            // debugging is explicitly enabled (it contains the raw page incl. name/address).
            if (page == 0)
            {
                var dbgPath = Path.Combine(AppSettings.ConfigDir, "debug-page.json");
                if (DebugEnabled)
                {
                    try { File.WriteAllText(dbgPath, JsonSerializer.Serialize(pageData.Debug, new JsonSerializerOptions { WriteIndented = true })); }
                    catch { }
                }
                else TryDelete(dbgPath);
            }

            foreach (var o in pageData.Orders)
                if (seen.Add(o.ShipmentKey))
                    all.Add(o);

            nextUrl = pageData.NextUrl;
        }

        DumpForDebug(all);
        return all;
    }

    /// <summary>Opt-in diagnostics via env var. Off by default so no order history / PII
    /// is written to disk on a normal install.</summary>
    private static bool DebugEnabled =>
        Environment.GetEnvironmentVariable("PACKAGEPEEK_DEBUG") == "1";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Write the raw scrape to a file for extractor tuning — debug-gated, since
    /// it contains order titles (potentially sensitive purchases).</summary>
    private static void DumpForDebug(List<OrderInfo> orders)
    {
        var path = Path.Combine(AppSettings.ConfigDir, "last-scrape.json");
        if (!DebugEnabled) { TryDelete(path); return; } // clean up any file left by a prior version
        try
        {
            var rows = orders.Select(o => new
            {
                o.Title, o.StatusText, stage = o.Stage.ToString(),
                o.EtaText, o.StopsAway, o.Delayed, o.OrderId, hasImage = !string.IsNullOrEmpty(o.ImageUrl),
                o.RawText
            });
            File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* debugging aid only */ }
    }

    /// <summary>Bring the browser on-screen so the user can sign in.</summary>
    public async Task ShowForLoginAsync(CancellationToken ct = default)
    {
        await EnsureReadyAsync();
        MoveOnScreen();

        // Only navigate if we're not already sitting on a sign-in page. Re-navigating
        // mid-login reloads the form and discards whatever the user just typed.
        if (!IsSignInPage())
            await NavigateAsync(OrdersUrl, ct);
    }

    private void MoveOnScreen()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
        Show();
        BringToFront();
        Activate();
        _web.Focus();
    }

    private void MoveOffScreen()
    {
        ShowInTaskbar = false;
        Location = OffScreen;
    }

    public bool LooksSignedIn() => _ready && !IsSignInPage();

    private Task NavigateAsync(string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _web.CoreWebView2.NavigationCompleted -= Handler;
            tcs.TrySetResult();
        }
        _web.CoreWebView2.NavigationCompleted += Handler;
        ct.Register(() => tcs.TrySetCanceled());
        _web.CoreWebView2.Navigate(url);
        return tcs.Task;
    }

    private sealed class ExtractPage
    {
        [JsonPropertyName("orders")] public List<OrderInfo> Orders { get; set; } = new();
        [JsonPropertyName("nextUrl")] public string NextUrl { get; set; } = "";
        [JsonPropertyName("debug")] public JsonElement Debug { get; set; }
    }

    private static ExtractPage ParseExtractPage(string rawJsonStringLiteral)
    {
        // ExecuteScriptAsync returns a JSON-encoded value; our script returns a JSON
        // *string*, so it comes back double-encoded. Unwrap once, then parse.
        var page = new ExtractPage();
        try
        {
            var inner = JsonSerializer.Deserialize<string>(rawJsonStringLiteral);
            if (string.IsNullOrWhiteSpace(inner)) return page;

            var parsed = JsonSerializer.Deserialize<ExtractPage>(inner);
            if (parsed is null) return page;

            foreach (var o in parsed.Orders)
            {
                o.ShipmentKey = o.OrderId + "|" + o.Title;
                o.Stage = OrderLogic.Classify(o.StatusText);
                o.EtaDate = OrderLogic.ParseWhen(o.EtaText, DateTime.Now);
                // No reliable status => Unknown (neutral). We don't fabricate a transit
                // stage from the ETA alone; the real status line is the source of truth.

                if (o.Stage == DeliveryStage.Delivered)
                    o.DeliveredToday = OrderLogic.DeliveredToday(o.StatusText + " " + o.EtaText, DateTime.Now);
            }
            page = parsed;
        }
        catch
        {
            // Extraction shape changed or page wasn't what we expected — return what we have.
        }
        return page;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the login window should just park it off-screen, not tear down the
        // session (and not minimize — that breaks WebView2 input on the next login).
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MoveOffScreen();
            // Likely just finished signing in — resync immediately rather than waiting.
            LoginWindowClosed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            base.OnFormClosing(e);
        }
    }
}
