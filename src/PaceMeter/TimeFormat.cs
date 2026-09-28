namespace PaceMeter;

internal static class TimeFormat
{
    /// <summary>Local time, with the weekday when it isn't today: "3:48 AM" or "Sat 3:48 AM".</summary>
    public static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? $"{local:t}" : $"{local:ddd} {local:t}";
    }
}
