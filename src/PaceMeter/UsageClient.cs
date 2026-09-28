using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaceMeter;

/// <summary>One rate limit as shown in the UI.</summary>
internal sealed record LimitView(string Kind, string Label, double Percent, DateTimeOffset? ResetsAt, TimeSpan? Window);

internal sealed record UsageSnapshot(IReadOnlyList<LimitView> Limits, DateTimeOffset FetchedAt);

/// <param name="TokenExpired">True when the failure is an expired or rejected OAuth token, which Claude Code fixes on its next run.</param>
/// <param name="ExpiredAt">When the token expired, if known.</param>
internal sealed class UsageException(string message, bool tokenExpired = false, DateTimeOffset? expiredAt = null) : Exception(message)
{
    public bool TokenExpired { get; } = tokenExpired;
    public DateTimeOffset? ExpiredAt { get; } = expiredAt;
}

/// <summary>
/// Reads the Claude Code OAuth token from disk and queries the usage endpoint.
/// The endpoint is undocumented and may change without notice.
/// The token is never refreshed here; Claude Code owns that, and we re-read the file on every fetch.
/// </summary>
internal sealed class UsageClient : IDisposable
{
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string CredentialsPath
    {
        get
        {
            var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(configDir))
                configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            return Path.Combine(configDir, ".credentials.json");
        }
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct = default)
    {
        var token = await ReadTokenAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("PaceMeter", "0.1"));

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UsageException($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UsageException("Request timed out.");
        }

        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new UsageException("Token rejected. Run Claude Code once to refresh it.", tokenExpired: true);
            if (!resp.IsSuccessStatusCode)
                throw new UsageException($"Usage request failed: HTTP {(int)resp.StatusCode}.");

            var body = await resp.Content.ReadAsStringAsync(ct);
            return new UsageSnapshot(Parse(body), DateTimeOffset.Now);
        }
    }

    static async Task<string> ReadTokenAsync(CancellationToken ct)
    {
        var path = CredentialsPath;
        if (!File.Exists(path))
            throw new UsageException($"No Claude Code credentials at {path}. Sign in with Claude Code first.");

        CredentialsFile? creds;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            creds = await JsonSerializer.DeserializeAsync<CredentialsFile>(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new UsageException($"Could not read credentials: {ex.Message}");
        }

        var oauth = creds?.ClaudeAiOauth;
        if (string.IsNullOrEmpty(oauth?.AccessToken))
            throw new UsageException("Credentials file has no OAuth token. Sign in with Claude Code.");

        if (oauth.ExpiresAt is long ms && DateTimeOffset.FromUnixTimeMilliseconds(ms) is var expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw new UsageException("Token expired. Run Claude Code once to refresh it.", tokenExpired: true, expiredAt: expiresAt);

        return oauth.AccessToken;
    }

    internal static List<LimitView> Parse(string json)
    {
        UsageResponse? resp;
        try
        {
            resp = JsonSerializer.Deserialize<UsageResponse>(json);
        }
        catch (JsonException ex)
        {
            throw new UsageException($"Unexpected usage response: {ex.Message}");
        }

        var limits = new List<LimitView>();
        foreach (var l in resp?.Limits ?? [])
        {
            if (l.Kind is null) continue;
            limits.Add(new LimitView(l.Kind, LabelFor(l), l.Percent ?? 0, l.ResetsAt, WindowFor(l.Group)));
        }
        return limits;
    }

    static string LabelFor(ApiLimit l)
    {
        var model = l.Scope?.Model?.DisplayName;
        return l.Kind switch
        {
            "session" => "Session (5h)",
            "weekly_all" => "Weekly",
            "weekly_scoped" when model is not null => $"Weekly - {model}",
            _ when model is not null => $"{l.Kind} - {model}",
            _ => l.Kind!,
        };
    }

    static TimeSpan? WindowFor(string? group) => group switch
    {
        "session" => TimeSpan.FromHours(5),
        "weekly" => TimeSpan.FromDays(7),
        _ => null,
    };

    public void Dispose() => _http.Dispose();

    sealed class CredentialsFile
    {
        [JsonPropertyName("claudeAiOauth")] public OAuthCreds? ClaudeAiOauth { get; set; }
    }

    sealed class OAuthCreds
    {
        [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
        [JsonPropertyName("expiresAt")] public long? ExpiresAt { get; set; }
    }

    sealed class UsageResponse
    {
        [JsonPropertyName("limits")] public List<ApiLimit>? Limits { get; set; }
    }

    sealed class ApiLimit
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("group")] public string? Group { get; set; }
        [JsonPropertyName("percent")] public double? Percent { get; set; }
        [JsonPropertyName("resets_at")] public DateTimeOffset? ResetsAt { get; set; }
        [JsonPropertyName("scope")] public ApiScope? Scope { get; set; }
    }

    sealed class ApiScope
    {
        [JsonPropertyName("model")] public ApiModel? Model { get; set; }
    }

    sealed class ApiModel
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }
}
