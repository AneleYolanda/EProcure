using EProcure.Web.Infrastructure;

namespace EProcure.Tests.Rules;

/// <summary>Filled buttons use a version of the brand colour that white text can be read on (WCAG AA, 4.5:1).</summary>
public class BrandColoursTests
{
    [Theory]
    [InlineData("#1CA3EC", "#157CB3")] // eProcure blue: 2.8:1 in the design, 4.6:1 after
    [InlineData("#E4572E", "#CB4D29")] // MVLM coral: 3.4:1 in the design, 4.5:1 after
    public void Brand_colours_are_darkened_just_enough_for_white_text(string brand, string expected)
    {
        Assert.True(BrandColours.ContrastWithWhite(brand) < BrandColours.MinimumTextContrast);

        var readable = BrandColours.ReadableWithWhiteText(brand);

        Assert.Equal(expected, readable);
        Assert.True(BrandColours.ContrastWithWhite(readable) >= BrandColours.MinimumTextContrast);
    }

    [Theory]
    [InlineData("#0F1B33")] // RBIDZ navy
    [InlineData("#00695C")] // MVLM teal
    public void Colours_that_already_pass_are_unchanged(string colour)
    {
        Assert.Equal(colour, BrandColours.ReadableWithWhiteText(colour));
    }

    [Theory]
    [InlineData("#FFFF00")] // yellow: the hardest case
    [InlineData("#FFFFFF")]
    [InlineData("#C9A227")]
    [InlineData("#7FD0F7")]
    public void Any_colour_ends_up_readable(string colour)
    {
        Assert.True(BrandColours.ContrastWithWhite(BrandColours.ReadableWithWhiteText(colour)) >= BrandColours.MinimumTextContrast);
    }

    [Fact]
    public void An_invalid_value_falls_back_to_the_default_and_cannot_inject_css()
    {
        Assert.Equal("#157CB3", BrandColours.ReadableWithWhiteText("red; } body { display:none"));
    }

    [Fact]
    public void Contrast_matches_the_wcag_reference_values()
    {
        Assert.Equal(21.0, BrandColours.ContrastWithWhite("#000000"), 1);
        Assert.Equal(1.0, BrandColours.ContrastWithWhite("#FFFFFF"), 2);
    }
}
