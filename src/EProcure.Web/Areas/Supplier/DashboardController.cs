using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Supplier;
using EProcure.Web.ViewModels.Tenders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Supplier;

/// <summary>
/// The supplier's home screen: open tenders from EVERY organisation (the shared marketplace),
/// each labelled with whether the supplier's company meets the B-BBEE minimum.
/// </summary>
[Area("Supplier")]
[Authorize(Policy = "SupplierOnly")]
[ServiceFilter(typeof(EnsurePhoneVerifiedFilter))]
public class DashboardController : Controller
{
    private const int MaxCategoryChips = 4;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EProcureDbContext _db;

    public DashboardController(UserManager<ApplicationUser> userManager, EProcureDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<IActionResult> Index(string? q, string? category, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var company = await _db.SupplierProfiles
            .AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .Select(p => p.Company)
            .SingleOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // Suppliers are not organisation-scoped, so this sees published tenders from all organisations.
        var open = _db.Tenders.AsNoTracking()
            .Where(t => t.Status == TenderStatus.Published && t.ClosingDateUtc > now);

        var categories = await open
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .Take(MaxCategoryChips)
            .ToListAsync(cancellationToken);

        var query = open;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t =>
                t.Title.Contains(term) ||
                t.ReferenceNumber.Contains(term) ||
                t.Category.Contains(term) ||
                t.Organisation.Name.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(t => t.Category == category);
        }

        var rows = await query
            .OrderBy(t => t.ClosingDateUtc)
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.Title,
                OrganisationName = t.Organisation.Name,
                t.Category,
                t.EstimatedValue,
                t.TenderFee,
                t.ClosingDateUtc,
                t.MinimumBbbeeLevel
            })
            .ToListAsync(cancellationToken);

        // The eligibility rule runs in C# (enums are stored as text, so SQL cannot compare levels).
        var cards = rows.Select(r => new TenderCardViewModel
        {
            Id = r.Id,
            ReferenceNumber = r.ReferenceNumber,
            Title = r.Title,
            OrganisationName = r.OrganisationName,
            Category = r.Category,
            EstimatedValue = r.EstimatedValue,
            TenderFee = r.TenderFee,
            ClosingDateUtc = r.ClosingDateUtc,
            Eligibility = EligibilityRules.CheckBbbee(company?.BbbeeLevel, r.MinimumBbbeeLevel)
        }).ToList();

        return View(new SupplierDashboardViewModel
        {
            FullName = user.FullName,
            CompanyName = company?.Name,
            Query = q,
            Category = category,
            Categories = categories,
            Tenders = cards
        });
    }
}
