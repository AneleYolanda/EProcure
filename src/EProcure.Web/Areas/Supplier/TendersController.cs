using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Supplier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Supplier;

/// <summary>
/// A tender as a supplier sees it (journey step 4; design screen "sDetail"): who posted it, whether the
/// supplier's company qualifies, the checklist, and the way into the application.
/// Only PUBLISHED tenders exist for suppliers; drafts and cancelled tenders return 404.
/// </summary>
[Area("Supplier")]
[Authorize(Policy = "SupplierOnly")]
[ServiceFilter(typeof(EnsurePhoneVerifiedFilter))]
public class TendersController : Controller
{
    private readonly EProcureDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TendersController(EProcureDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // GET /Supplier/Tenders/Details/5
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.AsNoTracking()
            .Include(t => t.Organisation)
            .Include(t => t.Requirements)
            .SingleOrDefaultAsync(t => t.Id == id && t.Status == TenderStatus.Published, ct);
        if (tender is null) return NotFound();

        var userId = _userManager.GetUserId(User)!;
        var company = await _db.SupplierProfiles.AsNoTracking()
            .Where(p => p.UserId == userId).Select(p => p.Company).SingleOrDefaultAsync(ct);
        var application = company is null ? null : await _db.Submissions.AsNoTracking()
            .Where(s => s.TenderId == id && s.CompanyId == company.Id)
            .Select(s => new { s.Id, s.Status })
            .SingleOrDefaultAsync(ct);

        return View(new SupplierTenderViewModel
        {
            Id = tender.Id,
            ReferenceNumber = tender.ReferenceNumber,
            Title = tender.Title,
            OrganisationName = tender.Organisation.Name,
            OrganisationCode = tender.Organisation.Code,
            OrganisationLogoPath = tender.Organisation.LogoPath,
            Category = tender.Category,
            Description = tender.Description,
            ClosingDateUtc = tender.ClosingDateUtc,
            TenderFee = tender.TenderFee,
            EstimatedValue = tender.EstimatedValue,
            PointSystem = tender.PointSystem,
            MinimumBbbeeLevel = tender.MinimumBbbeeLevel,
            Requirements = tender.Requirements.OrderBy(r => r.SortOrder).Select(r => r.Name).ToList(),
            IsOpen = tender.ClosingDateUtc > DateTime.UtcNow,
            CompanyName = company?.Name,
            CompanyLevel = company?.BbbeeLevel,
            CompanyCsd = company?.CsdNumber,
            Eligibility = EligibilityRules.CheckBbbee(company?.BbbeeLevel, tender.MinimumBbbeeLevel),
            ApplicationId = application?.Id,
            ApplicationStatus = application?.Status
        });
    }
}
