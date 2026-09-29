using System.ComponentModel.DataAnnotations;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Web.ViewModels.Admin;

/// <summary>"BEC scoring" and "BAC adjudication" lists.</summary>
public class EvaluationListViewModel
{
    public string View { get; set; } = "all"; // all | scoring | adjudication
    public IReadOnlyList<EvaluationListRow> Rows { get; set; } = Array.Empty<EvaluationListRow>();
}

/// <summary>A tender's scoresheet page, with the forms the signed-in role may use.</summary>
public class ScoresheetPageViewModel
{
    public Scoresheet Sheet { get; set; } = null!;
    public bool IsBecMember { get; set; }
    public bool IsScmOfficer { get; set; }
    public RecommendForm Recommend { get; set; } = new();
    public AwardForm Award { get; set; } = new();
    public ReturnForm Return { get; set; } = new();
}

public class RecommendForm
{
    public int? RecommendedSubmissionId { get; set; }
    public string? Reason { get; set; }
}

public class AwardForm
{
    public int? SubmissionId { get; set; }
    public string? CommitteeReference { get; set; }
    [DataType(DataType.Date)]
    public DateTime? DecisionDateLocal { get; set; }
    public string? Rationale { get; set; }
}

public class ReturnForm
{
    public string? ReturnReason { get; set; }
}

/// <summary>The BEC's evaluation page for one bid.</summary>
public class EvaluateBidViewModel
{
    public int SubmissionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public int TenderId { get; set; }
    public string TenderReference { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public PreferencePointSystem PointSystem { get; set; }
    public EvaluationStage Stage { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public BbbeeLevel DeclaredLevel { get; set; }
    public IReadOnlyList<string> RedFlags { get; set; } = Array.Empty<string>();
    public IReadOnlyList<(int Id, string Requirement, string FileName)> Documents { get; set; } = Array.Empty<(int, string, string)>();

    /// <summary>The bidder's track record as held at the closing date (empty while sealed).</summary>
    public IReadOnlyList<TrackRecordRow> TrackRecord { get; set; } = Array.Empty<TrackRecordRow>();

    /// <summary>Functionality stage: NULL threshold = none. Criteria in the tender's order.</summary>
    public int? FunctionalityThreshold { get; set; }
    public IReadOnlyList<(int Id, string Name, int Weight)> Criteria { get; set; } = Array.Empty<(int, string, int)>();
    public decimal? FunctionalityScore { get; set; }

    public record TrackRecordRow(int Id, string Title, string Kind, string Details, string FileName, DateTime UploadedAtUtc);
    public string? EvaluatedBy { get; set; }
    public DateTime? EvaluatedAtUtc { get; set; }
    public bool CanEdit { get; set; }
    public EvaluateBidForm Form { get; set; } = new();
}

public class EvaluateBidForm
{
    public bool? IsResponsive { get; set; }

    /// <summary>Criterion id -> rating 0-5 (tenders with a functionality stage only).</summary>
    public Dictionary<int, int?> Ratings { get; set; } = new();
    public string? NonResponsiveReason { get; set; }
    public decimal? BidPrice { get; set; }
    public string? Notes { get; set; }
}
