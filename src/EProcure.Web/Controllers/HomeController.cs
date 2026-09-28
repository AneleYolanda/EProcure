using System.Diagnostics;
using EProcure.Web.Domain;
using EProcure.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// "/" sends each kind of visitor to their own starting screen:
    /// signed out → splash (design "sSplash"), supplier → tender feed, organisation staff → console.
    /// </summary>
    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View("Splash");
        }

        if (User.IsInRole(AppRoles.OrgAdmin) || User.IsInRole(AppRoles.Evaluator))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        return RedirectToAction("Index", "Dashboard", new { area = "Supplier" });
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

    /// <summary>
    /// Friendly page for empty error responses (UseStatusCodePagesWithReExecute), e.g. 404 from another
    /// organisation's id. The original status code is kept, and the page never says WHY something was not
    /// found, so it does not reveal whether a record exists in another organisation.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult Status(int id) => View("StatusPage", id);
}
