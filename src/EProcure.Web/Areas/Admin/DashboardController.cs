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

    public DashboardController(EProcureDbContext db, ICurrentOrganisation currentOrganisation)
    {
        _db = db;
        _currentOrganisation = currentOrganisation;
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
                Applications = t.Submissions.Count() // only paid submissions: the filter hides unpaid ones
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
            actions.AddRange(drafts.Select(t => new ActionItem(t.Id,
                $"{t.ReferenceNumber} is still a draft", $"{t.Title} · not visible to suppliers", "Draft", "neutral", "file")));
            actions.AddRange(closingSoon.Select(t => new ActionItem(t.Id,
                $"{t.ReferenceNumber} closes {DisplayFormat.DateTime(t.ClosingDateUtc)}",
                $"{t.Title} · {Plural(t.Applications, "application")} so far", "Closing soon", "warning", "clock")));
        }
        actions.AddRange(closed.Select(t => new ActionItem(t.Id,
            $"{t.ReferenceNumber} has closed",
            $"{t.Title} · {Plural(t.Applications, "application")} to evaluate", "Evaluate", "info", "inbox")));

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
