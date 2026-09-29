using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Tests.Rules;

/// <summary>The PPPFA 2022 arithmetic: price points, preference points and ranking.</summary>
public class EvaluationRulesTests
{
    private const PreferencePointSystem EightyTwenty = PreferencePointSystem.EightyTwenty;
    private const PreferencePointSystem NinetyTen = PreferencePointSystem.NinetyTen;

    [Theory]
    [InlineData(EightyTwenty, 80)]
    [InlineData(NinetyTen, 90)]
    public void The_lowest_price_gets_full_price_points(PreferencePointSystem system, decimal expected)
    {
        Assert.Equal(expected, EvaluationRules.PricePoints(1_150_000m, 1_150_000m, system));
    }

    [Fact]
    public void Price_points_follow_the_regulation_formula()
    {
        // 80 x (1 - (1 240 000 - 1 150 000) / 1 150 000) = 73.739... -> 73.74
        Assert.Equal(73.74m, EvaluationRules.PricePoints(1_240_000m, 1_150_000m, EightyTwenty));
        // 90 x (1 - (110 - 100) / 100) = 81
        Assert.Equal(81m, EvaluationRules.PricePoints(110m, 100m, NinetyTen));
    }

    [Fact]
    public void A_price_double_the_lowest_or_more_gets_zero_never_negative()
    {
        Assert.Equal(0m, EvaluationRules.PricePoints(200m, 100m, EightyTwenty));
        Assert.Equal(0m, EvaluationRules.PricePoints(350m, 100m, EightyTwenty));
    }

