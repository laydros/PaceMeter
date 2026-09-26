# PaceMeter

A small Windows tray app that shows your Claude plan usage limits and whether you're on pace to make them last. Inspired by [CodexBar](https://github.com/steipete/CodexBar) for macOS.

- **Tray icon:** two mini meters. The top one is the 5-hour session window and the bottom one is the weekly window. Green, amber, and red mark 75% and 90% used.
- **Hover:** a tooltip with the session and weekly percentages.
- **Left-click:** a popup with every limit your plan reports (session, weekly, and per-model weekly limits such as Fable), each with:
  - percent used and time until reset
  - a white pace tick on the bar showing where an even burn rate would put you right now
  - **% in reserve / % in deficit**: how far under or over that even pace you are
  - a projection at your average rate so far: "lasts until reset" or "runs out in Xh (Yh early)"
- **Right-click:** Refresh, Start with Windows, Quit.

Data refreshes every 5 minutes, and again when you open the popup if the data is more than a minute old.

## Install

Download `PaceMeter.exe` from the [latest release](https://github.com/laydros/PaceMeter/releases/latest) and run it. It's a single self-contained exe, so there's no installer and no .NET install needed. Right-click the tray icon and choose **Start with Windows** to launch it at login.

The exe isn't code-signed, so on first run Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**. If you'd rather not trust a prebuilt binary, build it yourself (see below).

## Requirements

- Windows 10/11
- [Claude Code](https://claude.com/claude-code), signed in with a Claude subscription (Pro/Max). PaceMeter reads the OAuth token Claude Code stores in `%USERPROFILE%\.claude\.credentials.json`, or in `%CLAUDE_CONFIG_DIR%\.credentials.json` if that variable is set.

PaceMeter never refreshes or writes the token. If it expires, run Claude Code once and PaceMeter picks up the new token on its next refresh.

## Caveat

Usage comes from `https://api.anthropic.com/api/oauth/usage`, the same undocumented endpoint Claude Code uses for `/usage`. It isn't a public API and can change or disappear without notice.

## Build

Requires the .NET 9 SDK.

```bash
dotnet build
dotnet test
```

Single self-contained exe (no .NET install needed on the target machine):

```bash
dotnet publish src/PaceMeter -c Release -r win-x64
```

The output is `src/PaceMeter/bin/Release/net9.0-windows/win-x64/publish/PaceMeter.exe`.

## How pace is calculated

For a window of length `W` (5 hours or 7 days) that resets at `R`:

- `elapsed = W - (R - now)`
- `expected = elapsed / W * 100`: where a perfectly even burn would be
- `reserve = expected - used`. Positive numbers show as "in reserve", negative ones as "in deficit".
- `rate = used / elapsed`, and the projected time to 100% is `(100 - used) / rate`. If that's later than the reset, the limit lasts.

No projection is shown during the first 2% of a window, when the average rate is mostly noise.

## Project layout

```
src/PaceMeter/
  Program.cs            entry point, single-instance guard
  TrayContext.cs        tray icon, menu, refresh timer, startup toggle
  PopupForm.cs          owner-drawn popup
  TrayIconRenderer.cs   draws the two-bar tray icon
  UsageClient.cs        reads credentials, calls the usage endpoint, parses limits
  Pace.cs               pace/projection math
  Theme.cs              colors
tests/PaceMeter.Tests/  xUnit tests for pace math and response parsing
```

## License

GPL-3.0. See [LICENSE](LICENSE).
