using System.ComponentModel.DataAnnotations;
using EProcure.Web.Domain.Enums;

namespace EProcure.Web.ViewModels.Admin;

/// <summary>A row in the organisation's list of received applications.</summary>
public record ReceivedApplicationRow(int Id, string? ReferenceNumber, string CompanyName, BbbeeLevel DeclaredLevel,
    string TenderReference, string TenderTitle, SubmissionStatus Status, DateTime? SubmittedAtUtc, int FlagCount, int DocumentCount);

public class ReceivedApplicationsViewModel
{
    public int? TenderId { get; set; }
    public string? TenderReference { get; set; }
    public IReadOnlyList<ReceivedApplicationRow> Rows { get; set; } = Array.Empty<ReceivedApplicationRow>();
}

/// <summary>One received application, as the organisation's staff see it.</summary>
public class ReceivedApplicationViewModel
{
    public int Id { get; set; }
    public string? ReferenceNumber { get; set; }
    public int TenderId { get; set; }
    public string TenderReference { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public SubmissionStatus Status { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public string PaymentText { get; set; } = string.Empty;

    // The bidding company (juristic-person data; shown only because it applied to this organisation's tender).
    public string CompanyName { get; set; } = string.Empty;
    public IReadOnlyList<(string Key, string Value)> CompanyFields { get; set; } = Array.Empty<(string, string)>();
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    public IReadOnlyList<(string Question, string Answer, bool Flag)> Answers { get; set; } = Array.Empty<(string, string, bool)>();
    public IReadOnlyList<string> RedFlags { get; set; } = Array.Empty<string>();
    public IReadOnlyList<DocumentRow> Documents { get; set; } = Array.Empty<DocumentRow>();
    public IReadOnlyList<(string What, string Who, DateTime WhenUtc)> Timeline { get; set; } = Array.Empty<(string, string, DateTime)>();

    public bool CanChangeStatus { get; set; }
    public IReadOnlyList<SubmissionStatus> AllowedNext { get; set; } = Array.Empty<SubmissionStatus>();

    public record DocumentRow(int Id, string Requirement, string FileName, long SizeBytes, string Sha256Short, DateTime UploadedAtUtc);
}

/// <summary>Status change form (SCM Officers only).</summary>
public class ChangeStatusForm
{
    [Required]
    public SubmissionStatus? NewStatus { get; set; }

    [Required(ErrorMessage = "Give a note. The supplier sees it on their application.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "The note must be between 5 and 1000 characters.")]
    public string Note { get; set; } = string.Empty;
}

public record AuditRow(DateTime OccurredAtUtc, string Action, string EntityType, string EntityId, string Who, string? Details, string? IpAddress);

public class AuditPageViewModel
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<AuditRow> Rows { get; set; } = Array.Empty<AuditRow>();
}
