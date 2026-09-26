namespace PaceMeter;

/// <summary>
/// Borderless panel shown above the tray icon. Everything is owner-drawn so it stays compact.
/// </summary>
internal sealed class PopupForm : Form
{
    const int BaseWidth = 340;
    const int Pad = 14;
    const int HeaderH = 30;
    const int RowH = 84;
    const int FooterH = 30;

    const int WS_EX_TOOLWINDOW = 0x80;
    const int CS_DROPSHADOW = 0x20000;

    readonly System.Windows.Forms.Timer _tick = new() { Interval = 30_000 };
    readonly Font _titleFont = new("Segoe UI Semibold", 11f);
    readonly Font _labelFont = new("Segoe UI Semibold", 9.5f);
    readonly Font _textFont = new("Segoe UI", 9f);

    UsageSnapshot? _snapshot;
    string? _error;
    bool _loading;
    Rectangle _refreshLink, _quitLink;

    public event EventHandler? RefreshRequested;
    public event EventHandler? QuitRequested;

    /// <summary>When the popup last hid itself, so a tray click that caused the hide doesn't immediately reopen it.</summary>
    public DateTime LastHiddenUtc { get; private set; } = DateTime.MinValue;

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Theme.Background;
        Text = "PaceMeter";
        _tick.Tick += (_, _) => Invalidate();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    float Scale1 => DeviceDpi / 96f;
    int S(int v) => (int)Math.Round(v * Scale1);

    public void SetState(UsageSnapshot? snapshot, string? error, bool loading)
    {
        _snapshot = snapshot;
        _error = error;
        _loading = loading;
        if (Visible)
        {
            var bottom = Bottom;
            Height = MeasureHeight();
            Top = bottom - Height;
            Invalidate();
        }
    }

