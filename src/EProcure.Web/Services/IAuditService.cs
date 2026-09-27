namespace EProcure.Web.Services;

/// <summary>
/// Records security and procurement actions in the append-only audit log.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityType,
        string entityId,
        int? organisationId,
        string? details = null);
}
