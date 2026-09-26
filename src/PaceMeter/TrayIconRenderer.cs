using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PaceMeter;

/// <summary>
/// Draws the tray icon: two stacked meters, session on top and weekly on the bottom.
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Returns a new icon. Dispose the previous one with <see cref="Release"/>.</summary>
    public static Icon Render(double? top, double? bottom, bool error)
    {
        var size = SystemInformation.SmallIconSize;
        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Color.Transparent);

            int w = size.Width, h = size.Height;
            int gap = Math.Max(1, h / 8);
            int barH = (h - gap * 3) / 2;

            DrawMeter(g, new Rectangle(0, gap, w, barH), top, error);
            DrawMeter(g, new Rectangle(0, gap * 2 + barH, w, barH), bottom, error);
        }

        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public static void Release(Icon? icon)
    {
        if (icon is null) return;
        var handle = icon.Handle;
        icon.Dispose();
        DestroyIcon(handle);
    }

    static void DrawMeter(Graphics g, Rectangle r, double? percent, bool error)
    {
        using (var track = new SolidBrush(Theme.Track))
            g.FillRectangle(track, r);

        if (error)
        {
            using var bad = new SolidBrush(Theme.Bad);
            g.FillRectangle(bad, r with { Width = Math.Max(2, r.Width / 4) });
            return;
        }

        if (percent is not double p) return;
        var fillW = (int)Math.Round(r.Width * Math.Clamp(p, 0, 100) / 100.0);
        if (fillW <= 0) return;
        using var fill = new SolidBrush(Theme.ForPercent(p));
        g.FillRectangle(fill, r with { Width = fillW });
    }
}
