using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Admin;

[Area("Admin")]
[Authorize(Policy = "OrgStaff")]
public class DashboardController : Controller
{
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
        var sevenDaysFromNow = now.AddDays(7);

        var model = new AdminDashboardViewModel
        {
            OrganisationName = organisation.Name,
            OpenTenderCount = await _db.Tenders.CountAsync(t =>
                t.Status == TenderStatus.Published && t.ClosingDateUtc > now, cancellationToken),
            SubmissionCount = await _db.Submissions.CountAsync(cancellationToken),
            ClosingWithinSevenDaysCount = await _db.Tenders.CountAsync(t =>
                t.Status == TenderStatus.Published
                && t.ClosingDateUtc > now
                && t.ClosingDateUtc <= sevenDaysFromNow, cancellationToken)
        };

        return View(model);
    }
}
