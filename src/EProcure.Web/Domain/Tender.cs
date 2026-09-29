using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// A tender (bid invitation) published by ONE organisation. TENANT-SCOPED via OrganisationId.
/// POPIA: no personal information (CreatedByUserId is an internal reference only).
/// </summary>
public class Tender
{
    public int Id { get; set; }

    /// <summary>The tenant key. Every admin query is filtered on this.</summary>
    public int OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    /// <summary>The organisation's own bid number, e.g. "RBIDZ/2026/014". Unique within one organisation.</summary>
    public string ReferenceNumber { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Stored in UTC; displayed in South African time (UTC+2).</summary>
    public DateTime ClosingDateUtc { get; set; }

    /// <summary>Non-refundable document fee in Rand. 0 = free.</summary>
    public decimal TenderFee { get; set; }

    /// <summary>
    /// Estimated project/contract value in Rand. Optional; shown on tender details.
    /// </summary>
    public decimal? EstimatedValue { get; set; }

    /// <summary>
    /// Prequalification: the WORST B-BBEE level still allowed to apply. NULL = no requirement.
    /// E.g. Level4 means Levels 1-4 may apply; 5-8 and NonCompliant are blocked (hard stop).
    /// </summary>
    public BbbeeLevel? MinimumBbbeeLevel { get; set; }

    /// <summary>
    /// Preference point system that will be used to evaluate this tender (80/20 or 90/10).
    /// Pre-filled from the organisation's default. Chosen by people; the evaluation then applies its formula.
    /// </summary>
    public PreferencePointSystem PointSystem { get; set; } = PreferencePointSystem.EightyTwenty;

    /// <summary>
    /// Functionality stage: the minimum percentage (1 to 100) a bid must score on the functionality criteria to be
    /// evaluated on price and preference. NULL = no functionality stage (the tender has no criteria).
    /// </summary>
    public int? FunctionalityThreshold { get; set; }

    public TenderStatus Status { get; set; } = TenderStatus.Draft;

    /// <summary>When and why the tender was cancelled (both required when Status = Cancelled).</summary>
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    // ---- Evaluation (BEC) and adjudication (BAC) ----

    /// <summary>When the BEC submitted its scoresheet and recommendation to the BAC (locks the scoresheet).</summary>
    public DateTime? EvaluationSubmittedAtUtc { get; set; }
    public string? EvaluationSubmittedByUserId { get; set; }
    public ApplicationUser? EvaluationSubmittedByUser { get; set; }

    /// <summary>The BEC's reasons, required when it recommends a bid other than the highest-ranked one.</summary>
    public string? RecommendationReason { get; set; }

    /// <summary>Why the BAC last sent the evaluation back to the BEC (shown to the BEC).</summary>
    public string? BacReturnNote { get; set; }

    // ---- Publication approval (when the organisation requires a second SCM Officer) ----

    /// <summary>Set while the draft waits for approval; the draft is locked against editing meanwhile.</summary>
    public DateTime? ApprovalRequestedAtUtc { get; set; }
    public string? ApprovalRequestedByUserId { get; set; }
    public ApplicationUser? ApprovalRequestedByUser { get; set; }

    /// <summary>Who approved (and thereby published) the tender.</summary>
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }

    /// <summary>Why the approver last sent the draft back (shown to the SCM Officers).</summary>
    public string? ApprovalReturnNote { get; set; }

    /// <summary>The required-documents checklist.</summary>
    public ICollection<TenderRequirement> Requirements { get; set; } = new List<TenderRequirement>();

    /// <summary>Functionality criteria and their weights (empty when there is no functionality stage).</summary>
    public ICollection<TenderFunctionalityCriterion> FunctionalityCriteria { get; set; } = new List<TenderFunctionalityCriterion>();

    public bool HasFunctionalityStage => FunctionalityThreshold is not null;
    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
    public AwardRecord? Award { get; set; }
}
