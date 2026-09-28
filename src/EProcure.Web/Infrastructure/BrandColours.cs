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

    /// <summary>WCAG AA minimum contrast for normal-size text.</summary>
    public const double MinimumTextContrast = 4.5;

    /// <summary>
    /// The same colour, darkened only as much as needed for WHITE text on it to reach WCAG AA (4.5:1).
    /// Used as the background of filled buttons and selected chips (DECISIONS D42), so any organisation's
    /// brand colour stays readable. Colours that already pass are returned unchanged.
    /// </summary>
    public static string ReadableWithWhiteText(string hex)
    {
        var colour = Safe(hex, DefaultAccent);
        int r = Convert.ToInt32(colour[1..3], 16), g = Convert.ToInt32(colour[3..5], 16), b = Convert.ToInt32(colour[5..7], 16);
        for (var factor = 1.0; factor > 0; factor -= 0.01)
        {
            int dr = (int)Math.Round(r * factor), dg = (int)Math.Round(g * factor), db = (int)Math.Round(b * factor);
            if (ContrastWithWhite(dr, dg, db) >= MinimumTextContrast)
                return $"#{dr:X2}{dg:X2}{db:X2}";
        }
        return "#000000";
    }

    /// <summary>WCAG 2 contrast ratio between white and the colour (1 to 21).</summary>
    public static double ContrastWithWhite(int r, int g, int b) => 1.05 / (RelativeLuminance(r, g, b) + 0.05);

    public static double ContrastWithWhite(string hex)
    {
        var colour = Safe(hex, DefaultAccent);
        return ContrastWithWhite(Convert.ToInt32(colour[1..3], 16), Convert.ToInt32(colour[3..5], 16), Convert.ToInt32(colour[5..7], 16));
    }

    private static double RelativeLuminance(int r, int g, int b)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }
}
