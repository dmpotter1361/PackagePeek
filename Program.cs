using Microsoft.Toolkit.Uwp.Notifications;

namespace AmazonTracker;

internal static class Program
{
    private static Mutex? _single;

    [STAThread]
    private static void Main(string[] args)
    {
        // Brand toasts as "Package Peek" (AppUserModelID + Start-Menu shortcut). Must run
        // before any notification is shown.
        ShellBranding.SetProcessAppId();
        ShellBranding.EnsureStartMenuShortcut();

        if (args.Length > 0 && args[0] == "--toasttest")
        {
            RunToastTest();
            return;
        }

        if (args.Length > 0 && args[0] == "--dashshot")
        {
            ApplicationConfiguration.Initialize();
            var f = new DashboardForm();
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-2000, -2000);
            f.Show();
            var sample = new List<OrderInfo>
            {
                new() { Title = "KOTAMU Wax Kit Pink Digital Handle Hard Wax Warmer", StatusText = "Arriving today", EtaText = "today", DeliveryWindow = "7 AM - 11 AM", Stage = DeliveryStage.OutForDelivery, OrderUrl = "https://www.amazon.com" },
                new() { Title = "Seranova Micro Infusion System for Face", StatusText = "Arriving June 29", EtaText = "June 29", Stage = DeliveryStage.Shipped, EtaDate = new DateTime(2026, 6, 29), OrderUrl = "https://www.amazon.com" },
                new() { Title = "The Tarot of Light Pocket Edition", StatusText = "Delivered today", EtaText = "today", Stage = DeliveryStage.Delivered, DeliveredToday = true, OrderUrl = "https://www.amazon.com" },
            };
            f.ShowOrders(sample, DateTime.Now);
            Application.DoEvents();
            using var b = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
            b.Save(Path.Combine(AppSettings.ConfigDir, "dash-shot.png"));
            f.Close();
            return;
        }

        if (args.Length > 0 && args[0] == "--settingsshot")
        {
            ApplicationConfiguration.Initialize();
            var f = new SettingsForm(AppSettings.Load());
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-2000, -2000);
            f.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(Path.Combine(AppSettings.ConfigDir, "settings-shot.png"));
            f.Close();
            return;
        }

        // Only one copy should run (it's a background tray app).
        _single = new Mutex(initiallyOwned: true, "PackagePeek.SingleInstance", out var isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var settings = AppSettings.Load();
        settings.Save(); // materialize defaults on first run so the file is editable

        Application.Run(new TrayContext(settings));

        GC.KeepAlive(_single);
    }

    /// <summary>Fires a single toast and records whether the API succeeded, for reliability testing.</summary>
    private static void RunToastTest()
    {
        var log = Path.Combine(AppSettings.ConfigDir, "toast-test.log");
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigDir);
            new ToastContentBuilder()
                .AddText("Package Peek toast test")
                .AddText("If you can see this notification, rich toasts work!")
                .AddButton(new ToastButton().SetContent("Great").SetProtocolActivation(new Uri("https://www.amazon.com")))
                .Show();
            File.WriteAllText(log, $"OK  {DateTime.Now:G}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(log, $"FAIL  {DateTime.Now:G}\n{ex}");
        }
        // Give the OS a moment to surface the toast before the process exits.
        Thread.Sleep(2500);
    }
}
