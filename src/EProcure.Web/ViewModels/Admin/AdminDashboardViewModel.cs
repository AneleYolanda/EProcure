namespace EProcure.Web.ViewModels.Admin;

/// <summary>Admin console dashboard (design screen "sDash"). All numbers are for ONE organisation.</summary>
public class AdminDashboardViewModel
{
    public string OrganisationCode { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;

    public int OpenTenderCount { get; set; }
    public int ClosingWithinSevenDaysCount { get; set; }
    public int SubmissionCount { get; set; }
    public int DraftCount { get; set; }
    public int ClosedCount { get; set; }
    public int AuditEntriesLastSevenDays { get; set; }

    public IReadOnlyList<ActionItem> ActionItems { get; set; } = Array.Empty<ActionItem>();
    public IReadOnlyList<PipelineStage> Pipeline { get; set; } = Array.Empty<PipelineStage>();

    /// <param name="Tone">"neutral", "warning" or "info": picks the icon and tag colours.</param>
    /// <param name="Icon">"file", "clock" or "inbox".</param>
    public record ActionItem(string Title, string Meta, string Tag, string Tone, string Icon);

    /// <param name="Colour">A fixed design colour for the bar, e.g. "#B9CBDA".</param>
    public record PipelineStage(string Label, int Count, int Percent, string Colour);
}
