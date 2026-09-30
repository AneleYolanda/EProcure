using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Web.ViewModels.Supplier;

/// <summary>A tender as a supplier sees it (design screen "sDetail").</summary>
public class SupplierTenderViewModel
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public string OrganisationCode { get; set; } = string.Empty;
    public string? OrganisationLogoPath { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime ClosingDateUtc { get; set; }
    public decimal TenderFee { get; set; }
    public decimal? EstimatedValue { get; set; }
    public PreferencePointSystem PointSystem { get; set; }
    public BbbeeLevel? MinimumBbbeeLevel { get; set; }
    public int? FunctionalityThreshold { get; set; }
    public IReadOnlyList<(string Name, int Weight)> FunctionalityCriteria { get; set; } = Array.Empty<(string, int)>();

    /// <summary>How many track-record documents the supplier's company has on its profile (NULL: no company yet).</summary>
    public int? TrackRecordCount { get; set; }
    public IReadOnlyList<string> Requirements { get; set; } = Array.Empty<string>();
    public bool IsOpen { get; set; }

    public string? CompanyName { get; set; }
    public BbbeeLevel? CompanyLevel { get; set; }
    public string? CompanyCsd { get; set; }
    public EligibilityResult Eligibility { get; set; } = new(EligibilityOutcome.Unknown, string.Empty);

    /// <summary>The supplier's existing application for this tender, if any.</summary>
    public int? ApplicationId { get; set; }
    public SubmissionStatus? ApplicationStatus { get; set; }

    /// <summary>The published award notice (NULL until the tender is awarded).</summary>
    public AwardNotice? Award { get; set; }

    public record AwardNotice(string CompanyName, decimal? Amount, DateTime DecisionDateUtc, decimal? TotalPoints, BbbeeLevel Level);
}

/// <summary>One step of the application wizard (design screen "sApply", steps 1 to 5).</summary>
public class ApplyStepViewModel
{
    public const int StepCount = 5;
    public static readonly string[] StepTitles = { "", "Bidding as", "Compliance checks", "Declarations", "Required documents", "Review and declare" };

    public int SubmissionId { get; set; }
    public int Step { get; set; }
    public int TenderId { get; set; }
    public string TenderReference { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public decimal TenderFee { get; set; }
    public DateTime ClosingDateUtc { get; set; }

    // Step 1
    public string CompanyName { get; set; } = string.Empty;
    public IReadOnlyList<(string Key, string Value)> CompanyFields { get; set; } = Array.Empty<(string, string)>();

    // Step 2 and 3 answers (NULL = not answered yet)
    public bool? CsdRegistered { get; set; }
    public bool? TaxCompliant { get; set; }
    public bool? HasInterest { get; set; }
    public string? InterestDetails { get; set; }
    public bool? OnRestrictedList { get; set; }
    public string? RestrictionDetails { get; set; }
    public bool? IndependentBid { get; set; }

    // Step 4
    public IReadOnlyList<ChecklistItem> Checklist { get; set; } = Array.Empty<ChecklistItem>();
    public long MaxFileSizeBytes { get; set; }

    // Step 5
    public IReadOnlyList<(string Key, string Value)> ReviewRows { get; set; } = Array.Empty<(string, string)>();
    public IReadOnlyList<string> Missing { get; set; } = Array.Empty<string>();
    public string EligibilityText { get; set; } = string.Empty;

    public record ChecklistItem(int RequirementId, string Name, bool Mandatory, int? DocumentId, string? FileName, long SizeBytes, DateTime? UploadedAtUtc);

    /// <summary>Checklist item id -> valid compliance documents on the profile that can be attached instead of uploading.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<ProfileOption>> ProfileOptions { get; set; } = new Dictionary<int, IReadOnlyList<ProfileOption>>();

    /// <summary>ExpiresBeforeClosing: still valid now, but not on the closing date (the supplier is warned).</summary>
    public record ProfileOption(int DocumentId, string Label, string Validity, bool ExpiresBeforeClosing);
}

/// <summary>Tender-fee payment page (design screen "sPay").</summary>
public class PayViewModel
{
    public int SubmissionId { get; set; }
    public string TenderReference { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ClosingDateUtc { get; set; }
    public bool LastAttemptFailed { get; set; }
}

/// <summary>After submission (design screen "sConfirm").</summary>
public class ConfirmationViewModel
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public string TenderReference { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public bool FeePaid { get; set; }
    public int DocumentCount { get; set; }
}

/// <summary>The eligibility hard stop (design screen "sGate").</summary>
public class GateViewModel
{
    public string TenderReference { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int QualifyingCount { get; set; }
}
