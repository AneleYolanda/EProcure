using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// Append-only timeline of a submission's status. Powers "track my application" for the
/// supplier and is part of the procurement record. Rows are only ever INSERTED.
/// POPIA: references the user who made the change. Access follows the parent Submission.
/// </summary>
public class SubmissionStatusHistory
{
    public int Id { get; set; }

    public int SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;

    /// <summary>NULL for the very first entry.</summary>
    public SubmissionStatus? FromStatus { get; set; }
    public SubmissionStatus ToStatus { get; set; }

    public string ChangedByUserId { get; set; } = string.Empty;
    public ApplicationUser ChangedByUser { get; set; } = null!;

    public DateTime ChangedAtUtc { get; set; }

    /// <summary>Optional explanation, shown to the supplier.</summary>
    public string? Note { get; set; }
}
