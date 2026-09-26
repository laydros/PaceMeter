namespace PaceMeter;

/// <summary>
/// Result of comparing actual usage against a linear burn across the window.
/// </summary>
/// <param name="ExpectedPercent">Usage a perfectly even pace would have reached by now.</param>
/// <param name="Delta">ExpectedPercent minus actual usage. Positive means reserve, negative means deficit.</param>
/// <param name="RunsOutIn">Projected time until 100% at the average rate so far, or null when there is too little data.</param>
/// <param name="LastsUntilReset">True when the projection does not hit 100% before the window resets.</param>
internal sealed record PaceResult(double ExpectedPercent, double Delta, TimeSpan? RunsOutIn, bool LastsUntilReset);

internal static class Pace
{
    // Below this fraction of the window elapsed, the average rate is too noisy to project from.
    const double MinElapsedFraction = 0.02;

    public static PaceResult Compute(double usedPercent, DateTimeOffset resetsAt, TimeSpan window, DateTimeOffset now)
    {
        var remaining = resetsAt - now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (remaining > window) remaining = window;

        var elapsed = window - remaining;
        var expected = elapsed / window * 100.0;
        var delta = expected - usedPercent;

        if (usedPercent >= 100)
            return new PaceResult(expected, delta, TimeSpan.Zero, false);

        if (usedPercent <= 0 || elapsed / window < MinElapsedFraction)
            return new PaceResult(expected, delta, null, true);

        var percentPerHour = usedPercent / elapsed.TotalHours;
        var runsOutIn = TimeSpan.FromHours((100.0 - usedPercent) / percentPerHour);
        return new PaceResult(expected, delta, runsOutIn, runsOutIn >= remaining);
    }

    public static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))}m";
    }
}
