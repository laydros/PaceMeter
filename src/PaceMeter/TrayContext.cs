using Microsoft.Win32;

namespace PaceMeter;

internal sealed class TrayContext : ApplicationContext
{
    static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    // Opening the popup refetches only if the data is older than this.
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "PaceMeter";

    readonly NotifyIcon _tray;
    readonly PopupForm _popup = new();
    readonly UsageClient _client = new();
    readonly SnapshotStore _store = new();
    readonly System.Windows.Forms.Timer _timer = new();
    readonly ToolStripMenuItem _startupItem;

    UsageSnapshot? _snapshot;
    UsageException? _error;
    bool _loading;
    Icon? _icon;

    public TrayContext()
    {
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, async (_, _) => await RefreshAsync(userInitiated: true));
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "PaceMeter",
            Visible = true,
        };
        _tray.MouseClick += OnTrayClick;

        _popup.RefreshRequested += async (_, _) => await RefreshAsync(userInitiated: true);
        _popup.QuitRequested += (_, _) => ExitThread();

        // Show the last saved reading until the first fetch completes (or instead of it, if that fails).
        _snapshot = _store.Load();
        UpdateIcon();

        _timer.Interval = (int)RefreshInterval.TotalMilliseconds;
        _timer.Tick += async (_, _) => await RefreshAsync(userInitiated: false);
        _timer.Start();

        _ = RefreshAsync(userInitiated: false);
    }

    async void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // The click that deactivated the popup also lands here; treat it as "close".
        if (_popup.Visible || DateTime.UtcNow - _popup.LastHiddenUtc < TimeSpan.FromMilliseconds(300))
            return;

        _popup.SetState(_snapshot, _error, _loading);
        _popup.ShowNearCursor();

        if (_snapshot is null || DateTimeOffset.Now - _snapshot.FetchedAt > StaleAfter)
            await RefreshAsync(userInitiated: false);
    }

    /// <param name="userInitiated">
    /// True only for an explicit Refresh click. Only then may an expired token be renewed by launching
    /// Claude Code; background refreshes never start other programs.
    /// </param>
    async Task RefreshAsync(bool userInitiated)
    {
        if (_loading) return;
        _loading = true;
        _popup.SetState(_snapshot, _error, _loading);

        try
        {
            _snapshot = await FetchAsync(userInitiated);
            _error = null;
            _store.Save(_snapshot);
        }
        catch (UsageException ex)
        {
            _error = ex;
        }
        catch (Exception ex)
        {
            _error = new UsageException($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }

        _popup.SetState(_snapshot, _error, _loading);
        UpdateIcon();
    }

    async Task<UsageSnapshot> FetchAsync(bool userInitiated)
    {
        try
        {
            return await _client.FetchAsync();
        }
        catch (UsageException ex) when (userInitiated && ex.TokenExpired)
        {
            _popup.SetState(_snapshot, _error, _loading, "Renewing sign-in...");
            var outcome = await ClaudeCodeRefresher.RefreshAsync();
            _popup.SetState(_snapshot, _error, _loading);
            if (outcome != RefreshOutcome.Refreshed)
                throw new UsageException(RenewFailedMessage(outcome));
            return await _client.FetchAsync();
        }
    }

    static string RenewFailedMessage(RefreshOutcome outcome) => outcome switch
    {
        RefreshOutcome.ClaudeNotFound =>
            "Couldn't find Claude Code (the claude command) to renew the sign-in. Open Claude Code to renew it.",
        RefreshOutcome.TimedOut =>
            "Claude Code didn't respond within 20 seconds. Run claude in a terminal to renew the sign-in.",
        _ =>
            "Claude Code didn't renew its sign-in. Run claude in a terminal; you may need to sign in again.",
    };

    void UpdateIcon()
    {
        var session = Find("session");
        var weekly = Find("weekly_all");

        var old = _icon;
        var state = _snapshot is null ? (_error is null ? IconState.Normal : IconState.Error)
            : _error is null ? IconState.Normal
            : IconState.Stale;
        _icon = TrayIconRenderer.Render(CurrentPercent(session), CurrentPercent(weekly), state);
        _tray.Icon = _icon;
        TrayIconRenderer.Release(old);

        _tray.Text = TooltipText(session, weekly);
    }

    string TooltipText(LimitView? session, LimitView? weekly)
    {
        if (_snapshot is null)
            return _error is null ? "PaceMeter - loading" : Truncate($"PaceMeter - {_error.Message}");

        var parts = new List<string>();
        if (session is not null) parts.Add($"Session {LeftText(session)}");
        if (weekly is not null) parts.Add($"Weekly {LeftText(weekly)}");
        var text = parts.Count > 0 ? string.Join(" | ", parts) : "PaceMeter";
        if (_error is not null) text += $" (as of {TimeFormat.When(_snapshot.FetchedAt, DateTimeOffset.Now)})";
        return Truncate(text);
    }

    static string LeftText(LimitView limit) =>
        CurrentPercent(limit) is { } p ? $"{100 - Math.Clamp(p, 0, 100):0}% left" : "reset";

    /// <summary>The limit's percent used, or null if its window has reset since the reading was taken.</summary>
    static double? CurrentPercent(LimitView? limit) =>
        limit is null || limit.ResetsAt <= DateTimeOffset.Now ? null : limit.Percent;

    // NotifyIcon.Text throws above 127 characters.
    static string Truncate(string s) => s.Length <= 127 ? s : s[..124] + "...";

    LimitView? Find(string kind) => _snapshot?.Limits.FirstOrDefault(l => l.Kind == kind);

    static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (IsStartupEnabled())
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        else
            key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        _startupItem.Checked = IsStartupEnabled();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _popup.Dispose();
            _client.Dispose();
            TrayIconRenderer.Release(_icon);
        }
        base.Dispose(disposing);
    }
}
