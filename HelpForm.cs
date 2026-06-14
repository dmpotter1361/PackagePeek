namespace AmazonTracker;

/// <summary>
/// A friendly, scrollable in-app guide to every feature. Uses a RichTextBox so the
/// headings/spacing render crisply at any DPI without manual positioning.
/// </summary>
public sealed class HelpForm : Form
{
    private readonly RichTextBox _text = new();

    public HelpForm()
    {
        Text = "Package Peek - Help";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 600);
        MinimumSize = new Size(460, 360);

        _text.Dock = DockStyle.Fill;
        _text.ReadOnly = true;
        _text.BorderStyle = BorderStyle.None;
        _text.BackColor = Color.White;
        _text.Margin = new Padding(0);
        _text.Padding = new Padding(14);
        _text.ScrollBars = RichTextBoxScrollBars.Vertical;
        _text.DetectUrls = true;
        _text.LinkClicked += (_, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText!) { UseShellExecute = true }); } catch { } };
        Controls.Add(_text);

        Build();
        _text.SelectionStart = 0;
        _text.ScrollToCaret();
    }

    private void Title(string s)
    {
        _text.SelectionFont = new Font("Segoe UI", 15f, FontStyle.Bold);
        _text.SelectionColor = Color.FromArgb(20, 70, 170);
        _text.AppendText(s + "\n");
    }

    private void Heading(string s)
    {
        _text.AppendText("\n");
        _text.SelectionFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _text.SelectionColor = Color.FromArgb(30, 30, 30);
        _text.AppendText(s + "\n");
    }

    private void Body(string s)
    {
        _text.SelectionFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        _text.SelectionColor = Color.FromArgb(40, 40, 40);
        _text.AppendText(s + "\n");
    }

    private void Bullet(string s) => Body("   •  " + s);

    private void Build()
    {
        Title("Package Peek");
        Body("Keeps an eye on your Amazon deliveries and pops up when a package is almost there. " +
             "It runs quietly in the system tray (next to the clock) and checks your orders on a schedule.");

        Heading("Getting started");
        Bullet("The first time it runs, a window opens for you to sign in to Amazon - sign in like normal (including any 2-step code).");
        Bullet("Once you reach your Orders page, just close that window. It stays signed in and refreshes on its own.");
        Bullet("Single-click the tray icon any time to open the dashboard.");

        Heading("The dashboard");
        Bullet("Packages are grouped by how close they are: Almost here, On the way, Processing, and Delivered today.");
        Bullet("Colors show status at a glance - light blue = shipped, dark blue = out for delivery, green = delivered, " +
               "amber = a day or two late, red = 3+ days late (when you can ask Amazon about it).");
        Bullet("Each row shows the item, its status, the estimated arrival (and delivery time window when Amazon gives one).");
        Bullet("Double-click a row to open that order on Amazon. Double-click the Tracking cell to open live carrier tracking.");
        Bullet("Double-click a product picture to see it bigger.");
        Bullet("Right-click a row to give it a friendly name (e.g. rename a cryptic listing to \"Mom's birthday gift\").");
        Bullet("Click a column header to sort by it.");

        Heading("Notifications");
        Bullet("A popup appears when a package goes out for delivery, when it's within a few stops, and when it's delivered.");
        Bullet("Popups show the product photo with Track and View-order buttons - click the popup to open the order.");
        Bullet("You choose which popups you want, and can turn on a chime and/or have alerts read aloud.");
        Bullet("Quiet hours stop popups overnight (or whenever you set).");

        Heading("The tray icon");
        Bullet("A small red number on the tray icon means that many packages are out for delivery right now.");
        Bullet("Right-click the tray icon for the menu: open the dashboard, refresh now, sign in to Amazon, add today's arrivals to your calendar, settings, help, and quit.");

        Heading("Settings");
        Bullet("How often to check Amazon, how many order pages to scan, which popups fire, quiet hours, sound, and start-with-Windows.");
        Bullet("Use \"Send test popup\" to preview what an alert looks like.");

        Heading("Calendar");
        Bullet("Tray menu -> \"Add today's arrivals to calendar\" drops today's expected deliveries into your calendar app.");

        Heading("Privacy");
        Bullet("Everything stays on this PC. The app only talks to Amazon (your own login) and the carriers' tracking pages.");
        Bullet("There's no account, no tracking, and nothing is sent anywhere. Your Amazon password is never seen or stored.");

        Heading("Tips");
        Bullet("Closing the dashboard or the sign-in window doesn't quit the app - it keeps running in the tray.");
        Bullet("If Amazon ever signs you out, use the tray menu's \"Sign in to Amazon\" to sign back in.");
        Bullet("Quit any time from the tray menu.");
    }
}
