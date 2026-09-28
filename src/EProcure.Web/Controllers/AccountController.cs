using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.Tenancy;
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
    private readonly IAuditService _audit;
    private readonly IOtpSender _otpSender;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        EProcureDbContext db,
        IAuditService audit,
        IOtpSender otpSender)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _db = db;
        _audit = audit;
        _otpSender = otpSender;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = IsLocalReturnUrl(returnUrl) ? returnUrl : null;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        returnUrl = IsLocalReturnUrl(returnUrl) ? returnUrl : null;
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
                    return RedirectToAction("Index", "Dashboard", new { area = "Supplier" });
                }
                if (await _userManager.IsInRoleAsync(user, AppRoles.OrgAdmin) || await _userManager.IsInRoleAsync(user, AppRoles.Evaluator))
                {
                    return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
                }
            }

            // Fallback to home.
            return Redirect(returnUrl ?? "/");
        }

        await _audit.LogAsync("Account.LoginFailed", "Account", model.Email, null);
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

        // Validate POPIA consent was ticked.
        if (!model.PopiaConsent)
        {
            ModelState.AddModelError(nameof(model.PopiaConsent), "You must accept the POPIA processing notice to register.");
            return View(model);
        }

        // Normalize SA phone number to +27 format.
        var normalizedPhone = model.PhoneNumber.StartsWith("0")
            ? "+27" + model.PhoneNumber.Substring(1)
            : model.PhoneNumber;

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = normalizedPhone,
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
            ContactNumber = normalizedPhone,
            PopiaConsentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.SupplierProfiles.Add(profile);
        await _db.SaveChangesAsync();

        // Generate OTP token and send it.
        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, normalizedPhone);
        await _otpSender.SendAsync(normalizedPhone, code);

        await _audit.LogAsync("Account.Registered", "ApplicationUser", user.Id, null);

        // Sign in the new user.
        await _signInManager.SignInAsync(user, isPersistent: false);

        // Redirect to phone verification.
        return RedirectToAction(nameof(VerifyPhone));
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

    [HttpGet]
    [Authorize]
    public IActionResult VerifyPhone()
    {
        return View(new VerifyPhoneViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> VerifyPhone(VerifyPhoneViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        // A supplier always has a phone number from registration; without one there is nothing to verify.
        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            ModelState.AddModelError(string.Empty, "No phone number on file.");
            return View(model);
        }

        var result = await _userManager.ChangePhoneNumberAsync(user, user.PhoneNumber, model.Code);
        if (result.Succeeded)
        {
            await _audit.LogAsync("Account.PhoneVerified", "ApplicationUser", user.Id, null);
            await _signInManager.RefreshSignInAsync(user);
            return RedirectToAction("Index", "Dashboard", new { area = "Supplier" });
        }

        ModelState.AddModelError(string.Empty, "That code is incorrect or has expired.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ResendCode()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            ModelState.AddModelError(string.Empty, "No phone number on file.");
            return RedirectToAction(nameof(VerifyPhone));
        }

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber);
        await _otpSender.SendAsync(user.PhoneNumber, code);

        return RedirectToAction(nameof(VerifyPhone));
    }

    private bool IsLocalReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);
    }
}
