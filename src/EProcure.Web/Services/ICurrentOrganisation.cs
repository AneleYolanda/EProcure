using EProcure.Web.Domain;

namespace EProcure.Web.Services;

/// <summary>
/// Loads the current admin tenant's branding once per request.
/// </summary>
public interface ICurrentOrganisation
{
    Task<Organisation?> GetAsync(CancellationToken cancellationToken = default);
}
