using PaceMeter;

namespace PaceMeter.Tests;

public sealed class ClaudeCodeRefresherTests : IDisposable
{
    readonly string _configDir = Path.Combine(Path.GetTempPath(), "PaceMeterTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true);
    }

    [Fact]
    public void ArgumentsRunOnlyTheLocalUsageCommand()
    {
        var id = Guid.NewGuid();

        var args = ClaudeCodeRefresher.BuildArguments(id);

        Assert.Equal(
            ["--allowed-tools", "", "--strict-mcp-config", "--settings", """{"remoteControlAtStartup":false}""", "--session-id", id.ToString(), "/usage"],
            args);
    }

    [Fact]
    public void DeleteTranscriptRemovesOnlyThatSession()
    {
        var id = Guid.NewGuid();
        var probeProject = Path.Combine(_configDir, "projects", "C--probe");
        var otherProject = Path.Combine(_configDir, "projects", "C--real-project");
        Directory.CreateDirectory(probeProject);
        Directory.CreateDirectory(otherProject);
        var target = Path.Combine(probeProject, $"{id}.jsonl");
        var otherSession = Path.Combine(probeProject, $"{Guid.NewGuid()}.jsonl");
        var realSession = Path.Combine(otherProject, $"{Guid.NewGuid()}.jsonl");
        foreach (var f in new[] { target, otherSession, realSession }) File.WriteAllText(f, "{}");

        ClaudeCodeRefresher.DeleteTranscript(_configDir, id);

        Assert.False(File.Exists(target));
        Assert.True(File.Exists(otherSession));
        Assert.True(File.Exists(realSession));
    }

    [Fact]
    public void DeleteTranscriptToleratesMissingProjectsFolder()
    {
        ClaudeCodeRefresher.DeleteTranscript(_configDir, Guid.NewGuid());
    }
}
