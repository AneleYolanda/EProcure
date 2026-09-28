using EProcure.Web.Domain;
using EProcure.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// "Approval queue": tenders waiting for a second SCM Officer's approval before publication, and the organisation's
/// rule that switches this four-eyes control on or off. SCM Officers only; the decisions themselves are taken on the
/// tender page (TendersController), where the approver sees the full tender.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgAdminOnly")]
public class ApprovalsController : Controller
{
    private readonly ITenderService _tenders;
    private readonly UserManager<ApplicationUser> _userManager;

    public ApprovalsController(ITenderService tenders, UserManager<ApplicationUser> userManager)
    {
        _tenders = tenders;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["UserId"] = _userManager.GetUserId(User);
        ViewData["RequiresApproval"] = await _tenders.RequiresApprovalAsync(ct);
        return View(await _tenders.ApprovalQueueAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Rule(bool required, CancellationToken ct)
    {
        var result = await _tenders.SetApprovalRuleAsync(required, ct);
        if (result.NotFound) return NotFound();
        TempData["Flash"] = required
            ? "From now on a tender is published only after a second SCM Officer approves it."
            : "SCM Officers can now publish tenders without a second approval. Requests already waiting can still be approved.";
        return RedirectToAction(nameof(Index));
    }
}
