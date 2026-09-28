using System.Globalization;
using PaceMeter;

namespace PaceMeter.Tests;

public class TimeFormatTests
{
    [Fact]
    public void SameDayShowsTimeOnly()
    {
        var now = new DateTimeOffset(2026, 9, 27, 14, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 27, 14, 0, 0)));
        var earlier = now.AddHours(-3);

        Assert.Equal(earlier.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), TimeFormat.When(earlier, now));
    }

    [Fact]
    public void OtherDayIncludesWeekday()
    {
        var now = new DateTimeOffset(2026, 9, 27, 14, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 27, 14, 0, 0)));
        var yesterday = now.AddDays(-1);
        var local = yesterday.ToLocalTime();

        Assert.Equal($"{local:ddd} {local:t}", TimeFormat.When(yesterday, now));
    }
}
