using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Services;

/// <summary>One bid as the scoresheet needs it: the BEC's inputs plus the bidder's declared B-BBEE level.</summary>
public record BidInput(int SubmissionId, bool Evaluated, bool IsResponsive, decimal? Price, BbbeeLevel Level);

/// <summary>One line of the calculated scoresheet. Points and rank are NULL for bids that are not scored.</summary>
public record ScoreLine(int SubmissionId, bool Evaluated, bool IsResponsive, decimal? Price,
    decimal? PricePoints, decimal? PreferencePoints, decimal? TotalPoints, int? Rank, bool TiedForRank);

/// <summary>
/// The arithmetic of the Preferential Procurement Regulations, 2022 (PPPFA), in ONE place and with no database,
/// so it is easy to check against the regulations and to test. People decide responsiveness and read the price
/// from the pricing schedule; this class only calculates.
///
///   Price points     Ps = 80 x (1 - (Pt - Pmin) / Pmin)   (80/20 system)
///                    Ps = 90 x (1 - (Pt - Pmin) / Pmin)   (90/10 system)
///                    Pt = the bid's price, Pmin = the lowest ACCEPTABLE (responsive) price. Never below 0.
///   Preference       the specific goal used here is the bidder's B-BBEE status level, scored with the
///   points           standard table (80/20: Level 1 = 20 ... Level 8 = 2; 90/10: Level 1 = 10 ... Level 8 = 1;
///                    non-compliant = 0). An organ of state that sets other specific goals changes this table.
///   Ranking          highest total first; equal totals are separated by the higher preference points; bids
///                    still equal share a rank and the regulations require the award to be decided by drawing lots.
/// Points are rounded to 2 decimals (half away from zero), as they are printed on the scoresheet.
/// </summary>
public static class EvaluationRules
{
    public static int MaxPricePoints(PreferencePointSystem system) => system == PreferencePointSystem.NinetyTen ? 90 : 80;

    public static int MaxPreferencePoints(PreferencePointSystem system) => system == PreferencePointSystem.NinetyTen ? 10 : 20;

    public static string Describe(PreferencePointSystem system) => system == PreferencePointSystem.NinetyTen ? "90/10" : "80/20";

    public static decimal PricePoints(decimal price, decimal lowestPrice, PreferencePointSystem system)
    {
        if (price <= 0 || lowestPrice <= 0) throw new ArgumentOutOfRangeException(nameof(price), "Prices must be greater than zero.");
        if (price < lowestPrice) throw new ArgumentException("The lowest price cannot be higher than the bid's price.", nameof(lowestPrice));

        var points = MaxPricePoints(system) * (1m - (price - lowestPrice) / lowestPrice);
        return Round(Math.Max(0m, points));
    }

    public static decimal PreferencePoints(BbbeeLevel level, PreferencePointSystem system)
    {
        var eightyTwenty = level switch
        {
            BbbeeLevel.Level1 => 20m, BbbeeLevel.Level2 => 18m, BbbeeLevel.Level3 => 14m, BbbeeLevel.Level4 => 12m,
            BbbeeLevel.Level5 => 8m, BbbeeLevel.Level6 => 6m, BbbeeLevel.Level7 => 4m, BbbeeLevel.Level8 => 2m,
            _ => 0m
        };
        var ninetyTen = level switch
        {
            BbbeeLevel.Level1 => 10m, BbbeeLevel.Level2 => 9m, BbbeeLevel.Level3 => 6m, BbbeeLevel.Level4 => 5m,
            BbbeeLevel.Level5 => 4m, BbbeeLevel.Level6 => 3m, BbbeeLevel.Level7 => 2m, BbbeeLevel.Level8 => 1m,
            _ => 0m
        };
        return system == PreferencePointSystem.NinetyTen ? ninetyTen : eightyTwenty;
    }

    /// <summary>
    /// Scores and ranks every bid. Only evaluated, responsive bids with a price are scored; the others are
    /// returned unscored (not evaluated yet, or excluded as non-responsive) so the scoresheet shows every bid.
    /// </summary>
    public static IReadOnlyList<ScoreLine> Score(IEnumerable<BidInput> bids, PreferencePointSystem system)
    {
        var all = bids.ToList();
        var scorable = all.Where(b => b.Evaluated && b.IsResponsive && b.Price > 0).ToList();
        if (scorable.Count == 0)
            return all.Select(b => new ScoreLine(b.SubmissionId, b.Evaluated, b.IsResponsive, b.Price, null, null, null, null, false)).ToList();

        var lowest = scorable.Min(b => b.Price!.Value);
        var scored = scorable.Select(b =>
        {
            var price = PricePoints(b.Price!.Value, lowest, system);
            var preference = PreferencePoints(b.Level, system);
            return (Bid: b, Price: price, Preference: preference, Total: Round(price + preference));
        })
        .OrderByDescending(x => x.Total).ThenByDescending(x => x.Preference).ThenBy(x => x.Bid.SubmissionId)
        .ToList();

        // Competition ranking (1, 1, 3): bids equal on total AND preference points share a rank.
        var lines = new Dictionary<int, ScoreLine>();
        for (var i = 0; i < scored.Count; i++)
        {
            var current = scored[i];
            var rank = i + 1;
            if (i > 0 && SameScore(scored[i - 1], current)) rank = lines[scored[i - 1].Bid.SubmissionId].Rank!.Value;
            var tied = scored.Where((other, j) => j != i && SameScore(other, current)).Any();
            lines[current.Bid.SubmissionId] = new ScoreLine(current.Bid.SubmissionId, true, true, current.Bid.Price,
                current.Price, current.Preference, current.Total, rank, tied);
        }

        return all
            .Select(b => lines.TryGetValue(b.SubmissionId, out var line)
                ? line
                : new ScoreLine(b.SubmissionId, b.Evaluated, b.IsResponsive, b.Price, null, null, null, null, false))
            .OrderBy(l => l.Rank ?? int.MaxValue).ThenBy(l => l.SubmissionId)
            .ToList();

        static bool SameScore((BidInput Bid, decimal Price, decimal Preference, decimal Total) a,
                              (BidInput Bid, decimal Price, decimal Preference, decimal Total) b) =>
            a.Total == b.Total && a.Preference == b.Preference;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