    [Fact]
    public void Invalid_prices_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationRules.PricePoints(0m, 100m, EightyTwenty));
        Assert.Throws<ArgumentException>(() => EvaluationRules.PricePoints(90m, 100m, EightyTwenty));
    }

    [Theory]
    [InlineData(BbbeeLevel.Level1, EightyTwenty, 20)]
    [InlineData(BbbeeLevel.Level2, EightyTwenty, 18)]
    [InlineData(BbbeeLevel.Level4, EightyTwenty, 12)]
    [InlineData(BbbeeLevel.Level8, EightyTwenty, 2)]
    [InlineData(BbbeeLevel.NonCompliant, EightyTwenty, 0)]
    [InlineData(BbbeeLevel.Level1, NinetyTen, 10)]
    [InlineData(BbbeeLevel.Level3, NinetyTen, 6)]
    [InlineData(BbbeeLevel.NonCompliant, NinetyTen, 0)]
    public void Preference_points_follow_the_bbbee_table(BbbeeLevel level, PreferencePointSystem system, decimal expected)
    {
        Assert.Equal(expected, EvaluationRules.PreferencePoints(level, system));
    }

    [Fact]
    public void Ranking_combines_price_and_preference_points()
    {
        // The seeded demo tender: the cheapest bid is NOT ranked first once preference points are added.
        var lines = EvaluationRules.Score(new[]
        {
            new BidInput(1, true, true, 1_380_000m, BbbeeLevel.Level1), // 64.00 + 20 = 84.00
            new BidInput(2, true, true, 1_150_000m, BbbeeLevel.Level6), // 80.00 +  6 = 86.00
            new BidInput(3, true, true, 1_240_000m, BbbeeLevel.Level2)  // 73.74 + 18 = 91.74
        }, EightyTwenty);

        Assert.Equal(new[] { 3, 2, 1 }, lines.Select(l => l.SubmissionId));
        Assert.Equal(new decimal?[] { 91.74m, 86m, 84m }, lines.Select(l => l.TotalPoints));
        Assert.Equal(new int?[] { 1, 2, 3 }, lines.Select(l => l.Rank));
        Assert.All(lines, l => Assert.False(l.TiedForRank));
    }

    [Fact]
    public void Equal_totals_are_separated_by_preference_points()
    {
        var lines = EvaluationRules.Score(new[]
        {
            new BidInput(1, true, true, 100m, BbbeeLevel.Level4), // 80 + 12 = 92
            new BidInput(2, true, true, 105m, BbbeeLevel.Level1)  // 76 + 20 = 96 -> clearly first; make a real tie below
        }, EightyTwenty);
        Assert.Equal(2, lines[0].SubmissionId);

        // 10% dearer loses 8 price points; Level 1 (20) vs Level 4 (12) wins 8 back: both total 92.
        var tied = EvaluationRules.Score(new[]
        {
            new BidInput(1, true, true, 100m, BbbeeLevel.Level4), // 80 + 12 = 92
            new BidInput(2, true, true, 110m, BbbeeLevel.Level1)  // 72 + 20 = 92
        }, EightyTwenty);

        Assert.Equal(new[] { 2, 1 }, tied.Select(l => l.SubmissionId)); // higher preference points first
        Assert.Equal(new int?[] { 1, 2 }, tied.Select(l => l.Rank));
        Assert.All(tied, l => Assert.False(l.TiedForRank));
    }

    [Fact]
    public void Bids_equal_on_total_and_preference_share_a_rank_and_are_flagged_as_tied()
    {
        var lines = EvaluationRules.Score(new[]
        {
            new BidInput(1, true, true, 100m, BbbeeLevel.Level2),
            new BidInput(2, true, true, 100m, BbbeeLevel.Level2),
            new BidInput(3, true, true, 150m, BbbeeLevel.Level2)
        }, EightyTwenty);

        Assert.Equal(new int?[] { 1, 1, 3 }, lines.Select(l => l.Rank));
        Assert.True(lines[0].TiedForRank);
        Assert.True(lines[1].TiedForRank);
        Assert.False(lines[2].TiedForRank);
    }

    [Fact]
    public void Non_responsive_and_unevaluated_bids_are_listed_but_not_scored()
    {
        var lines = EvaluationRules.Score(new[]
        {
            new BidInput(1, true, false, null, BbbeeLevel.Level1),  // non-responsive
            new BidInput(2, false, false, null, BbbeeLevel.Level1), // not evaluated yet
            new BidInput(3, true, true, 500m, BbbeeLevel.Level8)
        }, EightyTwenty);

        Assert.Equal(3, lines.Count);
        var scored = Assert.Single(lines, l => l.Rank is not null);
        Assert.Equal(3, scored.SubmissionId);
        Assert.Equal(82m, scored.TotalPoints); // the only responsive bid is also the lowest price: 80 + 2
    }

    [Fact]
    public void Without_a_responsive_bid_nothing_is_ranked()
    {
        var lines = EvaluationRules.Score(new[] { new BidInput(1, true, false, null, BbbeeLevel.Level1) }, EightyTwenty);

        Assert.Null(Assert.Single(lines).Rank);
    }

    // ------------------------------------------------------------------ functionality

    [Fact]
    public void Functionality_is_the_weighted_rating_as_a_percentage()
    {
        // 40 x 4/5 + 30 x 3/5 + 30 x 5/5 = 32 + 18 + 30 = 80
        Assert.Equal(80m, EvaluationRules.FunctionalityScore(new[] { (40, 4), (30, 3), (30, 5) }));
        Assert.Equal(100m, EvaluationRules.FunctionalityScore(new[] { (60, 5), (40, 5) }));
        Assert.Equal(0m, EvaluationRules.FunctionalityScore(new[] { (100, 0) }));
        // 33 x 2/5 = 13.2; 67 x 3/5 = 40.2 -> 53.4
        Assert.Equal(53.4m, EvaluationRules.FunctionalityScore(new[] { (33, 2), (67, 3) }));
    }

    [Fact]
    public void Ratings_outside_zero_to_five_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationRules.FunctionalityScore(new[] { (100, 6) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationRules.FunctionalityScore(new[] { (100, -1) }));
    }

    [Theory]
    [InlineData(70, 70, true)]    // exactly the threshold meets it
    [InlineData(69.99, 70, false)]
    [InlineData(100, 70, true)]
    public void The_threshold_is_a_minimum(double score, int threshold, bool meets)
    {
        Assert.Equal(meets, EvaluationRules.MeetsThreshold((decimal)score, threshold));
    }

    [Fact]
    public void A_bid_below_the_functionality_threshold_is_not_scored_and_does_not_set_the_lowest_price()
    {
        var lines = EvaluationRules.Score(new[]
        {
            new BidInput(1, true, true, 100m, BbbeeLevel.Level1, PassedFunctionality: false), // cheapest, but failed functionality
            new BidInput(2, true, true, 110m, BbbeeLevel.Level1),
            new BidInput(3, true, true, 121m, BbbeeLevel.Level1)
        }, EightyTwenty);

        var failed = lines.Single(l => l.SubmissionId == 1);
        Assert.Null(failed.Rank);
        Assert.Null(failed.TotalPoints);
        // 110 is now the lowest acceptable price: it gets the full 80 price points.
        Assert.Equal(80m, lines.Single(l => l.SubmissionId == 2).PricePoints);
        Assert.Equal(1, lines.Single(l => l.SubmissionId == 2).Rank);
    }
}
