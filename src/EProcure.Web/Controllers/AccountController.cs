using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Controllers;

/// <summary>
/// Minimal account controller providing login, register (suppliers only), logout and access denied.
/// We avoid the scaffolded Identity UI to keep full control of the views and behaviour.
/// </summary>
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly EProcureDbContext _db;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        EProcureDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _db = db;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        // Generic error message to avoid user enumeration.
        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Determine role to route the user to the appropriate area.
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                if (await _userManager.IsInRoleAsync(user, AppRoles.Supplier))
                {
                    return LocalRedirect(returnUrl ?? "/Supplier/Dashboard");
                }
                if (await _userManager.IsInRoleAsync(user, AppRoles.OrgAdmin) || await _userManager.IsInRoleAsync(user, AppRoles.Evaluator))
                {
                    return LocalRedirect(returnUrl ?? "/Admin/Dashboard");
                }
            }

            // Fallback to home.
            return LocalRedirect(returnUrl ?? "/");
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            CreatedAtUtc = DateTime.UtcNow
        };

        var create = await _userManager.CreateAsync(user, model.Password);
        if (!create.Succeeded)
        {
            foreach (var err in create.Errors) ModelState.AddModelError(string.Empty, err.Description);
            return View(model);
        }

        // Ensure the Supplier role exists and add the user to it.
        if (!await _roleManager.RoleExistsAsync(AppRoles.Supplier))
        {
            await _roleManager.CreateAsync(new ApplicationRole(AppRoles.Supplier));
        }
        await _userManager.AddToRoleAsync(user, AppRoles.Supplier);

        // Create the supplier profile and record POPIA consent timestamp.
        var profile = new SupplierProfile
        {
            UserId = user.Id,
            PopiaConsentAtUtc = model.PopiaConsent ? DateTime.UtcNow : null,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.SupplierProfiles.Add(profile);
        await _db.SaveChangesAsync();

        // Sign in the new user.
        await _signInManager.SignInAsync(user, isPersistent: false);

        return RedirectToAction("Dashboard", "Supplier");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
