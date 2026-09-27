using EProcure.Web.Data;
using EProcure.Web.Domain;

namespace EProcure.Web.Services;

/// <summary>
/// Writes an audit row using the current authenticated identity and request IP address.
/// The service deliberately copies the email because audit records must remain useful
/// even if an account is later renamed or removed.
/// </summary>
public class AuditService : IAuditService
{
    private readonly EProcureDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(EProcureDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string entityId,
        int? organisationId,
        string? details = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;

        _db.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            UserEmail = user?.Identity?.IsAuthenticated == true ? user.Identity.Name : null,
            OrganisationId = organisationId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString()
        });

        await _db.SaveChangesAsync();
    }
}
