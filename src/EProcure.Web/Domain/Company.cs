using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// The supplier's LEGAL ENTITY: the thing that actually bids and can be awarded a contract.
/// Not tenant-scoped: a company can bid on tenders from any organisation.
///
/// POPIA: mostly juristic-person information (which POPIA also protects). Access: the
/// supplier's own users; an organisation sees it only through a submission to its own tender.
/// </summary>
public class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>CIPC registration number, e.g. 2019/123456/07. Unique.</summary>
    public string RegistrationNumber { get; set; } = string.Empty;

    /// <summary>SARS tax reference / Tax Compliance Status PIN.</summary>
    public string TaxPin { get; set; } = string.Empty;

    /// <summary>National Treasury Central Supplier Database number, e.g. MAAA0123456.</summary>
    public string CsdNumber { get; set; } = string.Empty;

    public BbbeeLevel BbbeeLevel { get; set; } = BbbeeLevel.NonCompliant;

    /// <summary>
    /// Enterprise size for B-BBEE purposes and tender eligibility (badge on supplier profile).
    /// </summary>
    public EnterpriseSize EnterpriseSize { get; set; } = EnterpriseSize.Generic;

    public string Sector { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<SupplierProfile> Profiles { get; set; } = new List<SupplierProfile>();
    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}
