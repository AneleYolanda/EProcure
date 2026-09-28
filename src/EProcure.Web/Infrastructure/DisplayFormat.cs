using System.Globalization;

namespace EProcure.Web.Infrastructure;

/// <summary>
/// Formats money and dates exactly the way the design shows them ("R8 600 000", "R500.00",
/// "28 Sep 2026", "Closes in 12 days"). Kept in one place so every screen is consistent.
/// </summary>
public static class DisplayFormat
{
    /// <summary>Whole rand with spaces between thousands, e.g. R8 600 000.</summary>
    public static string Rand(decimal amount) =>
        "R" + amount.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>Rand and cents, e.g. R1 500.00. A zero fee is shown as "Free".</summary>
    public static string Fee(decimal amount) =>
        amount == 0 ? "Free" : "R" + amount.ToString("#,0.00", CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>Date in South African time, e.g. 28 Sep 2026.</summary>
    public static string Date(DateTime utc) =>
        SaTime.ToSast(utc).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Date and time in South African time, e.g. 28 Sep 2026, 11:00.</summary>
    public static string DateTime(DateTime utc) =>
        SaTime.ToSast(utc).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Closes in 12 days", "Closes tomorrow", "Closes today" or "Closed".</summary>
    public static string ClosesIn(DateTime closingUtc, DateTime nowUtc)
    {
        if (closingUtc <= nowUtc) return "Closed";
        var days = (SaTime.ToSast(closingUtc).Date - SaTime.ToSast(nowUtc).Date).Days;
        return days switch
        {
            0 => "Closes today",
            1 => "Closes tomorrow",
            _ => $"Closes in {days} days"
        };
    }

    /// <summary>Two-letter initials for avatar tiles, e.g. "Supplier One" → "SO".</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => (parts[0][0].ToString() + parts[^1][0]).ToUpperInvariant()
        };
    }

    /// <summary>+27820000004 → 082 000 0004 (how South Africans write a cellphone number).</summary>
    public static string Cellphone(string? e164)
    {
        if (string.IsNullOrEmpty(e164) || !e164.StartsWith("+27") || e164.Length != 12) return e164 ?? string.Empty;
        var local = "0" + e164[3..];
        return $"{local[..3]} {local[3..6]} {local[6..]}";
    }
}
