using EProcure.Web.Domain;

namespace EProcure.Web.Tenancy;

/// <summary>
/// Works out the tenant from the signed-in user's claims (the authentication cookie,
/// which is signed and encrypted by the server, so the browser cannot forge it).
/// The OrganisationId is NEVER taken from the URL, a form field or a query string.
/// </summary>
public class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsOrganisationScoped
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return false;
            return AppRoles.OrganisationScoped.Any(user.IsInRole);
        }
    }

    public int? OrganisationId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirst(TenantClaimTypes.OrganisationId)?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }
    }
}
