namespace EProcure.Web.Domain;

/// <summary>
/// RECORDS a human award decision. It does not make one. There is no scoring algorithm in
/// the system that picks a winner: an authorised official (e.g. after the Bid Adjudication
/// Committee sits) captures who was awarded, by whom, and why.
///
/// One award per tender in the MVP (unique TenderId). Multi-award tenders can be supported
/// later by dropping that unique index.
/// POPIA: references the deciding official. Access: the tender's organisation; the outcome
/// (not the rationale) may later be published to suppliers.
/// </summary>
public class AwardRecord
{
    public int Id { get; set; }

    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;

    /// <summary>The successful submission (must belong to the same tender; enforced in code).</summary>
    public int SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;

    /// <summary>Date the committee / delegated official decided.</summary>
    public DateTime DecisionDateUtc { get; set; }

    /// <summary>E.g. BAC minute number or delegation reference.</summary>
    public string? CommitteeReference { get; set; }

    /// <summary>Human-written reasons for the decision. Required.</summary>
    public string Rationale { get; set; } = string.Empty;

    public decimal? AwardedAmount { get; set; }

    /// <summary>The user who captured the decision in the system.</summary>
    public string RecordedByUserId { get; set; } = string.Empty;
    public ApplicationUser RecordedByUser { get; set; } = null!;

    public DateTime RecordedAtUtc { get; set; }
}
