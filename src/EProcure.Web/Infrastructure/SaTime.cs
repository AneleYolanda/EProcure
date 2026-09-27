namespace EProcure.Web.Infrastructure;

/// <summary>
/// Converts between stored UTC timestamps and South African Standard Time.
/// Windows uses the named time-zone id; the fixed offset keeps local development
/// predictable if the named zone is unavailable.
/// </summary>
public static class SaTime
{
    private static readonly TimeZoneInfo FallbackZone =
        TimeZoneInfo.CreateCustomTimeZone("South Africa Standard Time fallback", TimeSpan.FromHours(2), "South Africa Standard Time", "South Africa Standard Time");

    public static DateTime ToSast(DateTime utc)
    {
        var normalised = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(normalised, GetZone());
    }

    public static DateTime FromSast(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, GetZone());
    }

    private static TimeZoneInfo GetZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return FallbackZone;
        }
        catch (InvalidTimeZoneException)
        {
            return FallbackZone;
        }
    }
}
