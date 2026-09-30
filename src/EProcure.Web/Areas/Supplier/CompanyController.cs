using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
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
    private readonly ITrackRecordService _trackRecord;
    private readonly IComplianceService _compliance;
    private readonly IFileStorage _files;
    private readonly UserManager<ApplicationUser> _userManager;

    public CompanyController(ICompanyService companies, ITrackRecordService trackRecord, IComplianceService compliance, IFileStorage files,
        UserManager<ApplicationUser> userManager)
    {
        _companies = companies;
        _trackRecord = trackRecord;
        _compliance = compliance;
        _files = files;
        _userManager = userManager;
    }

    // GET /Supplier/Company
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var company = await _companies.GetForUserAsync(_userManager.GetUserId(User)!, ct);
        ViewData["TrackRecordCount"] = (await _trackRecord.ListOwnAsync(_userManager.GetUserId(User)!, ct)).Count;
        ViewData["Compliance"] = await _compliance.ListCurrentAsync(_userManager.GetUserId(User)!, ct);
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
        var saved = await SaveAsync(form, "Company saved. Tenders now show whether you qualify.", ct);
        // Registration continues with the compliance documents (step 3 of 4) and the track record (step 4 of 4); both can wait.
        return welcome && saved is RedirectToActionResult ? RedirectToAction(nameof(Compliance), new { welcome = true }) : saved;
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

    // ---- Compliance documents: CSD report, tax status, B-BBEE, IDs ... (kept on the profile, reminded before expiry) ----

    // GET /Supplier/Company/Compliance   (?welcome=true straight after adding the company: "Step 3 of 4")
    public async Task<IActionResult> Compliance(bool welcome, CancellationToken ct)
    {
        if (await _companies.GetForUserAsync(_userManager.GetUserId(User)!, ct) is null)
            return RedirectToAction(nameof(Create));
        return View(await CompliancePageAsync(welcome, new ComplianceFormViewModel(), ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddCompliance(ComplianceFormViewModel form, IFormFile? file, bool welcome, CancellationToken ct)
    {
        var result = await _compliance.AddAsync(_userManager.GetUserId(User)!, new ComplianceInput(form.Type, form.IssuedOn, form.ExpiresOn), file, ct);
        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors)
                ModelState.AddModelError(field is "" or "File" ? field : "Form." + field, message); // the PDF is posted outside the form model
            return View(nameof(Compliance), await CompliancePageAsync(welcome, form, ct));
        }

        TempData["Flash"] = "Saved. You will be reminded by email before it expires.";
        return RedirectToAction(nameof(Compliance), new { welcome = welcome ? true : (bool?)null });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveCompliance(int documentId, CancellationToken ct)
    {
        var result = await _compliance.RemoveAsync(_userManager.GetUserId(User)!, documentId, ct);
        if (result.NotFound) return NotFound();
        TempData["Flash"] = "Removed from your profile. Bids that already used it keep their own copy.";
        return RedirectToAction(nameof(Compliance));
    }

    // GET /Supplier/Company/ComplianceFile/7   The supplier's own document only.
    public async Task<IActionResult> ComplianceFile(int id, CancellationToken ct)
    {
        var document = await _compliance.FindOwnAsync(_userManager.GetUserId(User)!, id, ct);
        if (document is null) return NotFound();
        var stream = await _files.OpenReadAsync(document.StorageKey, ct);
        return stream is null ? NotFound() : File(stream, "application/pdf", document.OriginalFileName);
    }

    private async Task<CompliancePageViewModel> CompliancePageAsync(bool welcome, ComplianceFormViewModel form, CancellationToken ct) => new()
    {
        Welcome = welcome,
        TodaySast = SaTime.ToSast(DateTime.UtcNow).Date,
        Current = await _compliance.ListCurrentAsync(_userManager.GetUserId(User)!, ct),
        Form = form
    };

    // ---- Track record: past work, reference letters, company profile (uploaded once, part of every bid) ----

    // GET /Supplier/Company/TrackRecord   (?welcome=true after the compliance documents: "Step 4 of 4")
    public async Task<IActionResult> TrackRecord(bool welcome, CancellationToken ct)
    {
        if (await _companies.GetForUserAsync(_userManager.GetUserId(User)!, ct) is null)
            return RedirectToAction(nameof(Create));
        return View(await TrackRecordPageAsync(welcome, new TrackRecordFormViewModel(), ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddTrackRecord(TrackRecordFormViewModel form, IFormFile? file, bool welcome, CancellationToken ct)
    {
        var result = await _trackRecord.AddAsync(_userManager.GetUserId(User)!,
            new TrackRecordInput(form.Kind, form.Title, form.ClientName, form.YearCompleted, form.ContractValue), file, ct);
        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors)
                ModelState.AddModelError(field is "" or "File" ? field : "Form." + field, message); // the PDF is posted outside the form model
            return View(nameof(TrackRecord), await TrackRecordPageAsync(welcome, form, ct));
        }

        TempData["Flash"] = "Added to your track record. Committees see it with every bid you submit.";
        return RedirectToAction(nameof(TrackRecord), new { welcome = welcome ? true : (bool?)null });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveTrackRecord(int documentId, CancellationToken ct)
    {
        var result = await _trackRecord.RemoveAsync(_userManager.GetUserId(User)!, documentId, ct);
        if (result.NotFound) return NotFound();
        TempData["Flash"] = "Removed from your track record. Bids for tenders that have already closed keep it.";
        return RedirectToAction(nameof(TrackRecord));
    }

    // GET /Supplier/Company/TrackRecordFile/7   The supplier's own document only.
    public async Task<IActionResult> TrackRecordFile(int id, CancellationToken ct)
    {
        var document = await _trackRecord.FindOwnAsync(_userManager.GetUserId(User)!, id, ct);
        if (document is null) return NotFound();
        var stream = await _files.OpenReadAsync(document.StorageKey, ct);
        return stream is null ? NotFound() : File(stream, "application/pdf", document.OriginalFileName);
    }

    private async Task<TrackRecordPageViewModel> TrackRecordPageAsync(bool welcome, TrackRecordFormViewModel form, CancellationToken ct) => new()
    {
        Welcome = welcome,
        Documents = await _trackRecord.ListOwnAsync(_userManager.GetUserId(User)!, ct),
        Form = form
    };

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
