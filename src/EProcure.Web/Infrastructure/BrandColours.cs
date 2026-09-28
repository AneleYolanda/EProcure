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
    /// The darkest light background that text sits on in the design (the pale-blue info box, --ep-info-bg).
    /// A shade that passes against it also passes against white and every lighter grey.
    /// </summary>
    public const string DarkestLightSurface = "#E3F1FB";

    /// <summary>
    /// The same colour, darkened only as much as needed to reach WCAG AA (4.5:1) against the light surfaces
    /// of the design. It is then readable both ways round: white text on it (filled buttons, selected chips)
    /// and it as text on white or light grey (links, text buttons). DECISIONS D42. Colours that already pass
    /// are returned unchanged.
    /// </summary>
    public static string ReadableShade(string hex)
    {
        var colour = Safe(hex, DefaultAccent);
        var (r, g, b) = Rgb(colour);
        var surface = RelativeLuminance(Rgb(DarkestLightSurface));
        for (var factor = 1.0; factor > 0; factor -= 0.01)
        {
            int dr = (int)Math.Round(r * factor), dg = (int)Math.Round(g * factor), db = (int)Math.Round(b * factor);
            if (Ratio(surface, RelativeLuminance((dr, dg, db))) >= MinimumTextContrast)
                return $"#{dr:X2}{dg:X2}{db:X2}";
        }
        return "#000000";
    }

    /// <summary>WCAG 2 contrast ratio between two colours (1 to 21).</summary>
    public static double Contrast(string hexA, string hexB) =>
        Ratio(RelativeLuminance(Rgb(Safe(hexA, DefaultAccent))), RelativeLuminance(Rgb(Safe(hexB, DefaultAccent))));

    private static double Ratio(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    private static (int R, int G, int B) Rgb(string hex) =>
        (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    private static double RelativeLuminance((int R, int G, int B) c)
    {
        static double Channel(int value)
        {
            var v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
