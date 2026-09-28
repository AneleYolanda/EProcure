using EProcure.Web.Infrastructure;

namespace EProcure.Tests.Rules;

/// <summary>
/// Buttons and links use a shade of the brand colour that meets WCAG AA (4.5:1) against white AND the light grey
/// and pale-blue surfaces of the design, both as a background for white text and as text colour.
/// </summary>
public class BrandColoursTests
{
    private const string White = "#FFFFFF";
    private const string PageGrey = "#F7FAFC";   // --ep-page, e.g. the POPIA consent box
    private const string SoftGrey = "#EEF4F9";   // --ep-soft

    [Theory]
    [InlineData("#1CA3EC", "#1472A5")] // eProcure blue: 2.8:1 in the design
    [InlineData("#E4572E", "#BB4726")] // MVLM coral: 3.4:1 in the design
    public void Brand_colours_are_darkened_just_enough(string brand, string expected)
    {
        Assert.True(BrandColours.Contrast(brand, White) < BrandColours.MinimumTextContrast);

        Assert.Equal(expected, BrandColours.ReadableShade(brand));
    }

    [Theory]
    [InlineData("#1CA3EC")]
    [InlineData("#E4572E")]
    [InlineData("#FFFF00")] // yellow: the hardest case
    [InlineData("#FFFFFF")]
    [InlineData("#C9A227")]
    [InlineData("#7FD0F7")]
    public void The_shade_is_readable_on_every_light_surface(string brand)
    {
        var shade = BrandColours.ReadableShade(brand);

        foreach (var surface in new[] { White, PageGrey, SoftGrey, BrandColours.DarkestLightSurface })
            Assert.True(BrandColours.Contrast(shade, surface) >= BrandColours.MinimumTextContrast, $"{shade} on {surface}");
    }

    [Theory]
    [InlineData("#0F1B33")] // RBIDZ navy
    [InlineData("#00695C")] // MVLM teal
    public void Colours_that_already_pass_are_unchanged(string colour)
    {
        Assert.Equal(colour, BrandColours.ReadableShade(colour));
    }

    [Fact]
    public void An_invalid_value_falls_back_to_the_default_and_cannot_inject_css()
    {
        Assert.Equal("#1472A5", BrandColours.ReadableShade("red; } body { display:none"));
    }

    [Fact]
    public void Contrast_matches_the_wcag_reference_values()
    {
        Assert.Equal(21.0, BrandColours.Contrast("#000000", White), 1);
        Assert.Equal(1.0, BrandColours.Contrast(White, White), 2);
    }
}
