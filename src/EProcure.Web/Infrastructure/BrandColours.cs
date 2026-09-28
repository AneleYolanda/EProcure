using System.Text.RegularExpressions;

namespace EProcure.Web.Infrastructure;

/// <summary>
/// Organisation colours come from the database and are written into a &lt;style&gt; block.
/// Only a strict "#RRGGBB" value is ever written, so a bad or malicious value cannot inject CSS.
/// </summary>
public static partial class BrandColours
{
    public const string DefaultPrimary = "#0F1B33"; // eProcure navy (design)
    public const string DefaultAccent = "#1CA3EC";  // eProcure blue (design)

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColour();

    public static string Safe(string? value, string fallback) =>
        value is not null && HexColour().IsMatch(value) ? value : fallback;
}
