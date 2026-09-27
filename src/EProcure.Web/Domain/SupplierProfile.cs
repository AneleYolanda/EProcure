namespace EProcure.Web.Domain;

/// <summary>
/// The PERSON acting for a supplier: links a supplier login (ApplicationUser) to the
/// legal entity it represents (Company). Kept separate from Company because the person
/// and the company are different things in law (and in POPIA): a company can later have
/// several people bidding for it, and a person's details change independently.
///
/// POPIA: personal information (contact number, job title). Access: that supplier only;
/// an organisation sees the contact person only on a submission made to its own tender.
/// </summary>
public class SupplierProfile
{
    public int Id { get; set; }

    /// <summary>FK to the login. Unique: one profile per user.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    /// <summary>NULL until the supplier has created their company profile.</summary>
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public string? JobTitle { get; set; }

    /// <summary>Used later for OTP (IOtpSender). Optional in the MVP.</summary>
    public string? ContactNumber { get; set; }

    /// <summary>When the supplier accepted the POPIA processing notice.</summary>
    public DateTime? PopiaConsentAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
