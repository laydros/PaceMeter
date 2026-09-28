using System.Text.Json;

namespace PaceMeter;

/// <summary>
/// Persists the last successful usage reading so it can be shown, marked stale, when a fetch fails
/// (for example after the Claude Code token expires overnight).
/// </summary>
internal sealed class SnapshotStore(string path)
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaceMeter", "last-usage.json");

    public SnapshotStore() : this(DefaultPath) { }

    /// <summary>Returns the saved snapshot, or null if there is none or it can't be read.</summary>
    public UsageSnapshot? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Saves the snapshot. Failures are ignored; the cache is a convenience.</summary>
    public void Save(UsageSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
