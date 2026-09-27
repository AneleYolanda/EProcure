using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Services;

/// <summary>
/// Resolves organisation branding from the signed-in tenant claim.
/// The result is cached in the request scope so multiple layout reads do not repeat the query.
/// </summary>
public class CurrentOrganisation : ICurrentOrganisation
{
    private readonly EProcureDbContext _db;
    private readonly ITenantContext _tenant;
    private Organisation? _organisation;
    private bool _loaded;

    public CurrentOrganisation(EProcureDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Organisation?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded) return _organisation;

        _loaded = true;
        if (!_tenant.IsOrganisationScoped || !_tenant.OrganisationId.HasValue)
        {
            return null;
        }

        _organisation = await _db.Organisations
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == _tenant.OrganisationId.Value, cancellationToken);

        return _organisation;
    }
}
