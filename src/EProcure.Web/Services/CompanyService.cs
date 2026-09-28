using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.ViewModels.Supplier;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Services;

public interface ICompanyService
{
    /// <summary>The signed-in supplier's company, or NULL if they have not added one yet.</summary>
    Task<Company?> GetForUserAsync(string userId, CancellationToken ct);

    /// <summary>True if the company already has applications (its legal identity is then locked).</summary>
    Task<bool> HasSubmissionsAsync(int companyId, CancellationToken ct);

    /// <summary>Creates the supplier's company, or updates it if they already have one.</summary>
    Task<ServiceResult> SaveAsync(string userId, CompanyFormViewModel form, CancellationToken ct);
}

/// <summary>
/// Rules for a supplier's company (journey step 3).
/// Ownership: the company is always found through the signed-in user's own SupplierProfile, never by an id
/// from the browser, so there is nothing to tamper with. Suppliers are not organisation-scoped; no tenant
/// filter applies to companies, which is correct: a company may bid to every organisation.
/// </summary>
public class CompanyService : ICompanyService
{
    private readonly EProcureDbContext _db;
    private readonly IAuditService _audit;

    public CompanyService(EProcureDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<Company?> GetForUserAsync(string userId, CancellationToken ct) =>
        _db.SupplierProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Company)
            .SingleOrDefaultAsync(ct);

    public Task<bool> HasSubmissionsAsync(int companyId, CancellationToken ct) =>
        _db.Submissions.AnyAsync(s => s.CompanyId == companyId, ct);

    public async Task<ServiceResult> SaveAsync(string userId, CompanyFormViewModel form, CancellationToken ct)
    {
        // Normalise so the same number is always stored the same way (and uniqueness really works).
        var registration = form.RegistrationNumber.Trim();
        var csd = form.CsdNumber.Trim().ToUpperInvariant();
        var taxPin = form.TaxPin.Trim().ToUpperInvariant();

        var profile = await _db.SupplierProfiles.Include(p => p.Company).SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            // Every supplier gets a profile at registration; this only covers older accounts.
            profile = new SupplierProfile { UserId = userId, CreatedAtUtc = DateTime.UtcNow };
            _db.SupplierProfiles.Add(profile);
        }

        var company = profile.Company;
        var ownId = company?.Id ?? 0;
        var result = new ServiceResult();

        // Once a company has applied for tenders, its legal identity must not change underneath those bids.
        if (company is not null && await HasSubmissionsAsync(company.Id, ct)
            && (company.RegistrationNumber != registration || company.CsdNumber != csd))
        {
            result.With(nameof(form.RegistrationNumber),
                "The CIPC and CSD numbers are locked because this company has applications. Contact support to correct them.");
        }

        // One legal entity is registered once. Checked here for a friendly message; the unique indexes
        // on Companies are the real guarantee (see TrySaveAsync).
        if (await _db.Companies.AnyAsync(c => c.RegistrationNumber == registration && c.Id != ownId, ct))
            result.With(nameof(form.RegistrationNumber), AlreadyRegistered);
        if (await _db.Companies.AnyAsync(c => c.CsdNumber == csd && c.Id != ownId, ct))
            result.With(nameof(form.CsdNumber), AlreadyRegistered);
        if (!TenderCatalog.Categories.Contains(form.Sector))
            result.With(nameof(form.Sector), "Choose a sector from the list.");
        if (!result.Succeeded) return result;

        var isNew = company is null;
        if (company is null)
        {
            company = new Company { CreatedAtUtc = DateTime.UtcNow };
            _db.Companies.Add(company);
            profile.Company = company;
        }

        company.Name = form.Name.Trim();
        company.RegistrationNumber = registration;
        company.TaxPin = taxPin;
        company.CsdNumber = csd;
        company.BbbeeLevel = form.BbbeeLevel!.Value;
        company.EnterpriseSize = form.EnterpriseSize!.Value;
        company.Sector = form.Sector;

        if (!await TrySaveAsync(result, ct)) return result;

        // Suppliers belong to no organisation, so OrganisationId is NULL on these audit rows.
        await _audit.LogAsync(isNew ? "Company.Created" : "Company.Updated", "Company", company.Id.ToString(), null, company.Name);
        return ServiceResult.Ok(company.Id);
    }

    private const string AlreadyRegistered =
        "This company is already registered on eProcure. If you work for it, ask the person who registered it for access.";

    private async Task<bool> TrySaveAsync(ServiceResult result, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Companies_") == true)
        {
            var field = ex.InnerException.Message.Contains("CsdNumber") ? nameof(CompanyFormViewModel.CsdNumber) : nameof(CompanyFormViewModel.RegistrationNumber);
            result.With(field, AlreadyRegistered);
            return false;
        }
    }
}
