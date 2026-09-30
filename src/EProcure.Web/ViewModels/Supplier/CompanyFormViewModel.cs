using System.ComponentModel.DataAnnotations;
using EProcure.Web.Domain.Enums;

namespace EProcure.Web.ViewModels.Supplier;

/// <summary>
/// The Add / Edit company form (design screen "sAddCo"). Only the fields the MVP needs are collected
/// (POPIA: minimum necessary). The company is always the signed-in supplier's own: there is no company
/// id in the form or the URL, so one supplier cannot edit another's company by changing a number.
/// </summary>
public class CompanyFormViewModel
{
    [Required(ErrorMessage = "Enter the registered company name.")]
    [StringLength(200)]
    [Display(Name = "Registered company name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the CIPC registration number.")]
    [RegularExpression(@"^\s*\d{4}/\d{6}/\d{2}\s*$", ErrorMessage = "Use the CIPC format, e.g. 2019/123456/07.")]
    [Display(Name = "CIPC registration number")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your SARS tax number or Tax Compliance Status PIN.")]
    [RegularExpression(@"^\s*[A-Za-z0-9]{10}\s*$", ErrorMessage = "Enter the 10-character SARS tax number or Tax Compliance Status PIN.")]
    [Display(Name = "SARS tax number or TCS PIN")]
    public string TaxPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the Central Supplier Database (CSD) number.")]
    [RegularExpression(@"^\s*[Mm][Aa][Aa][Aa]\d{7}\s*$", ErrorMessage = "Use the CSD format: MAAA followed by 7 digits, e.g. MAAA0123456.")]
    [Display(Name = "CSD supplier number")]
    public string CsdNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose the B-BBEE status level on your certificate or affidavit.")]
    [Display(Name = "B-BBEE level")]
    public BbbeeLevel? BbbeeLevel { get; set; }

    [Required(ErrorMessage = "Choose EME, QSE or Generic.")]
    [Display(Name = "Enterprise size")]
    public EnterpriseSize? EnterpriseSize { get; set; }

    [Required(ErrorMessage = "Choose the sector your company works in.")]
    [Display(Name = "Industry / sector")]
    public string Sector { get; set; } = string.Empty;
}

/// <summary>The track-record page: what is on the profile, and the form to add one more document.</summary>
public class TrackRecordPageViewModel
{
    /// <summary>Straight after registration ("Step 3 of 3", with "Finish" instead of "Back").</summary>
    public bool Welcome { get; set; }
    public IReadOnlyList<EProcure.Web.Domain.CompanyDocument> Documents { get; set; } = Array.Empty<EProcure.Web.Domain.CompanyDocument>();
    public TrackRecordFormViewModel Form { get; set; } = new();
}

/// <summary>One piece of past work (the PDF is posted separately as "file"). Validated in TrackRecordService.</summary>
public class TrackRecordFormViewModel
{
    public CompanyDocumentKind? Kind { get; set; }
    public string? Title { get; set; }
    public string? ClientName { get; set; }
    public int? YearCompleted { get; set; }
    public decimal? ContractValue { get; set; }
}

/// <summary>The compliance documents page: every document type with the current document (if any) and the upload form.</summary>
public class CompliancePageViewModel
{
    /// <summary>Straight after registration ("Step 3 of 4", continuing to the track record).</summary>
    public bool Welcome { get; set; }
    public DateTime TodaySast { get; set; }
    public IReadOnlyList<EProcure.Web.Domain.ComplianceDocument> Current { get; set; } = Array.Empty<EProcure.Web.Domain.ComplianceDocument>();
    public ComplianceFormViewModel Form { get; set; } = new();
}

/// <summary>One compliance document as posted (the PDF comes separately as "file"). Validated in ComplianceService.</summary>
public class ComplianceFormViewModel
{
    public ComplianceDocumentType? Type { get; set; }
    public DateTime? IssuedOn { get; set; }
    public DateTime? ExpiresOn { get; set; }
}
