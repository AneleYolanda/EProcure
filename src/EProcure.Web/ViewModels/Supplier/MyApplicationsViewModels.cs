using EProcure.Web.Domain.Enums;

namespace EProcure.Web.ViewModels.Supplier;

/// <summary>One row of "My applications" (design screen "sApps").</summary>
public record MyApplicationRow(int Id, string TenderReference, string TenderTitle, string OrganisationName,
    string CompanyName, SubmissionStatus Status, DateTime CreatedAtUtc, DateTime? SubmittedAtUtc);

/// <summary>One application as its supplier sees it (design screen "sAppDetail").</summary>
public class MyApplicationDetailsViewModel
{
    public int Id { get; set; }
    public string? ReferenceNumber { get; set; }
    public int TenderId { get; set; }
    public string TenderReference { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public SubmissionStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public decimal AmountPaid { get; set; }
    public string? PaymentReference { get; set; }
    public IReadOnlyList<(string Question, string Answer)> Answers { get; set; } = Array.Empty<(string, string)>();
    public IReadOnlyList<DocumentRow> Documents { get; set; } = Array.Empty<DocumentRow>();
    public IReadOnlyList<TimelineItem> Timeline { get; set; } = Array.Empty<TimelineItem>();

    public record DocumentRow(int Id, string Requirement, string FileName, long SizeBytes, DateTime UploadedAtUtc);
    public record TimelineItem(string What, string Who, DateTime WhenUtc);
}
