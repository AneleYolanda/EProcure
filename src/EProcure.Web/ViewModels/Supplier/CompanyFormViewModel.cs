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
