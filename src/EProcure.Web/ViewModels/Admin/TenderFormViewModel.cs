using System.ComponentModel.DataAnnotations;
using EProcure.Web.Domain.Enums;

namespace EProcure.Web.ViewModels.Admin;

/// <summary>
/// What the Create / Edit tender form posts. The Tender ENTITY is never bound directly (that would
/// let a crafted form set OrganisationId, Status or CreatedByUserId: "over-posting").
/// OrganisationId and the creating user are always taken from the signed-in user on the server.
/// </summary>
public class TenderFormViewModel
{
    [Required, StringLength(250)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Tender reference")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Category")]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(8000)]
    [Display(Name = "Scope of work")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Entered in South African time; converted to UTC on the server.</summary>
    [Required(ErrorMessage = "Enter the closing date and time.")]
    [Display(Name = "Closing date and time")]
    public DateTime? ClosingDateLocal { get; set; }

    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "The tender fee must be between R0 and R1 000 000.")]
    [Display(Name = "Tender fee")]
    public decimal TenderFee { get; set; }

    [Range(typeof(decimal), "0", "100000000000", ErrorMessage = "Enter a valid estimated value.")]
    [Display(Name = "Estimated contract value")]
    public decimal? EstimatedValue { get; set; }

    /// <summary>NULL = "No requirement". Otherwise the WORST level still allowed to apply.</summary>
    [Display(Name = "Minimum B-BBEE status level")]
    public BbbeeLevel? MinimumBbbeeLevel { get; set; }

    [Display(Name = "Preference point system")]
    public PreferencePointSystem PointSystem { get; set; } = PreferencePointSystem.EightyTwenty;

    /// <summary>Standard documents ticked in the Documents step.</summary>
    public List<string> SelectedDocuments { get; set; } = new();

    /// <summary>Extra requirements, one per line.</summary>
    [Display(Name = "Other required documents")]
    public string? OtherDocuments { get; set; }
}
