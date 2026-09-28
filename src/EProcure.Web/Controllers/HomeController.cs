using System.Diagnostics;
using EProcure.Web.Data;
using EProcure.Web.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EProcure.Web.ViewModels.Home;
using EProcure.Web.Models;
using Microsoft.AspNetCore.Authorization;

namespace EProcure.Web.Controllers;

public class HomeController : Controller
{
    private readonly EProcureDbContext _db;

    public HomeController(EProcureDbContext db)
    {
        _db = db;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Show splash page for anonymous users
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return View();
        }

        var now = DateTime.UtcNow;
        var tenders = await _db.Tenders
            .AsNoTracking()
            .Where(t => t.Status == TenderStatus.Published && t.ClosingDateUtc > now)
            .OrderBy(t => t.ClosingDateUtc)
            .Take(6)
            .Select(t => new OpenTenderCardViewModel
            {
                Id = t.Id,
                OrganisationName = t.Organisation.Name,
                OrganisationLogoPath = t.Organisation.LogoPath,
                Title = t.Title,
                ReferenceNumber = t.ReferenceNumber,
                Category = t.Category,
                ClosingDateUtc = t.ClosingDateUtc,
                TenderFee = t.TenderFee
            })
            .ToListAsync(cancellationToken);

        return View(new HomeViewModel { LatestTenders = tenders });
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
