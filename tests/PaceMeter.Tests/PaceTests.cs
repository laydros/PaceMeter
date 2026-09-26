using PaceMeter;

namespace PaceMeter.Tests;

public class PaceTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Week = TimeSpan.FromDays(7);
    static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);

    [Fact]
    public void HalfwayThroughWindowExpectsFiftyPercent()
    {
        var result = Pace.Compute(30, Now + TimeSpan.FromHours(84), Week, Now);

        Assert.Equal(50, result.ExpectedPercent, precision: 6);
        Assert.Equal(20, result.Delta, precision: 6);
    }

    [Fact]
    public void UnderPaceLastsUntilReset()
    {
        // 1h into a 5h window at 10%: 10%/h, reaches 100% in 9h, reset is 4h away.
        var result = Pace.Compute(10, Now + TimeSpan.FromHours(4), FiveHours, Now);

        Assert.True(result.LastsUntilReset);
        Assert.Equal(9, result.RunsOutIn!.Value.TotalHours, precision: 6);
    }

    [Fact]
    public void OverPaceRunsOutBeforeReset()
    {
        // 1 day into a week at 40%: 40%/day, reaches 100% in 1.5 more days, reset is 6 days away.
        var result = Pace.Compute(40, Now + TimeSpan.FromDays(6), Week, Now);

        Assert.False(result.LastsUntilReset);
        Assert.True(result.Delta < 0);
        Assert.Equal(1.5, result.RunsOutIn!.Value.TotalDays, precision: 6);
    }

    [Fact]
    public void TooEarlyInWindowHasNoProjection()
    {
        var result = Pace.Compute(5, Now + FiveHours - TimeSpan.FromMinutes(2), FiveHours, Now);

        Assert.Null(result.RunsOutIn);
        Assert.True(result.LastsUntilReset);
    }

    [Fact]
    public void ZeroUsageHasNoProjection()
    {
        var result = Pace.Compute(0, Now + TimeSpan.FromHours(2), FiveHours, Now);

        Assert.Null(result.RunsOutIn);
        Assert.Equal(60, result.Delta, precision: 6);
    }

    [Fact]
    public void FullUsageIsLimitReached()
    {
        var result = Pace.Compute(100, Now + TimeSpan.FromHours(2), FiveHours, Now);

        Assert.Equal(TimeSpan.Zero, result.RunsOutIn);
        Assert.False(result.LastsUntilReset);
    }

    [Fact]
    public void ResetInThePastClampsToFullWindowElapsed()
    {
        var result = Pace.Compute(50, Now - TimeSpan.FromMinutes(5), FiveHours, Now);

        Assert.Equal(100, result.ExpectedPercent, precision: 6);
    }

    [Theory]
    [InlineData(0, 0, 30, 0, "30m")]
    [InlineData(0, 0, 0, 20, "1m")]
    [InlineData(0, 3, 12, 0, "3h 12m")]
    [InlineData(2, 4, 59, 0, "2d 4h")]
    public void FormatDuration(int d, int h, int m, int s, string expected)
    {
        Assert.Equal(expected, Pace.FormatDuration(new TimeSpan(d, h, m, s)));
    }
}
