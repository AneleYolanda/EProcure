using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Controllers;

/// <summary>
/// Each organisation's own entrance to the admin console: /admin/{orgCode} (splash) and
/// /admin/{orgCode}/login (sign in), branded with that organisation's logo and colours.
///
/// Security: the URL only decides the LOOK of these two pages. After signing in, the user's
/// organisation still comes from their account (the signed cookie claim), and a user may only
/// sign in at their own organisation's address; anyone else gets the same generic error as a
/// wrong password, so the page does not reveal which organisation an email belongs to.
///
/// Order = 100 makes these routes lower priority than the normal "{area}/{controller}" route,
/// so /Admin/Dashboard still reaches the dashboard and is never mistaken for an org code.
/// </summary>
[AllowAnonymous]
public class OrgPortalController : Controller
{
    private readonly EProcureDbContext _db;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _environment;

    public OrgPortalController(
        EProcureDbContext db,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService audit,
        IWebHostEnvironment environment)
    {
        _db = db;
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
        _environment = environment;
    }

    [HttpGet("admin/{orgCode}", Order = 100)]
    public async Task<IActionResult> Splash(string orgCode, CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(orgCode, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet("admin/{orgCode}/login", Order = 100)]
    public async Task<IActionResult> Login(string orgCode, CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(orgCode, cancellationToken);
        if (model is null) return NotFound();

        // Already signed in as staff: go straight to the console.
        if (User.IsInRole(AppRoles.OrgAdmin) || User.IsInRole(AppRoles.Evaluator))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        return View(model);
    }

    [HttpPost("admin/{orgCode}/login", Order = 100)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string orgCode, [Bind(Prefix = "Form")] LoginViewModel form, CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(orgCode, cancellationToken);
        if (model is null) return NotFound();
        model.Form = form;
        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(form.Email, form.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(form.Email);
            var organisationId = await _db.Organisations
                .Where(o => o.Code == model.OrgCode)
                .Select(o => o.Id)
                .SingleAsync(cancellationToken);

            var isStaff = user is not null
                && (await _userManager.IsInRoleAsync(user, AppRoles.OrgAdmin) || await _userManager.IsInRoleAsync(user, AppRoles.Evaluator));

            if (isStaff && user!.OrganisationId == organisationId)
            {
                return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
            }

            // Correct password, wrong door: sign straight back out and give the generic message.
            await _signInManager.SignOutAsync();
            await _audit.LogAsync("Account.LoginFailed", "Account", form.Email, organisationId, "Signed in at another organisation's portal");
        }
        else
        {
            await _audit.LogAsync("Account.LoginFailed", "Account", form.Email, null, $"Portal {model.OrgCode}");
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    /// <summary>Loads the organisation by its code; NULL (→ 404) if there is no such active organisation.</summary>
    private async Task<OrgPortalViewModel?> BuildModelAsync(string orgCode, CancellationToken cancellationToken)
    {
        var code = (orgCode ?? string.Empty).Trim().ToUpperInvariant();
        var org = await _db.Organisations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Code == code && o.IsActive, cancellationToken);
        if (org is null) return null;

        var now = DateTime.UtcNow;
        var startOfYear = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // This page is public, so no tenant filter is active: every query names the organisation itself.
        // The figures shown are public anyway (open tenders are advertised to everyone).
        var model = new OrgPortalViewModel
        {
            OrgCode = org.Code,
            OrgName = org.Name,
            LogoPath = org.LogoPath,
            PrimaryColour = BrandColours.Safe(org.PrimaryColour, BrandColours.DefaultPrimary),
            AccentColour = BrandColours.Safe(org.AccentColour, BrandColours.DefaultAccent),
            OpenTenders = await _db.Tenders.CountAsync(t => t.OrganisationId == org.Id
                && t.Status == TenderStatus.Published && t.ClosingDateUtc > now, cancellationToken),
            TendersThisYear = await _db.Tenders.CountAsync(t => t.OrganisationId == org.Id
                && t.PublishedAtUtc >= startOfYear, cancellationToken),
            RegisteredSuppliers = await (from ur in _db.UserRoles
                                         join r in _db.Roles on ur.RoleId equals r.Id
                                         where r.Name == AppRoles.Supplier
                                         select ur.UserId).CountAsync(cancellationToken)
        };

        // Development only: list this organisation's seeded staff so a demo can pick one quickly.
        if (_environment.IsDevelopment())
        {
            var staff = await _db.Users.AsNoTracking()
                .Where(u => u.OrganisationId == org.Id)
                .OrderBy(u => u.Email)
                .ToListAsync(cancellationToken);

            var accounts = new List<OrgPortalViewModel.DemoAccount>();
            foreach (var u in staff)
            {
                var label = await _userManager.IsInRoleAsync(u, AppRoles.OrgAdmin) ? RoleLabels.OrgAdmin : RoleLabels.Evaluator;
                accounts.Add(new(u.Email ?? string.Empty, label, $"{u.FullName} · {u.Email}"));
            }
            model.DemoAccounts = accounts;
        }

        return model;
    }
}
