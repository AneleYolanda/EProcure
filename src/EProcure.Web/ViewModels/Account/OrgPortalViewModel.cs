namespace EProcure.Web.ViewModels.Account;

/// <summary>
/// The organisation-branded admin splash and sign-in pages (design screens "sSplash" and "sLogin"
/// of the admin console). Branding here is cosmetic: the tenant is still decided by the signed
/// claim after sign-in, never by the URL.
/// </summary>
public class OrgPortalViewModel
{
    public string OrgCode { get; set; } = string.Empty;
    public string OrgName { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public string PrimaryColour { get; set; } = string.Empty;
    public string AccentColour { get; set; } = string.Empty;

    public int OpenTenders { get; set; }
    public int RegisteredSuppliers { get; set; }
    public int TendersThisYear { get; set; }

    /// <summary>Filled only in the Development environment.</summary>
    public IReadOnlyList<DemoAccount> DemoAccounts { get; set; } = Array.Empty<DemoAccount>();

    public LoginViewModel Form { get; set; } = new();

    public record DemoAccount(string Email, string Label, string Meta);
}
