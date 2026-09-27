namespace EProcure.Web.ViewModels.Home;

public class HomeViewModel
{
    public IReadOnlyList<OpenTenderCardViewModel> LatestTenders { get; init; } = Array.Empty<OpenTenderCardViewModel>();
}

public class OpenTenderCardViewModel
{
    public int Id { get; init; }
    public string OrganisationName { get; init; } = string.Empty;
    public string? OrganisationLogoPath { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public DateTime ClosingDateUtc { get; init; }
    public decimal TenderFee { get; init; }
}
