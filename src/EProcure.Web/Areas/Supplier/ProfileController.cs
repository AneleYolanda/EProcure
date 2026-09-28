using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Supplier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Supplier;

/// <summary>
/// The supplier's own profile and settings (design screen "sSettings"): who is signed in,
/// their company, the POPIA notice and sign out. Shows only the signed-in user's own data.
/// </summary>
[Area("Supplier")]
[Authorize(Policy = "SupplierOnly")]
[ServiceFilter(typeof(EnsurePhoneVerifiedFilter))]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EProcureDbContext _db;

    public ProfileController(UserManager<ApplicationUser> userManager, EProcureDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var profile = await _db.SupplierProfiles
            .AsNoTracking()
            .Include(p => p.Company)
            .SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var company = profile?.Company;
        return View(new SupplierProfileViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Cellphone = DisplayFormat.Cellphone(user.PhoneNumber),
            CompanyName = company?.Name,
            CompanyMeta = company is null
                ? null
                : $"{EligibilityRules.Describe(company.BbbeeLevel)} · {company.EnterpriseSize} · {company.Sector}",
            PopiaConsentAtUtc = profile?.PopiaConsentAtUtc
        });
    }
}
