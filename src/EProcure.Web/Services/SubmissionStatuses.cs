using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Services;

/// <summary>How an application's status is shown (design STATUS colours) and which changes staff may make.</summary>
public static class SubmissionStatuses
{
    public static string Label(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft => "Draft",
        SubmissionStatus.AwaitingPayment => "Fee unpaid",
        SubmissionStatus.Submitted => "Submitted",
        SubmissionStatus.UnderEvaluation => "Under evaluation",
        SubmissionStatus.Unsuccessful => "Not awarded",
        SubmissionStatus.Awarded => "Awarded",
        _ => "Withdrawn"
    };

    public static string BadgeCss(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft or SubmissionStatus.AwaitingPayment => "ep-badge ep-badge--warning",
        SubmissionStatus.Submitted => "ep-badge ep-badge--info",
        SubmissionStatus.UnderEvaluation => "ep-badge ep-badge--navy",
        SubmissionStatus.Awarded => "ep-badge ep-badge--success",
        _ => "ep-badge"
    };

    // Statuses after submission are set only by the evaluation workflow (EvaluationService): Under evaluation when
    // the BEC opens a bid, Awarded / Not awarded when the BAC's decision is recorded. There is no manual change.

    /// <summary>Answers the evaluation committee should look at closely. Recorded and flagged, never auto-rejected.</summary>
    public static IReadOnlyList<string> RedFlags(Submission s)
    {
        var flags = new List<string>();
        if (s.IsCsdRegistered == false) flags.Add("Not registered on the CSD");
        if (s.IsTaxCompliant == false) flags.Add("Not tax compliant");
        if (s.HasDeclaredInterest == true) flags.Add("Declared an interest (SBD 4)");
        if (s.ConfirmsNotRestricted == false) flags.Add("On a restricted list (SBD 8)");
        if (s.ConfirmsIndependentBid == false) flags.Add("Cannot confirm independent bid (SBD 9)");
        return flags;
    }
}
