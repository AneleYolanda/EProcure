using EProcure.Web.Data;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// The organisation's audit trail, newest first. The tenant filter on AuditLog limits it to entries carrying
/// this organisation's id; suppliers' private actions (drafts, uploads before submission) have no organisation
/// id and are therefore not shown here.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgStaff")]
public class AuditController : Controller
{
    private const int PageSize = 50;
    private readonly EProcureDbContext _db;

    public AuditController(EProcureDbContext db) => _db = db;

    // GET /Admin/Audit?q=award&page=2
    public async Task<IActionResult> Index(int page = 1, string? q = null, CancellationToken ct = default)
    {
        var term = SearchRules.Term(q);
        var query = _db.AuditEntries.AsNoTracking();
        if (term is not null)
        {
            // Searches what the page shows: the action, the record, the details, and who did it (name or email).
            var people = _db.Users.Where(u => u.FullName.Contains(term)).Select(u => u.Id);
            query = query.Where(a => a.Action.Contains(term) || a.EntityId.Contains(term)
                || (a.Details != null && a.Details.Contains(term))
                || (a.UserEmail != null && a.UserEmail.Contains(term))
                || (a.UserId != null && people.Contains(a.UserId)));
        }

        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var entries = await query
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync(ct);
        var userIds = entries.Where(e => e.UserId != null).Select(e => e.UserId!).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return View(new AuditPageViewModel
        {
            Page = page,
            TotalPages = pages,
            Query = term,
            Rows = entries.Select(e => new AuditRow(e.OccurredAtUtc, e.Action, e.EntityType, e.EntityId,
                e.UserId != null && names.TryGetValue(e.UserId, out var n) ? n : e.UserEmail ?? "System", e.Details, e.IpAddress)).ToList()
        });
    }
}
