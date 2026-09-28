using System.Diagnostics;

namespace PaceMeter;

internal enum RefreshOutcome
{
    Refreshed,
    ClaudeNotFound,
    NotRefreshed,
    TimedOut,
}

/// <summary>
/// Gets Claude Code to renew its own expired OAuth token by running its local <c>/usage</c> command.
/// PaceMeter never uses the refresh token itself: each refresh issues a new refresh token and
/// invalidates the old one, which would sign Claude Code out.
/// <c>/usage</c> is a slash command, so no prompt is sent to a model and no quota is used.
/// </summary>
internal static class ClaudeCodeRefresher
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>Claude Code files each run under a project named after this folder, keeping probe sessions out of real projects.</summary>
    static string ProbeDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaceMeter", "probe");

    static string ClaudeConfigDirectory => Path.GetDirectoryName(UsageClient.CredentialsPath)!;

    public static async Task<RefreshOutcome> RefreshAsync(CancellationToken ct = default)
    {
        var claude = FindClaude();
        if (claude is null) return RefreshOutcome.ClaudeNotFound;

        var before = UsageClient.ReadTokenExpiry();
        var sessionId = Guid.NewGuid();
        Directory.CreateDirectory(ProbeDirectory);

        var psi = new ProcessStartInfo(claude)
        {
            WorkingDirectory = ProbeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in BuildArguments(sessionId)) psi.ArgumentList.Add(arg);

        var timedOut = false;
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return RefreshOutcome.ClaudeNotFound;
            process.StandardInput.Close();
            // Drain output so the process can't block on a full pipe.
            var drainOut = process.StandardOutput.ReadToEndAsync(ct);
            var drainErr = process.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(drainOut, drainErr);
            }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (!timedOut) throw;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return RefreshOutcome.ClaudeNotFound;
        }
        finally
        {
            DeleteTranscript(ClaudeConfigDirectory, sessionId);
        }

        var after = UsageClient.ReadTokenExpiry();
        if (after is { } a && a != before && a > DateTimeOffset.UtcNow) return RefreshOutcome.Refreshed;
        return timedOut ? RefreshOutcome.TimedOut : RefreshOutcome.NotRefreshed;
    }

    internal static IReadOnlyList<string> BuildArguments(Guid sessionId) =>
    [
        // No tools, no MCP servers, no Remote Control session: only the local /usage command runs.
        "--allowed-tools", "",
        "--strict-mcp-config",
        "--settings", """{"remoteControlAtStartup":false}""",
        "--session-id", sessionId.ToString(),
        "/usage",
    ];

    /// <summary>Removes the transcript Claude Code saved for this probe run, so probes don't pile up in its history.</summary>
    internal static void DeleteTranscript(string claudeConfigDir, Guid sessionId)
    {
        var projects = Path.Combine(claudeConfigDir, "projects");
        if (!Directory.Exists(projects)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(projects, $"{sessionId}.jsonl", SearchOption.AllDirectories))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static string? FindClaude()
    {
        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim('"'), "claude.exe"));
            candidates.Add(Path.Combine(dir.Trim('"'), "claude.cmd"));
        }
        // Default location of Claude Code's native installer, in case PATH wasn't inherited.
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