    public void ShowNearCursor()
    {
        Width = S(BaseWidth);
        Height = MeasureHeight();

        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var margin = S(8);
        var x = Math.Clamp(cursor.X - Width / 2, area.Left + margin, area.Right - Width - margin);
        // Taskbar is usually at the bottom; fall back to below the cursor if there is no room above.
        var y = cursor.Y - Height - margin;
        if (y < area.Top + margin) y = Math.Min(cursor.Y + margin, area.Bottom - Height - margin);
        if (y + Height > area.Bottom - margin) y = area.Bottom - Height - margin;
        Location = new Point(x, y);

        Show();
        Activate();
        _tick.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HidePopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) HidePopup();
    }

    void HidePopup()
    {
        if (!Visible) return;
        _tick.Stop();
        Hide();
        LastHiddenUtc = DateTime.UtcNow;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_refreshLink.Contains(e.Location)) RefreshRequested?.Invoke(this, EventArgs.Empty);
        else if (_quitLink.Contains(e.Location)) QuitRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _refreshLink.Contains(e.Location) || _quitLink.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    int ErrorHeight(int width) =>
        _error is null ? 0 : TextRenderer.MeasureText(_error, _textFont, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + S(10);

    int RowCount => _snapshot?.Limits.Count ?? 0;

    int MeasureHeight()
    {
        var inner = S(BaseWidth) - S(Pad) * 2;
        var body = RowCount > 0 ? RowCount * S(RowH) : (_error is null ? S(24) : 0);
        return S(Pad) + S(HeaderH) + ErrorHeight(inner) + body + S(FooterH) + S(Pad) / 2;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var border = new Pen(Theme.Track))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        int left = S(Pad), right = Width - S(Pad), inner = right - left;
        int y = S(Pad);

        TextRenderer.DrawText(g, "Claude usage", _titleFont, new Point(left, y), Theme.Text);
        y += S(HeaderH);

        if (_error is not null)
        {
            var h = ErrorHeight(inner) - S(10);
            TextRenderer.DrawText(g, _error, _textFont, new Rectangle(left, y, inner, h), Theme.Bad, TextFormatFlags.WordBreak);
            y += h + S(10);
        }

        if (_snapshot is { } snap)
        {
            var now = DateTimeOffset.Now;
            foreach (var limit in snap.Limits)
            {
                DrawRow(g, limit, now, left, y, inner);
                y += S(RowH);
            }
        }
        else if (_error is null)
        {
            TextRenderer.DrawText(g, "Loading...", _textFont, new Point(left, y), Theme.TextMuted);
            y += S(24);
        }

        DrawFooter(g, left, right, y);
    }

    void DrawRow(Graphics g, LimitView limit, DateTimeOffset now, int left, int y, int inner)
    {
        var right = left + inner;

        TextRenderer.DrawText(g, limit.Label, _labelFont, new Point(left, y), Theme.Text);
        var remaining = 100 - Math.Clamp(limit.Percent, 0, 100);
        DrawRight(g, $"{remaining:0}% left", _labelFont, right, y, Theme.ForPercent(limit.Percent));
        y += S(22);

        PaceResult? pace = limit is { ResetsAt: { } r, Window: { } w }
            ? Pace.Compute(limit.Percent, r, w, now)
            : null;

        // The bar shows what's left, draining from the right as usage grows.
        var bar = new Rectangle(left, y, inner, S(8));
        using (var track = new SolidBrush(Theme.Track)) g.FillRectangle(track, bar);
        var fillW = (int)Math.Round(bar.Width * remaining / 100.0);
        if (fillW > 0)
            using (var fill = new SolidBrush(Theme.ForPercent(limit.Percent)))
                g.FillRectangle(fill, bar with { Width = fillW });
        if (pace is not null)
        {
            // Tick where an even pace would put the remaining amount right now.
            var mx = bar.Left + (int)Math.Round(bar.Width * (100 - Math.Clamp(pace.ExpectedPercent, 0, 100)) / 100.0);
            using var marker = new SolidBrush(Theme.PaceMarker);
            g.FillRectangle(marker, mx - S(1), bar.Top - S(3), Math.Max(2, S(2)), bar.Height + S(6));
        }
        y += S(16);

        var resetText = limit.ResetsAt is { } resetsAt
            ? $"Resets in {Pace.FormatDuration(resetsAt - now)}"
            : "No reset scheduled";
        TextRenderer.DrawText(g, resetText, _textFont, new Point(left, y), Theme.TextMuted);

        if (pace is null) return;

        var (paceText, paceColor) = pace.Delta switch
        {
            >= 0.5 => ($"{pace.Delta:0}% in reserve", Theme.Good),
            <= -0.5 => ($"{-pace.Delta:0}% in deficit", Theme.ForDelta(pace.Delta)),
            _ => ("On pace", Theme.Good),
        };
        DrawRight(g, paceText, _textFont, right, y, paceColor);
        y += S(18);

        TextRenderer.DrawText(g, ProjectionText(pace, limit.ResetsAt!.Value - now), _textFont, new Point(left, y), Theme.TextMuted);
    }

    static string ProjectionText(PaceResult pace, TimeSpan untilReset) => pace switch
    {
        { RunsOutIn: { } t } when t == TimeSpan.Zero => "Limit reached",
        { RunsOutIn: null } => "Too early to project",
        { LastsUntilReset: true } => "At this rate, lasts until reset",
        { RunsOutIn: { } t } => $"At this rate, runs out in {Pace.FormatDuration(t)} ({Pace.FormatDuration(untilReset - t)} early)",
    };

    void DrawFooter(Graphics g, int left, int right, int y)
    {
        y += S(4);
        var status = _loading ? "Refreshing..."
            : _snapshot is { } s ? $"Updated {s.FetchedAt.LocalDateTime:t}"
            : "";
        TextRenderer.DrawText(g, status, _textFont, new Point(left, y), Theme.TextMuted);

        var quitSize = TextRenderer.MeasureText("Quit", _textFont);
        _quitLink = new Rectangle(right - quitSize.Width, y, quitSize.Width, quitSize.Height);
        var refreshSize = TextRenderer.MeasureText("Refresh", _textFont);
        _refreshLink = new Rectangle(_quitLink.Left - S(14) - refreshSize.Width, y, refreshSize.Width, refreshSize.Height);

        TextRenderer.DrawText(g, "Refresh", _textFont, _refreshLink.Location, Theme.Text);
        TextRenderer.DrawText(g, "Quit", _textFont, _quitLink.Location, Theme.Text);
    }

    static void DrawRight(Graphics g, string text, Font font, int right, int y, Color color)
    {
        var size = TextRenderer.MeasureText(text, font);
        TextRenderer.DrawText(g, text, font, new Point(right - size.Width, y), color);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _titleFont.Dispose();
            _labelFont.Dispose();
            _textFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
