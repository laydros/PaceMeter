using PaceMeter;

namespace PaceMeter.Tests;

public sealed class SnapshotStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "PaceMeterTests-" + Guid.NewGuid().ToString("N"));
    string StorePath => Path.Combine(_dir, "sub", "last-usage.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RoundTripsSnapshot()
    {
        var snapshot = new UsageSnapshot(
            [
                new LimitView("session", "Session (5h)", 13, new DateTimeOffset(2026, 9, 26, 20, 19, 59, TimeSpan.FromHours(-4)), TimeSpan.FromHours(5)),
                new LimitView("mystery", "mystery", 1, null, null),
            ],
            new DateTimeOffset(2026, 9, 26, 17, 5, 0, TimeSpan.FromHours(-4)));
        var store = new SnapshotStore(StorePath);

        store.Save(snapshot);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.FetchedAt, loaded.FetchedAt);
        Assert.Equal(snapshot.Limits, loaded.Limits);
    }

    [Fact]
    public void MissingFileLoadsNull()
    {
        Assert.Null(new SnapshotStore(StorePath).Load());
    }

    [Fact]
    public void CorruptFileLoadsNull()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, "{ not json");

        Assert.Null(new SnapshotStore(StorePath).Load());
    }
}
