using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// Bid evaluation (BEC) and adjudication (BAC). Every rule lives in EvaluationService; this controller only
/// decides WHO may press which button:
///   - BEC members (Evaluator role) evaluate bids and submit the recommendation;
///   - the SCM Officer (OrgAdmin role) records the BAC's decision or returns the evaluation to the BEC;
///   - both can read everything, so each committee sees the other's work.
/// Another organisation's tender or bid is "not found" (tenant query filters).
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgStaff")]
public class EvaluationController : Controller
{
    private readonly IEvaluationService _evaluation;
    private readonly ITrackRecordService _trackRecord;
    private readonly EProcureDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public EvaluationController(IEvaluationService evaluation, ITrackRecordService trackRecord, EProcureDbContext db, UserManager<ApplicationUser> userManager)
    {
        _evaluation = evaluation;
        _trackRecord = trackRecord;
        _db = db;
        _userManager = userManager;
    }

    // GET /Admin/Evaluation?view=scoring|adjudication
    public async Task<IActionResult> Index(string? view, CancellationToken ct)
    {
        var rows = await _evaluation.ListAsync(ct);
        var filter = view is "scoring" or "adjudication" ? view : "all";
        rows = filter switch
        {
            "scoring" => rows.Where(r => r.Stage == EvaluationStage.Evaluating).ToList(),
            "adjudication" => rows.Where(r => r.Stage == EvaluationStage.AwaitingAdjudication).ToList(),
            _ => rows
        };
        return View(new EvaluationListViewModel { View = filter, Rows = rows });
    }

    // GET /Admin/Evaluation/Tender/5   The scoresheet.
    public async Task<IActionResult> Tender(int id, CancellationToken ct)
    {
        var model = await PageAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }

