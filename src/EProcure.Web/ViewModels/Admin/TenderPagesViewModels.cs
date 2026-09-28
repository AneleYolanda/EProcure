using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Web.ViewModels.Admin;

/// <summary>Tender register (design screen "sTenders").</summary>
public class TenderRegisterViewModel
{
    public string? Stage { get; set; }
    public string? Query { get; set; }
    public bool CanCreate { get; set; }
    public IReadOnlyDictionary<TenderStage, int> Counts { get; set; } = new Dictionary<TenderStage, int>();
    public IReadOnlyList<TenderRegisterRow> Rows { get; set; } = Array.Empty<TenderRegisterRow>();
}

public record TenderRegisterRow(
    int Id, string ReferenceNumber, string Title, TenderStage Stage,
    decimal? EstimatedValue, DateTime ClosingDateUtc, int Applications);

/// <summary>One tender's page: facts, requirements, actions and history.</summary>
public class TenderDetailsViewModel
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TenderStage Stage { get; set; }
    public DateTime ClosingDateUtc { get; set; }
    public decimal TenderFee { get; set; }
    public decimal? EstimatedValue { get; set; }
    public BbbeeLevel? MinimumBbbeeLevel { get; set; }
    public PreferencePointSystem PointSystem { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public int Applications { get; set; }
    public IReadOnlyList<string> Requirements { get; set; } = Array.Empty<string>();

    /// <summary>True for SCM Officers (OrgAdmin); BEC members (Evaluator) only view.</summary>
    public bool CanManage { get; set; }

    /// <summary>Checks shown before publishing; publishing is only offered when all pass.</summary>
    public IReadOnlyList<PublishCheck> PublishChecks { get; set; } = Array.Empty<PublishCheck>();
    public bool CanPublish => Stage == TenderStage.Draft && PublishChecks.All(c => c.Passed);

    public IReadOnlyList<HistoryItem> History { get; set; } = Array.Empty<HistoryItem>();

    public record PublishCheck(string Label, string Meta, bool Passed);
    public record HistoryItem(string What, string Who, DateTime WhenUtc);
}

/// <summary>Cancel confirmation page.</summary>
public class CancelTenderViewModel
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Give the reason for cancelling. It is kept in the audit trail.")]
    [System.ComponentModel.DataAnnotations.StringLength(1000, MinimumLength = 10, ErrorMessage = "The reason must be between 10 and 1000 characters.")]
    public string Reason { get; set; } = string.Empty;
}
