using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Domain;

/// <summary>
/// THE TENANT. One row per buying organisation (RBIDZ, a municipality, an SOE).
/// Admin users and tenders carry an OrganisationId pointing here; that column is
/// what the tenant filter in EProcureDbContext uses to keep organisations apart.
/// POPIA: holds organisational data only, no personal information.
/// </summary>
public class Organisation
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Short unique code, e.g. "RBIDZ". Used in URLs and file names.</summary>
    public string Code { get; set; } = string.Empty;

    // ---- Branding (drives the admin workspace look) ----
    /// <summary>Path under wwwroot, e.g. "/img/orgs/rbidz.svg".</summary>
    public string? LogoPath { get; set; }
    /// <summary>Hex colour, e.g. "#0B2545".</summary>
    public string PrimaryColour { get; set; } = "#0B2545";
    public string AccentColour { get; set; } = "#C9A227";

    // ---- Procurement configuration ----
    /// <summary>Default point system pre-selected on this organisation's new tenders.</summary>
    public PreferencePointSystem DefaultPointSystem { get; set; } = PreferencePointSystem.EightyTwenty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, a tender can only be published after a SECOND SCM Officer approves it (four-eyes control).
    /// Set by the organisation's SCM Officers on the Approval queue page.
    /// </summary>
    public bool RequireTenderApproval { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // Navigation properties (one organisation -> many of each)
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
}
