using PaceMeter;

namespace PaceMeter.Tests;

public class UsageParseTests
{
    const string Sample = """
    {
      "five_hour": { "utilization": 13.0, "resets_at": "2026-09-26T20:19:59.85162-04:00" },
      "limits": [
        { "kind": "session", "group": "session", "percent": 13, "severity": "normal",
          "resets_at": "2026-09-26T20:19:59.85162-04:00", "scope": null, "is_active": false },
        { "kind": "weekly_all", "group": "weekly", "percent": 52, "severity": "normal",
          "resets_at": "2026-09-26T19:59:59.851641-04:00", "scope": null, "is_active": true },
        { "kind": "weekly_scoped", "group": "weekly", "percent": 7, "severity": "normal",
          "resets_at": "2026-09-26T19:59:59.851804-04:00",
          "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null }, "is_active": false },
        { "kind": "mystery", "group": "monthly", "percent": 1, "resets_at": null }
      ]
    }
    """;

    [Fact]
    public void ParsesLimitsArray()
    {
        var limits = UsageClient.Parse(Sample);

        Assert.Equal(["Session (5h)", "Weekly", "Weekly - Fable", "mystery"], limits.Select(l => l.Label));
        Assert.Equal([13d, 52d, 7d, 1d], limits.Select(l => l.Percent));
        Assert.Equal(TimeSpan.FromHours(5), limits[0].Window);
        Assert.Equal(TimeSpan.FromDays(7), limits[1].Window);
        Assert.Null(limits[3].Window);
        Assert.Null(limits[3].ResetsAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 0, 19, 59, TimeSpan.Zero), limits[0].ResetsAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MissingLimitsArrayYieldsEmptyList()
    {
        Assert.Empty(UsageClient.Parse("""{ "five_hour": null }"""));
    }

    [Fact]
    public void MalformedJsonThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => UsageClient.Parse("not json"));
    }
}
