using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// One compliance document on a supplier's company profile (CSD report, tax compliance, B-BBEE, certified IDs, ...),
/// uploaded once and attached to bids from there. Each type has a validity rule (ComplianceRules), so eProcure knows when
/// it expires and reminds the supplier 30 and 7 days before, and on the day.
///
/// Only one document per type is CURRENT (ArchivedAtUtc NULL). Uploading a newer one, or removing it, archives the old one;
/// nothing is deleted, because a bid that used it keeps its own copy of the file (UploadedDocument) anyway.
///
/// POPIA: certified ID copies are personal information of directors. Access: the company's own users only. An organisation
/// sees a compliance document only as the copy attached to a bid submitted to one of its tenders.
/// </summary>
public class ComplianceDocument
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ComplianceDocumentType Type { get; set; }

    /// <summary>The date on the document: issued, printed, certified or commissioned (a calendar date, South African time).</summary>
    public DateTime IssuedOn { get; set; }

    /// <summary>Last day the document is valid (a calendar date). NULL = it does not expire (e.g. CIPC registration certificate).</summary>
    public DateTime? ExpiresOn { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;

    public string UploadedByUserId { get; set; } = string.Empty;
    public ApplicationUser UploadedByUser { get; set; } = null!;
    public DateTime UploadedAtUtc { get; set; }

    /// <summary>When a newer document of the same type replaced it, or the supplier removed it. NULL = current.</summary>
    public DateTime? ArchivedAtUtc { get; set; }
}
