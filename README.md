# Package Peek

A small Windows tray app that keeps an eye on your Amazon deliveries and pops up
when a package is almost there. It lives in the system tray, checks your orders on
a schedule, and shows what's processing, in transit, and arriving today — with
desktop notifications, live carrier tracking links, and color-graded statuses.

> Personal project. Not affiliated with, endorsed by, or sponsored by Amazon.

## Features

- **Tray app** — runs quietly next to the clock; single-click to open the dashboard.
- **Sign in once** — an embedded browser signs into *your* Amazon account; the session persists.
- **Dashboard** — packages grouped by *Almost here / On the way / Processing / Delivered today*, with:
  - color grading by stage and lateness (amber 1–2 days late, red 3+ days late),
  - product thumbnails (double-click to enlarge),
  - estimated arrival and delivery time window,
  - clickable carrier **Tracking** (UPS/USPS/FedEx/DHL/Amazon),
  - per-item **nicknames** (right-click to rename), and column sorting.
- **Notifications** — rich Windows toasts with the product photo and *Track* / *View order*
  buttons for out-for-delivery, stops-away, and delivered. Optional chime, text-to-speech,
  and quiet hours.
- **Live tray badge** — a red count when packages are out for delivery.
- **Calendar export** — drop today's expected arrivals into your calendar.
- **In-app Help** — a built-in guide to every feature.

## Privacy

Everything stays on your PC. The app talks only to Amazon (your own login) and the
carriers' tracking pages — there's no account, no telemetry, and nothing is sent
anywhere. Your Amazon password is never seen or stored (you sign in to the real
Amazon page inside the app). Diagnostic dumps are off by default and only enabled
with the `PACKAGEPEEK_DEBUG=1` environment variable.

## Install

Download the latest `PackagePeek-x.y.z-x64.msi` from
[Releases](../../releases) and run it. Because it's a personal/beta build it isn't
code-signed yet, so Windows SmartScreen may warn — choose **More info → Run anyway**.

Installing a newer version automatically removes the old one (no manual uninstall).

**Requirements:** Windows 10/11 (x64) with the WebView2 runtime (preinstalled on
Windows 11 and updated Windows 10). The .NET runtime is bundled, so nothing else is needed.

## Build from source

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- WiX v5 (for the installer): `dotnet tool install --global wix --version 5.0.2`

```powershell
# Run the app
dotnet run --project AmazonTracker.csproj

# Run the tests
dotnet test

# Build the installer (publishes self-contained, then builds the MSI)
pwsh ./build.ps1 -Version 0.1.2
```

## How it works

Amazon has no public API for personal order tracking, so Package Peek uses an
embedded [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)
browser with a persistent profile: you sign in once, and on a timer it quietly
reopens the *Your Orders* page and reads each shipment's status, ETA, and tracking
info from the page. The extraction logic lives in
[`Resources/extract.js`](Resources/extract.js); the pure decision logic (status
classification, date parsing, overdue grading) is in
[`OrderLogic.cs`](OrderLogic.cs) and covered by unit tests in
[`tests/PackagePeek.Tests`](tests/PackagePeek.Tests).

## Continuing development with Claude Code

This project was built with AI assistance and is set up so you can keep going the
same way. To pick up where it left off on your own machine:

1. **Get the code onto your PC**

   ```bash
   git clone https://github.com/dmpotter1361/PackagePeek.git
   cd PackagePeek
   ```

2. **Install [Claude Code](https://claude.com/claude-code)** (Anthropic's coding CLI)
   and start it in the project folder:

   ```bash
   npm install -g @anthropic-ai/claude-code
   claude
   ```

   (You can also use the Claude Code extension for VS Code / JetBrains, or
   [claude.ai/code](https://claude.ai/code).)

3. **Point Claude at the project and ask for what you want.** A good first prompt:

   > Read the README, `OrderLogic.cs`, and `Resources/extract.js`, then run the tests
   > so you understand the project. I'd like to add &lt;your feature&gt;.

### Helpful map for a new contributor (human or AI)

- **`Resources/extract.js`** — reads order data off the Amazon page. This is the part
  that needs tuning when Amazon changes their layout. Set the environment variable
  `PACKAGEPEEK_DEBUG=1` to have the app dump what it scraped to
  `%APPDATA%\PackagePeek\last-scrape.json` and `debug-page.json` for inspection
  (off by default for privacy).
- **`OrderLogic.cs`** — pure, UI-free decision logic (status classification, date
  parsing, overdue grading), fully unit-tested. Add a test in
  `tests/PackagePeek.Tests` for any new rule — `dotnet test` runs them.
- **`TrayContext.cs`** — the app's brain: tray icon, timer, notifications, calendar.
- **`DashboardForm.cs` / `SettingsForm.cs` / `HelpForm.cs`** — the windows.
- **`build.ps1`** — produces the installer.

## Acknowledgments

Package Peek was designed and built collaboratively with **Claude** (Anthropic's AI),
pair-programming with the author from the first idea through the live extractor,
the UI and notifications, the installer, the tests, and this README. The direction,
decisions, and real-world testing are human; a lot of the implementation was
AI-assisted — and we're happy to say so. 🤖🤝

## License

[MIT](LICENSE)
