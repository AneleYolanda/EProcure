using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.ViewModels.Supplier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Areas.Supplier;

/// <summary>
/// The application wizard (journey step 5; design screens "sApply", "sGate", "sPay", "sConfirm").
/// All rules are in ApplicationService. Every action loads the application through the service, which
/// only finds applications of the signed-in supplier's own company: anyone else's id gives 404.
/// </summary>
[Area("Supplier")]
[Authorize(Policy = "SupplierOnly")]
[ServiceFilter(typeof(EnsurePhoneVerifiedFilter))]
public class ApplicationsController : Controller
{
    private readonly IApplicationService _applications;
    private readonly EProcureDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UploadOptions _uploads;

    public ApplicationsController(IApplicationService applications, EProcureDbContext db, UserManager<ApplicationUser> userManager, IOptions<UploadOptions> uploads)
    {
        _applications = applications;
        _db = db;
        _userManager = userManager;
        _uploads = uploads.Value;
    }

    private string UserId => _userManager.GetUserId(User)!;

    // GET /Supplier/Applications   (journey step 6: track my applications; design "sApps")
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var companyId = await _db.SupplierProfiles.Where(p => p.UserId == UserId).Select(p => p.CompanyId).SingleOrDefaultAsync(ct);
        // Only this supplier's own company: another company's applications are never part of the query.
        var rows = companyId is null ? new List<MyApplicationRow>() : await _db.Submissions.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new MyApplicationRow(s.Id, s.Tender.ReferenceNumber, s.Tender.Title, s.Tender.Organisation.Name,
                s.Company.Name, s.Status, s.CreatedAtUtc, s.SubmittedAtUtc))
            .ToListAsync(ct);
        return View("Index", rows);
    }

    // GET /Supplier/Applications/Details/12   (design "sAppDetail")
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var s = await _applications.LoadAsync(id, UserId, ct);
        if (s is null) return NotFound();

        var history = await _db.SubmissionStatusHistory.AsNoTracking()
            .Where(h => h.SubmissionId == id)
            .OrderByDescending(h => h.ChangedAtUtc).ThenByDescending(h => h.Id)
            .Select(h => new { h.Note, h.ToStatus, h.ChangedAtUtc, h.ChangedByUserId, h.ChangedByUser.FullName, h.ChangedByUser.OrganisationId })
            .ToListAsync(ct);
        string YesNo(bool? v) => v is null ? "Not answered" : v.Value ? "Yes" : "No";
        var requirementNames = s.Tender.Requirements.ToDictionary(r => r.Id, r => r.Name);

        return View("Details", new MyApplicationDetailsViewModel
        {
            Id = s.Id,
            ReferenceNumber = s.ReferenceNumber,
            TenderId = s.TenderId,
            TenderReference = s.Tender.ReferenceNumber,
            TenderTitle = s.Tender.Title,
            OrganisationName = s.Tender.Organisation.Name,
            CompanyName = s.Company.Name,
            Status = s.Status,
            PaymentStatus = s.PaymentStatus,
            AmountPaid = s.AmountPaid,
            PaymentReference = s.PaymentReference,
            Answers = new List<(string, string)>
            {
                ("Registered on the CSD", YesNo(s.IsCsdRegistered)),
                ("Tax compliant", YesNo(s.IsTaxCompliant)),
                ("B-BBEE level declared", EligibilityRules.Describe(s.DeclaredBbbeeLevel)),
                ("SBD 4: interest declared", YesNo(s.HasDeclaredInterest) + (s.InterestDetails is null ? "" : $" ({s.InterestDetails})")),
                ("SBD 8: on a restricted list", s.ConfirmsNotRestricted is null ? "Not answered" : s.ConfirmsNotRestricted.Value ? "No" : $"Yes ({s.RestrictionDetails})"),
                ("SBD 9: independent bid", s.ConfirmsIndependentBid is null ? "Not answered" : s.ConfirmsIndependentBid.Value ? "Confirmed" : "Cannot confirm")
            },
            Documents = s.Documents.OrderBy(d => d.UploadedAtUtc).Select(d => new MyApplicationDetailsViewModel.DocumentRow(
                d.Id, d.TenderRequirementId is int r && requirementNames.TryGetValue(r, out var n) ? n : "Supporting document",
                d.OriginalFileName, d.SizeBytes, d.UploadedAtUtc)).ToList(),
            // The supplier sees who acted: themselves as "You", organisation staff by organisation (not by name).
            Timeline = history.Select(h => new MyApplicationDetailsViewModel.TimelineItem(
                h.Note ?? SubmissionStatuses.Label(h.ToStatus),
                h.ChangedByUserId == UserId ? "You" : h.OrganisationId is null ? h.FullName : s.Tender.Organisation.Name,
                h.ChangedAtUtc)).ToList(),
            ClosingDateUtc = s.Tender.ClosingDateUtc,
            TenderOpen = s.Tender.Status == TenderStatus.Published && s.Tender.ClosingDateUtc > DateTime.UtcNow,
            CanWithdraw = _applications.CheckCanWithdraw(s, DateTime.UtcNow) is null
        });
    }

    // POST /Supplier/Applications/Withdraw/12   Withdraw a submitted bid before the closing date (it stays on record).
    [HttpPost]
    public async Task<IActionResult> Withdraw(int id, string? reason, bool confirm, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        if (!confirm)
        {
            TempData["FlashError"] = "Tick the box to confirm that you want to withdraw this bid.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _applications.WithdrawAsync(submission, reason, UserId, ct);
        TempData[result.Succeeded ? "Flash" : "FlashError"] = result.Succeeded
            ? "Your bid has been withdrawn and will not be evaluated. You can reopen and resubmit it until the closing date."
            : string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET /Supplier/Applications/Document/7   Download one of your OWN uploaded PDFs.
    public async Task<IActionResult> Document(int id, [FromServices] IFileStorage files, CancellationToken ct)
    {
        var companyId = await _db.SupplierProfiles.Where(p => p.UserId == UserId).Select(p => p.CompanyId).SingleOrDefaultAsync(ct);
        var document = await _db.UploadedDocuments.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id && d.Submission.CompanyId == companyId, ct);
        if (document is null) return NotFound();

        var stream = await files.OpenReadAsync(document.StorageKey, ct);
        if (stream is null) return NotFound();
        return File(stream, "application/pdf", document.OriginalFileName);
    }

    // POST /Supplier/Applications/Start/5   (5 = tender id)
    [HttpPost]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var result = await _applications.StartAsync(id, UserId, ct);
        return result.Outcome switch
        {
            StartOutcome.Started or StartOutcome.Existing => RedirectToAction(nameof(Step), new { id = result.SubmissionId, n = 1 }),
            StartOutcome.NoCompany => RedirectToAction("Create", "Company"),
            StartOutcome.NotEligible => RedirectToAction(nameof(Gate), new { id }),
            StartOutcome.Closed => Flash("This tender has closed. Applications are no longer accepted.", "Details", "Tenders", id),
            _ => NotFound()
        };
    }

    // GET /Supplier/Applications/Gate/5   The hard stop: shown when the company does not qualify.
    public async Task<IActionResult> Gate(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.AsNoTracking().Include(t => t.Organisation)
            .SingleOrDefaultAsync(t => t.Id == id && t.Status == TenderStatus.Published, ct);
        if (tender is null) return NotFound();
        var company = await _db.SupplierProfiles.AsNoTracking().Where(p => p.UserId == UserId).Select(p => p.Company).SingleOrDefaultAsync(ct);
        var eligibility = EligibilityRules.CheckBbbee(company?.BbbeeLevel, tender.MinimumBbbeeLevel);
        if (eligibility.Outcome != EligibilityOutcome.NotEligible) return RedirectToAction("Details", "Tenders", new { id });

        var now = DateTime.UtcNow;
        var open = await _db.Tenders.AsNoTracking()
            .Where(t => t.Status == TenderStatus.Published && t.ClosingDateUtc > now)
            .Select(t => t.MinimumBbbeeLevel).ToListAsync(ct);
        return View(new GateViewModel
        {
            TenderReference = tender.ReferenceNumber,
            OrganisationName = tender.Organisation.Name,
            Reason = eligibility.Reason,
            QualifyingCount = open.Count(min => EligibilityRules.CheckBbbee(company?.BbbeeLevel, min).Outcome == EligibilityOutcome.Qualifies)
        });
    }

    // GET /Supplier/Applications/Step/12?n=3
    public async Task<IActionResult> Step(int id, int n, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        if (submission.Status == SubmissionStatus.AwaitingPayment) return RedirectToAction(nameof(Pay), new { id });
        if (submission.Status != SubmissionStatus.Draft) return RedirectToAction(nameof(Confirmation), new { id });
        if (_applications.CheckCanChange(submission, DateTime.UtcNow) is string blocked)
        {
            var eligibility = EligibilityRules.CheckBbbee(submission.Company.BbbeeLevel, submission.Tender.MinimumBbbeeLevel);
            return eligibility.Outcome == EligibilityOutcome.NotEligible
                ? RedirectToAction(nameof(Gate), new { id = submission.TenderId })
                : Flash(blocked, "Details", "Tenders", submission.TenderId);
        }

        return View("Step", BuildStep(submission, Math.Clamp(n, 1, ApplyStepViewModel.StepCount)));
    }

    [HttpPost]
    public async Task<IActionResult> Compliance(int id, bool? csdRegistered, bool? taxCompliant, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        // If the page has to be shown again, keep what the supplier picked (not what is saved).
        void KeepAnswers(ApplyStepViewModel vm) { vm.CsdRegistered = csdRegistered; vm.TaxCompliant = taxCompliant; }

        if (csdRegistered is null || taxCompliant is null)
            return StepWithErrors(submission, 2, new ServiceResult().With(string.Empty, "Answer both questions."), KeepAnswers);

        var result = await _applications.SaveComplianceAsync(submission, csdRegistered.Value, taxCompliant.Value, ct);
        return result.Succeeded ? RedirectToAction(nameof(Step), new { id, n = 3 }) : StepWithErrors(submission, 2, result, KeepAnswers);
    }

    [HttpPost]
    public async Task<IActionResult> Declarations(int id, bool? hasInterest, string? interestDetails, bool? onRestrictedList,
        string? restrictionDetails, bool? independentBid, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        void KeepAnswers(ApplyStepViewModel vm)
        {
            vm.HasInterest = hasInterest; vm.InterestDetails = interestDetails;
            vm.OnRestrictedList = onRestrictedList; vm.RestrictionDetails = restrictionDetails;
            vm.IndependentBid = independentBid;
        }

        if (hasInterest is null || onRestrictedList is null || independentBid is null)
            return StepWithErrors(submission, 3, new ServiceResult().With(string.Empty, "Answer all three declarations."), KeepAnswers);

        var result = await _applications.SaveDeclarationsAsync(submission, hasInterest.Value, interestDetails,
            onRestrictedList.Value, restrictionDetails, independentBid.Value, ct);
        return result.Succeeded ? RedirectToAction(nameof(Step), new { id, n = 4 }) : StepWithErrors(submission, 3, result, KeepAnswers);
    }

    // POST /Supplier/Applications/Upload/12   (one PDF for one checklist item)
    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]           // a little above the 5 MB file limit, for the form overhead
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, int requirementId, IFormFile? file, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        if (file is null) return StepWithError(submission, 4, "Choose a PDF file to upload.");

        var result = await _applications.UploadAsync(submission, requirementId, file, UserId, ct);
        if (!result.Succeeded) return StepWithErrors(submission, 4, result);
        TempData["Flash"] = "Uploaded.";
        return RedirectToAction(nameof(Step), new { id, n = 4 });
    }

    [HttpPost]
    public async Task<IActionResult> UseProfileDocument(int id, int requirementId, int complianceDocumentId, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        var result = await _applications.UseProfileDocumentAsync(submission, requirementId, complianceDocumentId, UserId, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded) return StepWithErrors(submission, 4, result);
        TempData["Flash"] = "Attached from your profile.";
        return RedirectToAction(nameof(Step), new { id, n = 4 });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveDocument(int id, int documentId, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        var result = await _applications.RemoveDocumentAsync(submission, documentId, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? RedirectToAction(nameof(Step), new { id, n = 4 }) : StepWithErrors(submission, 4, result);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(int id, bool declared, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();

        var result = await _applications.SubmitAsync(submission, declared, UserId, ct);
        if (!result.Succeeded) return StepWithErrors(submission, 5, result);
        return submission.Status == SubmissionStatus.Submitted
            ? RedirectToAction(nameof(Confirmation), new { id })
            : RedirectToAction(nameof(Pay), new { id });
    }

    // GET /Supplier/Applications/Pay/12
    public async Task<IActionResult> Pay(int id, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        if (submission.Status == SubmissionStatus.Submitted) return RedirectToAction(nameof(Confirmation), new { id });
        if (submission.Status != SubmissionStatus.AwaitingPayment) return RedirectToAction(nameof(Step), new { id, n = 5 });

        return View(new PayViewModel
        {
            SubmissionId = id,
            TenderReference = submission.Tender.ReferenceNumber,
            OrganisationName = submission.Tender.Organisation.Name,
            Amount = submission.Tender.TenderFee,
            ClosingDateUtc = submission.Tender.ClosingDateUtc,
            LastAttemptFailed = submission.PaymentStatus == PaymentStatus.Failed
        });
    }

    [HttpPost]
    public async Task<IActionResult> Pay(int id, string method, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();

        var allowed = new[] { "card", "eft", "transfer" };
        var returnUrl = Url.Action(nameof(PaymentReturn), new { id })!;
        var (result, redirect) = await _applications.StartPaymentAsync(submission, allowed.Contains(method) ? method : "card", returnUrl, ct);
        if (!result.Succeeded)
        {
            TempData["FlashError"] = string.Join(" ", result.Errors.Select(e => e.Message));
            return RedirectToAction(nameof(Pay), new { id });
        }
        return LocalRedirect(redirect!); // the mock gateway lives inside this app; a real provider would be an external URL
    }

    // GET /Supplier/Applications/PaymentReturn/12?reference=MOCK-...   (the provider sends the supplier back here)
    public async Task<IActionResult> PaymentReturn(int id, string reference, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();

        var result = await _applications.CompletePaymentAsync(submission, reference, UserId, ct);
        if (result.Succeeded) return RedirectToAction(nameof(Confirmation), new { id });
        TempData["FlashError"] = string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Pay), new { id });
    }

    // GET /Supplier/Applications/Confirmation/12
    public async Task<IActionResult> Confirmation(int id, CancellationToken ct)
    {
        var submission = await _applications.LoadAsync(id, UserId, ct);
        if (submission is null) return NotFound();
        if (submission.Status is SubmissionStatus.Draft) return RedirectToAction(nameof(Step), new { id, n = 1 });
        if (submission.Status is SubmissionStatus.AwaitingPayment) return RedirectToAction(nameof(Pay), new { id });
        if (submission.Status is SubmissionStatus.Withdrawn) return RedirectToAction(nameof(Details), new { id });

        return View(new ConfirmationViewModel
        {
            ReferenceNumber = submission.ReferenceNumber ?? string.Empty,
            TenderReference = submission.Tender.ReferenceNumber,
            TenderTitle = submission.Tender.Title,
            OrganisationName = submission.Tender.Organisation.Name,
            CompanyName = submission.Company.Name,
            AmountPaid = submission.AmountPaid,
            FeePaid = submission.PaymentStatus == PaymentStatus.Paid,
            DocumentCount = submission.Documents.Count
        });
    }

    // ------------------------------------------------------------------ helpers

    private IActionResult Flash(string message, string action, string controller, int id)
    {
        TempData["FlashError"] = message;
        return RedirectToAction(action, controller, new { id });
    }

    private IActionResult StepWithError(Submission submission, int step, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        return View("Step", BuildStep(submission, step));
    }

    private IActionResult StepWithErrors(Submission submission, int step, ServiceResult result, Action<ApplyStepViewModel>? keepPosted = null)
    {
        foreach (var (field, message) in result.Errors) ModelState.AddModelError(field, message);
        var model = BuildStep(submission, step);
        keepPosted?.Invoke(model);
        return View("Step", model);
    }

    /// <summary>For each checklist item: the company's current, unexpired compliance documents that can fill it.</summary>
    private Dictionary<int, IReadOnlyList<ApplyStepViewModel.ProfileOption>> ProfileOptions(Submission s)
    {
        var today = SaTime.ToSast(DateTime.UtcNow);
        var closingDay = SaTime.ToSast(s.Tender.ClosingDateUtc).Date;
        var documents = _db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.CompanyId == s.CompanyId && d.ArchivedAtUtc == null)
            .ToList()
            .Where(d => ComplianceRules.Status(d.ExpiresOn, today) != ComplianceStatus.Expired)
            .ToList();
        var options = new Dictionary<int, IReadOnlyList<ApplyStepViewModel.ProfileOption>>();
        foreach (var requirement in s.Tender.Requirements)
        {
            var types = ComplianceRules.TypesFor(requirement.Name);
            var matches = documents.Where(d => types.Contains(d.Type))
                .Select(d => new ApplyStepViewModel.ProfileOption(d.Id, ComplianceRules.Info(d.Type).Label,
                    ComplianceRules.Describe(d.ExpiresOn, today), d.ExpiresOn is DateTime last && last < closingDay))
                .ToList();
            if (matches.Count > 0) options[requirement.Id] = matches;
        }
        return options;
    }

    private ApplyStepViewModel BuildStep(Submission s, int step)
    {
        var company = s.Company;
        var uploaded = s.Documents.Where(d => d.TenderRequirementId != null).ToDictionary(d => d.TenderRequirementId!.Value);
        var missing = _applications.MissingItems(s);
        var eligibility = EligibilityRules.CheckBbbee(company.BbbeeLevel, s.Tender.MinimumBbbeeLevel);

        return new ApplyStepViewModel
        {
            ProfileOptions = step == 4 ? ProfileOptions(s) : new Dictionary<int, IReadOnlyList<ApplyStepViewModel.ProfileOption>>(),
            SubmissionId = s.Id,
            Step = step,
            TenderId = s.TenderId,
            TenderReference = s.Tender.ReferenceNumber,
            TenderTitle = s.Tender.Title,
            OrganisationName = s.Tender.Organisation.Name,
            TenderFee = s.Tender.TenderFee,
            ClosingDateUtc = s.Tender.ClosingDateUtc,
            CompanyName = company.Name,
            CompanyFields = new List<(string, string)>
            {
                ("CIPC registration", company.RegistrationNumber),
                ("Tax number / TCS PIN", company.TaxPin),
                ("CSD number", company.CsdNumber),
                ("B-BBEE", $"{EligibilityRules.Describe(company.BbbeeLevel)} · {company.EnterpriseSize}"),
                ("Sector", company.Sector)
            },
            CsdRegistered = s.IsCsdRegistered,
            TaxCompliant = s.IsTaxCompliant,
            HasInterest = s.HasDeclaredInterest,
            InterestDetails = s.InterestDetails,
            OnRestrictedList = s.ConfirmsNotRestricted is bool notRestricted ? !notRestricted : null,
            RestrictionDetails = s.RestrictionDetails,
            IndependentBid = s.ConfirmsIndependentBid,
            MaxFileSizeBytes = _uploads.MaxFileSizeBytes,
            Checklist = s.Tender.Requirements.OrderBy(r => r.SortOrder).Select(r =>
                uploaded.TryGetValue(r.Id, out var d)
                    ? new ApplyStepViewModel.ChecklistItem(r.Id, r.Name, r.IsMandatory, d.Id, d.OriginalFileName, d.SizeBytes, d.UploadedAtUtc)
                    : new ApplyStepViewModel.ChecklistItem(r.Id, r.Name, r.IsMandatory, null, null, 0, null)).ToList(),
            ReviewRows = new List<(string, string)>
            {
                ("Tender", s.Tender.ReferenceNumber),
                ("Organisation", s.Tender.Organisation.Name),
                ("Bidding as", company.Name),
                ("Compliance answers", s.IsCsdRegistered is null || s.IsTaxCompliant is null ? "Not complete" : "2 of 2 answered"),
                ("Declarations", s.HasDeclaredInterest is null || s.ConfirmsNotRestricted is null || s.ConfirmsIndependentBid is null ? "Not complete" : "SBD 4, 8 and 9 answered"),
                ("Documents attached", $"{uploaded.Count} of {s.Tender.Requirements.Count}"),
                ("Tender fee due", DisplayFormat.Fee(s.Tender.TenderFee))
            },
            Missing = missing,
            EligibilityText = eligibility.Reason
        };
    }
}
