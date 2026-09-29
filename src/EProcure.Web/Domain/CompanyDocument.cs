using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// One piece of the company's TRACK RECORD: proof of work done before (a reference letter, a completion certificate,
/// the company profile). Kept on the company, not on a bid, so the supplier uploads it once, at registration or later,
/// and every bid carries it.
///
/// What a committee sees: the documents the company held AT THE TENDER'S CLOSING DATE (uploaded before it and not
/// removed before it; see <see cref="HeldAt"/>). "Remove" therefore only sets <see cref="RemovedAtUtc"/>: the file
/// is kept, because a bid that closed earlier may still rely on it.
///
/// POPIA: reference letters can name people at the client. Access: the company's own users; an organisation's
/// staff only through a bid to one of its tenders, after that tender has closed.
/// </summary>
public class CompanyDocument
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public CompanyDocumentKind Kind { get; set; }

    /// <summary>What the work was, e.g. "Resurfacing of 12 km of municipal roads".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Who the work was done for (the client). Optional for a company profile.</summary>
    public string? ClientName { get; set; }

    public int? YearCompleted { get; set; }

    /// <summary>Value of that contract in Rand, if the supplier chooses to state it.</summary>
    public decimal? ContractValue { get; set; }

    // The file itself, handled exactly like a bid document (see UploadedDocument).
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;

    public string UploadedByUserId { get; set; } = string.Empty;
    public ApplicationUser UploadedByUser { get; set; } = null!;
    public DateTime UploadedAtUtc { get; set; }

    /// <summary>When the supplier removed it from their profile. NULL = still on the profile.</summary>
    public DateTime? RemovedAtUtc { get; set; }

    /// <summary>True if the company held this document at the given moment (a tender's closing date).</summary>
    public bool HeldAt(DateTime momentUtc) => UploadedAtUtc <= momentUtc && (RemovedAtUtc is null || RemovedAtUtc > momentUtc);
}
