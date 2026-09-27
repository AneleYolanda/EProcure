namespace EProcure.Web.ViewModels.Admin;

public class AdminDashboardViewModel
{
    public string OrganisationName { get; set; } = string.Empty;
    public int OpenTenderCount { get; set; }
    public int SubmissionCount { get; set; }
    public int ClosingWithinSevenDaysCount { get; set; }
}
