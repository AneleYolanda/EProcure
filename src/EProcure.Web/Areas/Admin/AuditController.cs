using EProcure.Web.Data;
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

    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        var total = await _db.AuditEntries.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var entries = await _db.AuditEntries.AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync(ct);
        var userIds = entries.Where(e => e.UserId != null).Select(e => e.UserId!).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return View(new AuditPageViewModel
        {
            Page = page,
            TotalPages = pages,
            Rows = entries.Select(e => new AuditRow(e.OccurredAtUtc, e.Action, e.EntityType, e.EntityId,
                e.UserId != null && names.TryGetValue(e.UserId, out var n) ? n : e.UserEmail ?? "System", e.Details, e.IpAddress)).ToList()
        });
    }
}
