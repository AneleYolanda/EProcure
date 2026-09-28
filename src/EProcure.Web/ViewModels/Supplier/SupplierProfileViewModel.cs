namespace EProcure.Web.ViewModels.Supplier;

/// <summary>The supplier's profile and settings screen (design screen "sSettings").</summary>
public class SupplierProfileViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cellphone { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? CompanyMeta { get; set; }
    public DateTime? PopiaConsentAtUtc { get; set; }
}
