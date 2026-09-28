namespace EProcure.Web.Tenancy;

/// <summary>
/// The tenant context for scheduled system jobs (e.g. closing-date reminders), which work across every organisation
/// and act for no signed-in user. It is used ONLY to build the job's own DbContext, never for a web request, so a job
/// started while someone is signed in still sees all organisations, and no request ever sees more than its own.
/// </summary>
public sealed class SystemTenantContext : ITenantContext
{
    public static readonly SystemTenantContext Instance = new();

    private SystemTenantContext() { }

    public bool IsOrganisationScoped => false;
    public int? OrganisationId => null;
}
