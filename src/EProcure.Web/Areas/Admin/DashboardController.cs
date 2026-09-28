using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EProcure.Web.ViewModels.Admin.AdminDashboardViewModel;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// The console dashboard (design screen "sDash"). Every query below is automatically limited to
/// the signed-in user's organisation by the tenant filters in EProcureDbContext; this controller
/// never adds or removes that filter itself.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgStaff")]
public class DashboardController : Controller
{
    private const int MaxActionItems = 6;

    private readonly EProcureDbContext _db;
    private readonly ICurrentOrganisation _currentOrganisation;
    private readonly Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> _userManager;

    public DashboardController(EProcureDbContext db, ICurrentOrganisation currentOrganisation,
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentOrganisation = currentOrganisation;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var organisation = await _currentOrganisation.GetAsync(cancellationToken);
        if (organisation is null) return NotFound();

        var now = DateTime.UtcNow;
        var inSevenDays = now.AddDays(7);

        // One small query for this organisation's tenders; everything else is worked out in memory.
        var tenders = await _db.Tenders
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.Title,
                t.Status,
                t.ClosingDateUtc,
                t.EvaluationSubmittedAtUtc,
                t.ApprovalRequestedAtUtc,
                t.ApprovalRequestedByUserId,
                Applications = t.Submissions.Count(s => s.Status != SubmissionStatus.Withdrawn) // paid, not withdrawn (the filter hides unpaid ones)
            })
            .ToListAsync(cancellationToken);

        var open = tenders.Where(t => t.Status == TenderStatus.Published && t.ClosingDateUtc > now).ToList();
        var closingSoon = open.Where(t => t.ClosingDateUtc <= inSevenDays).OrderBy(t => t.ClosingDateUtc).ToList();
        var drafts = tenders.Where(t => t.Status == TenderStatus.Draft).ToList();
        // "Closed" = formally closed, or published with the closing date already passed.
        var closed = tenders.Where(t => t.Status == TenderStatus.Closed
                                     || (t.Status == TenderStatus.Published && t.ClosingDateUtc <= now)).ToList();
        var awarded = tenders.Count(t => t.Status == TenderStatus.Awarded);

        // Needs your action: only things that really exist in the data. SCM Officers (OrgAdmin) see
        // drafts and deadlines; BEC members (Evaluator) only see closed tenders ready for evaluation.
        var isOrgAdmin = User.IsInRole(AppRoles.OrgAdmin);
        var actions = new List<ActionItem>();
        if (isOrgAdmin)
        {
            var me = _userManager.GetUserId(User);
            // Four-eyes: a draft waiting for approval is an action for every OTHER SCM Officer.
            actions.AddRange(drafts.Where(t => t.ApprovalRequestedAtUtc is not null && t.ApprovalRequestedByUserId != me).Select(t => new ActionItem(t.Id,
                $"{t.ReferenceNumber} awaits your approval", $"{t.Title} · a second SCM Officer must approve it before publication", "Approve", "warning", "file")));
            actions.AddRange(drafts.Where(t => t.ApprovalRequestedAtUtc is null).Select(t => new ActionItem(t.Id,
                $"{t.ReferenceNumber} is still a draft", $"{t.Title} · not visible to suppliers", "Draft", "neutral", "file")));
            actions.AddRange(closingSoon.Select(t => new ActionItem(t.Id,
                $"{t.ReferenceNumber} closes {DisplayFormat.DateTime(t.ClosingDateUtc)}",
                $"{t.Title} · {Plural(t.Applications, "application")} so far", "Closing soon", "warning", "clock")));
        }
        // Closed tenders with bids: the BEC evaluates; once it has submitted, the SCM Officer records the BAC decision.
        var withBids = closed.Where(t => t.Applications > 0).ToList();
        if (isOrgAdmin)
        {
            actions.AddRange(withBids.Where(t => t.EvaluationSubmittedAtUtc is not null).Select(t => new ActionItem(t.Id,
                $"{t.ReferenceNumber}: BAC decision to record",
                $"{t.Title} · the BEC has submitted its recommendation", "BAC", "warning", "inbox")));
        }
        actions.AddRange(withBids.Where(t => t.EvaluationSubmittedAtUtc is null).Select(t => new ActionItem(t.Id,
            $"{t.ReferenceNumber} has closed",
            $"{t.Title} · {Plural(t.Applications, "bid")} for the BEC to evaluate", "Evaluate", "info", "inbox")));

        var stages = new (string Label, int Count, string Colour)[]
        {
            ("Draft", drafts.Count, "#B9CBDA"),
            ("Advertised", open.Count, "#1CA3EC"),
            ("Under evaluation", closed.Count, "#1B2A4A"),
            ("Awarded", awarded, "#0F7B5A")
        };
        var max = Math.Max(1, stages.Max(s => s.Count));

        var model = new AdminDashboardViewModel
        {
            OrganisationCode = organisation.Code,
            RoleLabel = RoleLabels.For(User),
            OpenTenderCount = open.Count,
            ClosingWithinSevenDaysCount = closingSoon.Count,
            SubmissionCount = tenders.Sum(t => t.Applications),
            DraftCount = drafts.Count,
            ClosedCount = closed.Count,
            AuditEntriesLastSevenDays = await _db.AuditEntries.CountAsync(a => a.OccurredAtUtc >= now.AddDays(-7), cancellationToken),
            ActionItems = actions.Take(MaxActionItems).ToList(),
            Pipeline = stages.Select(s => new PipelineStage(s.Label, s.Count, s.Count * 100 / max, s.Colour)).ToList()
        };

        return View(model);
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
}
