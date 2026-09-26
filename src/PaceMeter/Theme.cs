namespace PaceMeter;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(32, 32, 34);
    public static readonly Color Surface = Color.FromArgb(48, 48, 52);
    public static readonly Color Track = Color.FromArgb(70, 70, 76);
    public static readonly Color Text = Color.FromArgb(236, 236, 238);
    public static readonly Color TextMuted = Color.FromArgb(160, 160, 168);
    public static readonly Color Good = Color.FromArgb(80, 200, 120);
    public static readonly Color Warn = Color.FromArgb(240, 180, 60);
    public static readonly Color Bad = Color.FromArgb(235, 90, 80);
    public static readonly Color PaceMarker = Color.FromArgb(236, 236, 238);

    public static Color ForPercent(double percent) =>
        percent >= 90 ? Bad : percent >= 75 ? Warn : Good;

    public static Color ForDelta(double delta) =>
        delta >= -0.5 ? Good : delta >= -15 ? Warn : Bad;
}
