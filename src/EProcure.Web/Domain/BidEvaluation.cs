namespace EProcure.Web.Domain;

/// <summary>
/// The Bid Evaluation Committee's record for ONE bid (one per submission).
///
/// What people decide: whether the bid is responsive (and why not), and the bid price as read from the
/// bidder's pricing schedule. What the system calculates: price points and preference points with the
/// formulas prescribed by the Preferential Procurement Regulations, 2022 (EvaluationRules). Nobody types
/// points in, so the arithmetic cannot be "adjusted".
///
/// The points and rank are FROZEN into this row when the BEC submits its recommendation, so the scoresheet
/// the BAC adjudicates is exactly the one the BEC signed off, even if something changes later.
/// Access: the tender's organisation only (tenant query filter).
/// </summary>
public class BidEvaluation
{
    public int Id { get; set; }

    public int SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;

    /// <summary>Meets the tender's administrative and mandatory requirements (BEC judgement).</summary>
    public bool IsResponsive { get; set; }

    /// <summary>Required when not responsive. The bidder is told this reason when the tender is decided.</summary>
    public string? NonResponsiveReason { get; set; }

    /// <summary>
    /// Functionality percentage calculated from <see cref="FunctionalityRatings"/> when the evaluation is saved.
    /// NULL when the tender has no functionality stage or the bid is not responsive.
    /// </summary>
    public decimal? FunctionalityScore { get; set; }

    /// <summary>The BEC's 0-5 rating per functionality criterion.</summary>
    public ICollection<FunctionalityRating> FunctionalityRatings { get; set; } = new List<FunctionalityRating>();

    /// <summary>Total bid price including VAT, as stated in the bidder's pricing schedule. Required for a responsive bid that passes functionality.</summary>
    public decimal? BidPrice { get; set; }

    /// <summary>Committee notes for the record (not shown to the bidder).</summary>
    public string? Notes { get; set; }

    public string EvaluatedByUserId { get; set; } = string.Empty;
    public ApplicationUser EvaluatedByUser { get; set; } = null!;
    public DateTime EvaluatedAtUtc { get; set; }

    // Frozen when the BEC submits its recommendation (NULL while the evaluation is still open).
    public decimal? PricePoints { get; set; }
    public decimal? PreferencePoints { get; set; }
    public decimal? TotalPoints { get; set; }
    public int? Rank { get; set; }

    /// <summary>The bid the BEC recommends for award (at most one per tender).</summary>
    public bool IsRecommended { get; set; }
}
