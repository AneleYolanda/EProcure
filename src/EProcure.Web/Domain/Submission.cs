using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// A company's application (bid) to one tender. TENANT-SCOPED THROUGH ITS TENDER:
/// the owning organisation is Tender.OrganisationId. We deliberately do NOT copy
/// OrganisationId onto this table, so the two can never disagree.
///
/// Unique (TenderId, CompanyId): a company can apply to a tender only once.
///
/// POPIA: links a company and its contact person to a tender. Access: the supplier who
/// owns it; OrgAdmins/Evaluators of the tender's organisation, and only once Status >= Submitted
/// (i.e. after successful payment). No payment card data is ever stored; only the gateway's
/// reference string.
/// </summary>
public class Submission
{
    public int Id { get; set; }

    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;

    /// <summary>
    /// Application reference number in format EP-{yyyy}-{000000}. Unique, assigned on payment success.
    /// </summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>The bidding legal entity.</summary>
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>The supplier user who made the submission.</summary>
    public string SubmittedByUserId { get; set; } = string.Empty;
    public ApplicationUser SubmittedByUser { get; set; } = null!;

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;

    // ---- Standard Bidding Document (SBD) based declarations ----
    /// <summary>Registered on the National Treasury Central Supplier Database.</summary>
    public bool IsCsdRegistered { get; set; }
    /// <summary>SARS tax compliance status is compliant.</summary>
    public bool IsTaxCompliant { get; set; }
    /// <summary>B-BBEE level the bidder claims on THIS bid (copied from Company at submission time).</summary>
    public BbbeeLevel DeclaredBbbeeLevel { get; set; }
    /// <summary>SBD 4: bidder has a relationship with persons in the service of the state / the organisation (must be declared).</summary>
    public bool HasDeclaredInterest { get; set; }
    /// <summary>SBD 8 (past supply chain practices): NOT listed on the Database of Restricted Suppliers / Register for Tender Defaulters.</summary>
    public bool ConfirmsNotRestricted { get; set; }
    /// <summary>SBD 9: certificate of independent bid determination (no collusion).</summary>
    public bool ConfirmsIndependentBid { get; set; }

    // ---- Tender fee payment (reference only; never card data) ----
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public decimal AmountPaid { get; set; }
    /// <summary>Transaction id returned by IPaymentGateway (mock now, PayFast/Ozow later).</summary>
    public string? PaymentReference { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    /// <summary>When it became visible to the organisation (payment succeeded).</summary>
    public DateTime? SubmittedAtUtc { get; set; }

    public ICollection<UploadedDocument> Documents { get; set; } = new List<UploadedDocument>();
    public ICollection<SubmissionStatusHistory> StatusHistory { get; set; } = new List<SubmissionStatusHistory>();
}
