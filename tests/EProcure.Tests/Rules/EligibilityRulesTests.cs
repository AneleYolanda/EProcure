using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Tests.Rules;

/// <summary>The B-BBEE hard stop: a LOWER level number is BETTER, the same rule for every bidder.</summary>
public class EligibilityRulesTests
{
    [Theory]
    [InlineData(BbbeeLevel.Level1, BbbeeLevel.Level1)]   // exactly the minimum
    [InlineData(BbbeeLevel.Level1, BbbeeLevel.Level4)]   // better than the minimum
    [InlineData(BbbeeLevel.Level4, BbbeeLevel.Level4)]
    [InlineData(BbbeeLevel.Level8, BbbeeLevel.Level8)]
    public void Company_at_or_better_than_the_minimum_qualifies(BbbeeLevel company, BbbeeLevel minimum)
    {
        Assert.Equal(EligibilityOutcome.Qualifies, EligibilityRules.CheckBbbee(company, minimum).Outcome);
    }

    [Theory]
    [InlineData(BbbeeLevel.Level2, BbbeeLevel.Level1)]
    [InlineData(BbbeeLevel.Level5, BbbeeLevel.Level4)]
    [InlineData(BbbeeLevel.NonCompliant, BbbeeLevel.Level8)]
    public void Company_worse_than_the_minimum_is_not_eligible(BbbeeLevel company, BbbeeLevel minimum)
    {
        var result = EligibilityRules.CheckBbbee(company, minimum);

        Assert.Equal(EligibilityOutcome.NotEligible, result.Outcome);
        Assert.Contains("or better", result.Reason);
    }

    [Fact]
    public void Tender_without_a_minimum_accepts_any_level_even_non_compliant()
    {
        Assert.Equal(EligibilityOutcome.Qualifies, EligibilityRules.CheckBbbee(BbbeeLevel.NonCompliant, null).Outcome);
    }

    [Fact]
    public void Supplier_without_a_company_is_unknown_not_eligible()
    {
        Assert.Equal(EligibilityOutcome.Unknown, EligibilityRules.CheckBbbee(null, BbbeeLevel.Level4).Outcome);
    }

    [Fact]
    public void Non_compliant_is_described_in_words()
    {
        Assert.Equal("Non-compliant", EligibilityRules.Describe(BbbeeLevel.NonCompliant));
        Assert.Equal("B-BBEE Level 3", EligibilityRules.Describe(BbbeeLevel.Level3));
    }
}