    // GET /Admin/Evaluation/Bid/12   One bid: documents to read, and the BEC's evaluation form.
    public async Task<IActionResult> Bid(int id, CancellationToken ct)
    {
        var model = await BidPageAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [Authorize(Policy = "BecOnly")]
    public async Task<IActionResult> Bid(int id, EvaluateBidForm form, CancellationToken ct)
    {
        var result = await _evaluation.CaptureAsync(id, new BidEvaluationInput(form.IsResponsive, form.NonResponsiveReason, form.BidPrice, form.Notes, form.Ratings),
            _userManager.GetUserId(User)!, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            var model = await BidPageAsync(id, ct);
            if (model is null) return NotFound();
            model.Form = form;
            AddErrors(result, "Form.");
            return View(model);
        }

        var tenderId = await _db.Submissions.Where(s => s.Id == id).Select(s => s.TenderId).SingleAsync(ct);
        TempData["Flash"] = "Evaluation saved. Points are recalculated for every bid on the scoresheet.";
        return RedirectToAction(nameof(Tender), new { id = tenderId });
    }

    [HttpPost]
    [Authorize(Policy = "BecOnly")]
    public async Task<IActionResult> Recommend(int id, [Bind(Prefix = "Recommend")] RecommendForm form, CancellationToken ct)
    {
        var result = await _evaluation.SubmitRecommendationAsync(id, form.RecommendedSubmissionId, form.Reason, _userManager.GetUserId(User)!, ct);
        return await AfterAsync(id, result, "Recommend.", m => m.Recommend = form,
            "The scoresheet and recommendation have been submitted to the BAC. The evaluation is now locked.", ct);
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Award(int id, [Bind(Prefix = "Award")] AwardForm form, CancellationToken ct)
    {
        var result = await _evaluation.AwardAsync(id, new AwardInput(form.SubmissionId, form.CommitteeReference, form.DecisionDateLocal, form.Rationale),
            _userManager.GetUserId(User)!, ct);
        return await AfterAsync(id, result, "Award.", m => m.Award = form,
            "The BAC decision has been recorded. Every bidder can now see the outcome of their bid.", ct);
    }

    [HttpPost]
    [Authorize(Policy = "OrgAdminOnly")]
    public async Task<IActionResult> Return(int id, [Bind(Prefix = "Return")] ReturnForm form, CancellationToken ct)
    {
        var result = await _evaluation.ReturnToBecAsync(id, form.ReturnReason, _userManager.GetUserId(User)!, ct);
        return await AfterAsync(id, result, "Return.", m => m.Return = form,
            "The evaluation has been returned to the BEC with your note. The scoresheet is unlocked.", ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<IActionResult> AfterAsync(int tenderId, ServiceResult result, string prefix, Action<ScoresheetPageViewModel> keepForm,
        string success, CancellationToken ct)
    {
        if (result.NotFound) return NotFound();
        if (result.Succeeded)
        {
            TempData["Flash"] = success;
            return RedirectToAction(nameof(Tender), new { id = tenderId });
        }

        var model = await PageAsync(tenderId, ct);
        if (model is null) return NotFound();
        keepForm(model);
        AddErrors(result, prefix);
        return View(nameof(Tender), model);
    }

    private void AddErrors(ServiceResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
            ModelState.AddModelError(field.Length == 0 ? string.Empty : prefix + field, message);
    }

    private async Task<ScoresheetPageViewModel?> PageAsync(int tenderId, CancellationToken ct)
    {
        var sheet = await _evaluation.GetScoresheetAsync(tenderId, ct);
        if (sheet is null) return null;
        return new ScoresheetPageViewModel
        {
            Sheet = sheet,
            IsBecMember = User.IsInRole(AppRoles.Evaluator),
            IsScmOfficer = User.IsInRole(AppRoles.OrgAdmin),
            // Nothing is pre-selected: the BEC actively chooses the bid it recommends. The ranking is a guide.
            Recommend = new RecommendForm(),
            Award = new AwardForm { SubmissionId = sheet.Recommended?.SubmissionId, DecisionDateLocal = Infrastructure.SaTime.ToSast(DateTime.UtcNow).Date }
        };
    }

    private async Task<EvaluateBidViewModel?> BidPageAsync(int submissionId, CancellationToken ct)
    {
        var s = await _db.Submissions.AsNoTracking()
            .Include(x => x.Tender).ThenInclude(t => t.Requirements)
            .Include(x => x.Tender).ThenInclude(t => t.FunctionalityCriteria)
            .Include(x => x.Company)
            .Include(x => x.Documents)
            .Include(x => x.Evaluation).ThenInclude(e => e!.EvaluatedByUser)
            .Include(x => x.Evaluation).ThenInclude(e => e!.FunctionalityRatings)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == submissionId, ct);
        if (s is null) return null;

        var stage = EvaluationService.StageOf(s.Tender, DateTime.UtcNow);
        var names = s.Tender.Requirements.ToDictionary(r => r.Id, r => r.Name);
        var sealedBid = stage == EvaluationStage.NotClosed;
        IReadOnlyList<CompanyDocument> trackRecord = sealedBid ? Array.Empty<CompanyDocument>() : await _trackRecord.HeldAtAsync(s.CompanyId, s.Tender.ClosingDateUtc, ct);
        return new EvaluateBidViewModel
        {
            SubmissionId = s.Id,
            ReferenceNumber = s.ReferenceNumber,
            TenderId = s.TenderId,
            TenderReference = s.Tender.ReferenceNumber,
            TenderTitle = s.Tender.Title,
            PointSystem = s.Tender.PointSystem,
            Stage = stage,
            CompanyName = stage == EvaluationStage.NotClosed ? "Sealed bid" : s.Company.Name,
            DeclaredLevel = s.DeclaredBbbeeLevel,
            RedFlags = stage == EvaluationStage.NotClosed ? Array.Empty<string>() : SubmissionStatuses.RedFlags(s),
            Documents = stage == EvaluationStage.NotClosed ? Array.Empty<(int, string, string)>()
                : s.Documents
                    .Select(d => (d.Id, Name: d.TenderRequirementId is int r && names.TryGetValue(r, out var n) ? n : "Supporting document", d.OriginalFileName, d.UploadedAtUtc))
                    // The technical proposal (the bidder's pitch) first: it is what functionality is scored on.
                    .OrderByDescending(d => TenderCatalog.IsProposal(d.Name)).ThenBy(d => d.UploadedAtUtc)
                    .Select(d => (d.Id, TenderCatalog.IsProposal(d.Name) ? d.Name + ": the bidder's pitch" : d.Name, d.OriginalFileName)).ToList(),
            TrackRecord = trackRecord.Select(d => new EvaluateBidViewModel.TrackRecordRow(d.Id, d.Title, TrackRecordService.Label(d.Kind),
                TrackRecordService.Describe(d), d.OriginalFileName, d.UploadedAtUtc)).ToList(),
            FunctionalityThreshold = s.Tender.FunctionalityThreshold,
            Criteria = s.Tender.FunctionalityCriteria.OrderBy(c => c.SortOrder).Select(c => (c.Id, c.Name, c.Weight)).ToList(),
            FunctionalityScore = s.Evaluation?.FunctionalityScore,
            EvaluatedBy = s.Evaluation?.EvaluatedByUser.FullName,
            EvaluatedAtUtc = s.Evaluation?.EvaluatedAtUtc,
            CanEdit = stage == EvaluationStage.Evaluating && User.IsInRole(AppRoles.Evaluator),
            Form = new EvaluateBidForm
            {
                IsResponsive = s.Evaluation?.IsResponsive,
                NonResponsiveReason = s.Evaluation?.NonResponsiveReason,
                BidPrice = s.Evaluation?.BidPrice,
                Notes = s.Evaluation?.Notes,
                Ratings = s.Evaluation?.FunctionalityRatings.ToDictionary(r => r.TenderFunctionalityCriterionId, r => (int?)r.Rating) ?? new()
            }
        };
    }
}
