using EProcure.Web.Domain;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Supplier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Areas.Supplier;

/// <summary>
/// The supplier's company profile (journey step 3; design screens "sProfile" and "sAddCo").
/// No action takes a company id: the company is always the signed-in supplier's own (see CompanyService),
/// so there is no URL a supplier could change to reach someone else's company.
/// </summary>
[Area("Supplier")]
[Authorize(Policy = "SupplierOnly")]
[ServiceFilter(typeof(EnsurePhoneVerifiedFilter))]
public class CompanyController : Controller
{
    private readonly ICompanyService _companies;
    private readonly UserManager<ApplicationUser> _userManager;

    public CompanyController(ICompanyService companies, UserManager<ApplicationUser> userManager)
    {
        _companies = companies;
        _userManager = userManager;
    }

    // GET /Supplier/Company
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var company = await _companies.GetForUserAsync(_userManager.GetUserId(User)!, ct);
        return View(company);
    }

    // GET /Supplier/Company/Create  (?welcome=true straight after registration: "Step 2 of 2")
    public async Task<IActionResult> Create(bool welcome, CancellationToken ct)
    {
        if (await _companies.GetForUserAsync(_userManager.GetUserId(User)!, ct) is not null)
            return RedirectToAction(nameof(Edit));

        ViewData["Welcome"] = welcome;
        return View("Form", new CompanyFormViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CompanyFormViewModel form, bool welcome, CancellationToken ct)
    {
        ViewData["Welcome"] = welcome;
        return await SaveAsync(form, "Company saved. Tenders now show whether you qualify.", ct);
    }

    // GET /Supplier/Company/Edit
    public async Task<IActionResult> Edit(CancellationToken ct)
    {
        var company = await _companies.GetForUserAsync(_userManager.GetUserId(User)!, ct);
        if (company is null) return RedirectToAction(nameof(Create));

        ViewData["IsEdit"] = true;
        ViewData["IdentityLocked"] = await _companies.HasSubmissionsAsync(company.Id, ct);
        return View("Form", new CompanyFormViewModel
        {
            Name = company.Name,
            RegistrationNumber = company.RegistrationNumber,
            TaxPin = company.TaxPin,
            CsdNumber = company.CsdNumber,
            BbbeeLevel = company.BbbeeLevel,
            EnterpriseSize = company.EnterpriseSize,
            Sector = company.Sector
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(CompanyFormViewModel form, CancellationToken ct)
    {
        ViewData["IsEdit"] = true;
        return await SaveAsync(form, "Company details updated.", ct);
    }

    private async Task<IActionResult> SaveAsync(CompanyFormViewModel form, string successMessage, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Form", form);

        var result = await _companies.SaveAsync(_userManager.GetUserId(User)!, form, ct);
        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors) ModelState.AddModelError(field, message);
            return View("Form", form);
        }

        TempData["Flash"] = successMessage;
        return RedirectToAction(nameof(Index));
    }
}
