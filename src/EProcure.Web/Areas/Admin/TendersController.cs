using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// Tender register, create/edit (drafts only), publish and cancel. Journey step 1 of the MVP.
/// Everyone on the organisation's staff can VIEW (OrgStaff); only SCM Officers (OrgAdmin) can change
/// anything (OrgAdminOnly). Evaluators are read-only so the people who score bids cannot also shape
/// the tender (separation of duties).
/// A tender of another organisation is invisible (tenant filter), so its id returns 404, not 403.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgStaff")]
public class TendersController : Controller
{
    private readonly EProcureDbContext _db;
    private readonly ITenderService _tenders;
    private readonly ICurrentOrganisation _currentOrganisation;
    private readonly UserManager<ApplicationUser> _userManager;

    public TendersController(EProcureDbContext db, ITenderService tenders, ICurrentOrganisation currentOrganisation, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _tenders = tenders;
        _currentOrganisation = currentOrganisation;
        _userManager = userManager;
    }

    // GET /Admin/Tenders?stage=advertised&q=road
    public async Task<IActionResult> Index(string? stage, string? q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var query = _db.Tenders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t => t.Title.Contains(term) || t.ReferenceNumber.Contains(term) || t.Category.Contains(term));
        }

        var rows = (await query
                .OrderByDescending(t => t.CreatedAtUtc)
                .Select(t => new { t.Id, t.ReferenceNumber, t.Title, t.Status, t.EstimatedValue, t.ClosingDateUtc, Applications = t.Submissions.Count(s => s.Status != SubmissionStatus.Withdrawn) })
                .ToListAsync(ct))
            .Select(t => new TenderRegisterRow(t.Id, t.ReferenceNumber, t.Title, TenderStages.For(t.Status, t.ClosingDateUtc, now),
                t.EstimatedValue, t.ClosingDateUtc, t.Applications))
            .ToList();

        var counts = TenderStages.All.ToDictionary(s => s, s => rows.Count(r => r.Stage == s));
        var selected = TenderStages.All.FirstOrDefault(s => TenderStages.Key(s) == stage);
        var visible = stage is null || !TenderStages.All.Any(s => TenderStages.Key(s) == stage)
            ? rows
            : rows.Where(r => r.Stage == selected).ToList();

        return View(new TenderRegisterViewModel
        {
            Stage = visible == rows ? null : stage,
            Query = q,
            CanCreate = User.IsInRole(AppRoles.OrgAdmin),
            Counts = counts,
            Rows = visible
        });
    }

    // GET /Admin/Tenders/Details/5
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await BuildDetailsAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var organisation = await _currentOrganisation.GetAsync(ct);
        var threeWeeks = SaTime.ToSast(DateTime.UtcNow).Date.AddDays(21).AddHours(11); // 11:00 SAST, 21 days out
        return View(new TenderFormViewModel
        {
            PointSystem = organisation?.DefaultPointSystem ?? PreferencePointSystem.EightyTwenty,
            ClosingDateLocal = threeWeeks,
            FunctionalityThreshold = 70,
            FunctionalityCriteria = TenderFormViewModel.SuggestedCriteria(),
            SelectedDocuments = TenderCatalog.DefaultDocuments.ToList()
        });
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Create(TenderFormViewModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);

        var result = await _tenders.CreateAsync(form, _userManager.GetUserId(User)!, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded) return ShowErrors(result, form);

        TempData["Flash"] = "Draft saved. Nothing is visible to bidders until you publish it.";
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.AsNoTracking().Include(t => t.Requirements).Include(t => t.FunctionalityCriteria)
            .SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return NotFound();
        if (tender.Status != TenderStatus.Draft)
        {
            TempData["Flash"] = "Only draft tenders can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var names = tender.Requirements.OrderBy(r => r.SortOrder).Select(r => r.Name).ToList();
        ViewData["TenderId"] = id;
        return View(new TenderFormViewModel
        {
            Title = tender.Title,
            ReferenceNumber = tender.ReferenceNumber,
            Category = tender.Category,
            Description = tender.Description,
            ClosingDateLocal = SaTime.ToSast(tender.ClosingDateUtc),
            TenderFee = tender.TenderFee,
            EstimatedValue = tender.EstimatedValue,
            MinimumBbbeeLevel = tender.MinimumBbbeeLevel,
            PointSystem = tender.PointSystem,
            UseFunctionality = tender.FunctionalityThreshold is not null,
            FunctionalityThreshold = tender.FunctionalityThreshold ?? 70,
            FunctionalityCriteria = tender.FunctionalityThreshold is null
                ? TenderFormViewModel.SuggestedCriteria()
                : tender.FunctionalityCriteria.OrderBy(c => c.SortOrder).Select(c => new TenderFormViewModel.CriterionInput { Name = c.Name, Weight = c.Weight }).ToList(),
            SelectedDocuments = names.Where(n => TenderCatalog.StandardDocuments.Contains(n)).ToList(),
            OtherDocuments = string.Join("\n", names.Where(n => !TenderCatalog.StandardDocuments.Contains(n)))
        });
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Edit(int id, TenderFormViewModel form, CancellationToken ct)
    {
        ViewData["TenderId"] = id;
        if (!ModelState.IsValid) return View(form);

        var result = await _tenders.UpdateDraftAsync(id, form, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded) return ShowErrors(result, form);

        TempData["Flash"] = "Draft updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Publishing needs the "I confirm" tick box on the tender page (a deliberate second step).</summary>
    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Publish(int id, bool confirm, CancellationToken ct)
    {
        if (!confirm)
        {
            TempData["FlashError"] = "Tick the confirmation box to publish.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _tenders.PublishAsync(id, ct);
        if (result.NotFound) return NotFound();
        TempData[result.Succeeded ? "Flash" : "FlashError"] = result.Succeeded
            ? "Published. The tender is now visible to every supplier in the eProcure marketplace."
            : string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---- Publication approval (four-eyes) ----

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> RequestApproval(int id, bool confirm, CancellationToken ct)
    {
        if (!confirm)
        {
            TempData["FlashError"] = "Tick the confirmation box to submit the tender for approval.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return Done(id, await _tenders.RequestApprovalAsync(id, UserId, ct),
            "Submitted for approval. Another SCM Officer has been asked to approve it; the draft is locked meanwhile.");
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Approve(int id, bool confirm, CancellationToken ct)
    {
        if (!confirm)
        {
            TempData["FlashError"] = "Tick the confirmation box to approve and publish.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return Done(id, await _tenders.ApproveAsync(id, UserId, ct),
            "Approved and published. The tender is now visible to every supplier in the eProcure marketplace.");
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> ReturnForChanges(int id, string? returnNote, CancellationToken ct) =>
        Done(id, await _tenders.ReturnForChangesAsync(id, UserId, returnNote, ct),
            "Sent back with your note. The requester can edit the draft and submit it again.");

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> WithdrawApproval(int id, CancellationToken ct) =>
        Done(id, await _tenders.WithdrawApprovalRequestAsync(id, UserId, ct), "Approval request withdrawn. The draft can be edited again.");

    private string UserId => _userManager.GetUserId(User)!;

    private IActionResult Done(int id, ServiceResult result, string success)
    {
        if (result.NotFound) return NotFound();
        TempData[result.Succeeded ? "Flash" : "FlashError"] = result.Succeeded ? success : string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return NotFound();
        return View(new CancelTenderViewModel { Id = id, ReferenceNumber = tender.ReferenceNumber, Title = tender.Title });
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Cancel(int id, CancelTenderViewModel form, CancellationToken ct)
    {
        var tender = await _db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return NotFound();
        form.Id = id;
        form.ReferenceNumber = tender.ReferenceNumber;
        form.Title = tender.Title;
        if (!ModelState.IsValid) return View(form);

        var result = await _tenders.CancelAsync(id, form.Reason, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors) ModelState.AddModelError(field, message);
            return View(form);
        }

        TempData["Flash"] = "Tender cancelled. It stays on record with your reason.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ------------------------------------------------------------------ helpers

    private IActionResult ShowErrors(ServiceResult result, TenderFormViewModel form)
    {
        foreach (var (field, message) in result.Errors) ModelState.AddModelError(field, message);
        return View(form);
    }

    private async Task<TenderDetailsViewModel?> BuildDetailsAsync(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.AsNoTracking()
            .Include(t => t.Requirements)
            .Include(t => t.CreatedByUser)
            .Include(t => t.FunctionalityCriteria)
            .Include(t => t.ApprovalRequestedByUser)
            .Include(t => t.ApprovedByUser)
            .SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return null;

        var now = DateTime.UtcNow;
        var key = tender.Id.ToString();
        var entries = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == "Tender" && a.EntityId == key)
            .OrderByDescending(a => a.OccurredAtUtc)
            .ToListAsync(ct);
        var userIds = entries.Where(e => e.UserId != null).Select(e => e.UserId!).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return new TenderDetailsViewModel
        {
            Id = tender.Id,
            ReferenceNumber = tender.ReferenceNumber,
            Title = tender.Title,
            Category = tender.Category,
            Description = tender.Description,
            Stage = TenderStages.For(tender.Status, tender.ClosingDateUtc, now),
            ClosingDateUtc = tender.ClosingDateUtc,
            TenderFee = tender.TenderFee,
            EstimatedValue = tender.EstimatedValue,
            MinimumBbbeeLevel = tender.MinimumBbbeeLevel,
            PointSystem = tender.PointSystem,
            FunctionalityThreshold = tender.FunctionalityThreshold,
            FunctionalityCriteria = tender.FunctionalityCriteria.OrderBy(c => c.SortOrder).Select(c => (c.Name, c.Weight)).ToList(),
            CreatedBy = tender.CreatedByUser.FullName,
            CreatedAtUtc = tender.CreatedAtUtc,
            PublishedAtUtc = tender.PublishedAtUtc,
            CancellationReason = tender.CancellationReason,
            Applications = await _db.Submissions.CountAsync(s => s.TenderId == tender.Id && s.Status != SubmissionStatus.Withdrawn, ct),
            Requirements = tender.Requirements.OrderBy(r => r.SortOrder).Select(r => r.Name).ToList(),
            CanManage = User.IsInRole(AppRoles.OrgAdmin),
            PublishChecks = _tenders.PublishChecks(tender, now),
            RequiresApproval = await _tenders.RequiresApprovalAsync(ct),
            ApprovalPending = tender.ApprovalRequestedAtUtc is not null,
            ApprovalRequestedBy = tender.ApprovalRequestedByUser?.FullName,
            ApprovalRequestedAtUtc = tender.ApprovalRequestedAtUtc,
            IsApprovalRequester = tender.ApprovalRequestedByUserId == UserId,
            ApprovalReturnNote = tender.ApprovalReturnNote,
            ApprovedBy = tender.ApprovedByUser?.FullName,
            History = entries.Select(e => new TenderDetailsViewModel.HistoryItem(
                Describe(e.Action, e.Details),
                e.UserId != null && names.TryGetValue(e.UserId, out var n) ? n : e.UserEmail ?? "System",
                e.OccurredAtUtc)).ToList()
        };
    }

    private static string Describe(string action, string? details) => action switch
    {
        "Tender.Created" => "Draft created",
        "Tender.Updated" => "Draft edited",
        "Tender.Published" => "Published to the eProcure marketplace",
        "Tender.Cancelled" => $"Cancelled: {details}",
        "Tender.ApprovalRequested" => "Submitted for approval by a second SCM Officer",
        "Tender.Approved" => "Approved by a second SCM Officer",
        "Tender.ApprovalReturned" => $"Sent back before publication: {details}",
        "Tender.ApprovalWithdrawn" => "Approval request withdrawn",
        _ => action
    };
}
