using EProcure.Web.ViewModels.Tenders;

namespace EProcure.Web.ViewModels.Supplier;

/// <summary>The supplier's home: the tender feed (design screen "sFeed").</summary>
public class SupplierDashboardViewModel
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>NULL until the supplier has created a company profile.</summary>
    public string? CompanyName { get; set; }

    /// <summary>Search text typed by the supplier (title, reference, organisation or category).</summary>
    public string? Query { get; set; }

    /// <summary>Selected category chip; NULL = "All".</summary>
    public string? Category { get; set; }

    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
    public IReadOnlyList<TenderCardViewModel> Tenders { get; set; } = Array.Empty<TenderCardViewModel>();
}
