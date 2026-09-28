using EProcure.Web.Services;

namespace EProcure.Web.ViewModels.Tenders;

/// <summary>Everything one tender card in the feed shows (design screen "sFeed").</summary>
public class TenderCardViewModel
{
    public int Id { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string OrganisationName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal? EstimatedValue { get; init; }
    public decimal TenderFee { get; init; }
    public DateTime ClosingDateUtc { get; init; }

    /// <summary>Result of the B-BBEE rule for the signed-in supplier's company.</summary>
    public EligibilityResult Eligibility { get; init; } = new(EligibilityOutcome.Unknown, string.Empty);
}
