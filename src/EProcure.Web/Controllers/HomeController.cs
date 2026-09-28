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
}
