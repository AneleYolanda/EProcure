namespace EProcure.Web.Domain;

/// <summary>
/// One FUNCTIONALITY criterion of a tender, e.g. "Relevant experience (track record)" with a weight of 40.
/// The weights of a tender's criteria add up to 100, and the tender's FunctionalityThreshold is the minimum
/// percentage a bid must score to be evaluated further on price and preference (Preferential Procurement
/// Regulations, 2022: functionality is a qualifying stage, not part of the 80/20 or 90/10 points).
/// Set by the SCM Officer with the tender and locked when it is published. POPIA: no personal information.
/// </summary>
public class TenderFunctionalityCriterion
{
    public int Id { get; set; }

    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Share of the functionality score, 1 to 100. A tender's weights add up to 100.</summary>
    public int Weight { get; set; }

    public int SortOrder { get; set; }
}
