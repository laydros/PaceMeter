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
    readonly System.Windows.Forms.Timer _timer = new();
    readonly ToolStripMenuItem _startupItem;

    UsageSnapshot? _snapshot;
    string? _error;
    bool _loading;
    Icon? _icon;

    public TrayContext()
    {
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, async (_, _) => await RefreshAsync());
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

        _popup.RefreshRequested += async (_, _) => await RefreshAsync();
        _popup.QuitRequested += (_, _) => ExitThread();

        UpdateIcon();

        _timer.Interval = (int)RefreshInterval.TotalMilliseconds;
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
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
            await RefreshAsync();
    }

    async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        _popup.SetState(_snapshot, _error, _loading);

        try
        {
            _snapshot = await _client.FetchAsync();
            _error = null;
        }
        catch (UsageException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex)
        {
            _error = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }

        _popup.SetState(_snapshot, _error, _loading);
        UpdateIcon();
    }

    void UpdateIcon()
    {
        var session = Find("session");
        var weekly = Find("weekly_all");

        var old = _icon;
        _icon = TrayIconRenderer.Render(session?.Percent, weekly?.Percent, error: _error is not null && _snapshot is null);
        _tray.Icon = _icon;
        TrayIconRenderer.Release(old);

        _tray.Text = TooltipText(session, weekly);
    }

    string TooltipText(LimitView? session, LimitView? weekly)
    {
        if (_snapshot is null)
            return _error is null ? "PaceMeter - loading" : Truncate($"PaceMeter - {_error}");

        var parts = new List<string>();
        if (session is not null) parts.Add($"Session {100 - Math.Clamp(session.Percent, 0, 100):0}% left");
        if (weekly is not null) parts.Add($"Weekly {100 - Math.Clamp(weekly.Percent, 0, 100):0}% left");
        var text = parts.Count > 0 ? string.Join(" | ", parts) : "PaceMeter";
        if (_error is not null) text += " (stale)";
        return Truncate(text);
    }

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
