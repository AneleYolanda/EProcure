using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.ViewModels.Supplier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Supplier;

[Area("Supplier")]
[Authorize(Policy = "SupplierOnly")]
[ServiceFilter(typeof(EnsurePhoneVerifiedFilter))]
public class DashboardController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EProcureDbContext _db;

    public DashboardController(UserManager<ApplicationUser> userManager, EProcureDbContext db)
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
            .SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        return View(new SupplierDashboardViewModel
        {
            FullName = user.FullName,
            HasCompanyProfile = profile?.CompanyId.HasValue == true
        });
    }
}
