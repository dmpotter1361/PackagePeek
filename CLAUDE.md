# PackagePeek (AmazonTracker) — notes for Claude Code

Windows system-tray app that tracks Amazon delivery status, built on WebView2.
C# / .NET 10 WinForms, Windows-only. Published publicly as `dmpotter1361/PackagePeek`.

## Run / build

```powershell
dotnet run                 # launch the tray app
dotnet build -c Release    # release build
./build.ps1                # packaging / release (MSI) script
```

No tests. Plain `dotnet` is enough — no Visual Studio dependency.

## Layout

- **`BrowserHost.cs`** — hosts the WebView2 control used to read Amazon order pages.
- **`TrayContext.cs`** — `ApplicationContext` for the tray icon, menu, lifecycle.
- **`DashboardForm.cs`** — main UI listing tracked orders.
- **`OrderInfo.cs` / `OrderLogic.cs`** — order model + parsing/status logic.
- **`AppSettings.cs`** — persisted settings; **`NicknameStore.cs` / `HiddenStore.cs`** — per-order nicknames and hidden orders.
- **`Sounds.cs` / `WavGain.cs`** — notification sounds; **`ShellBranding.cs`** — window/shell branding.
- **`SettingsForm.cs` / `HelpForm.cs`** — settings and help dialogs.

## Conventions

- Forms are hand-written in code (no `.Designer.cs`); match that style.
- **UI must survive different fonts / display-scaling**: prefer `TableLayoutPanel`/`FlowLayoutPanel` + `AutoSize` + font fallback rather than fixed coordinates.
- Wife is an active tester — expect bug-fix iterations; keep changes conservative.
