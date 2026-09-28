using System.Text;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.Tenancy;
using EProcure.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

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
    private readonly INotificationService _notifications;
    private readonly ILinkBuilder _links;
    private readonly IEmailSender _emailSender;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        EProcureDbContext db,
        IAuditService audit,
        IOtpSender otpSender,
        INotificationService notifications,
        ILinkBuilder links,
        IEmailSender emailSender)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _db = db;
        _audit = audit;
        _otpSender = otpSender;
        _notifications = notifications;
        _links = links;
        _emailSender = emailSender;
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
            // A safe (local) return address wins, e.g. the page that sent them to sign in.
            if (returnUrl is not null) return LocalRedirect(returnUrl);

            // Otherwise route by role to the right area.
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

        // Normalise the SA cellphone to +27XXXXXXXXX (spaces removed) so it is stored one way only.
        var digitsOnly = model.PhoneNumber.Replace(" ", string.Empty).Trim();
        var normalizedPhone = digitsOnly.StartsWith("0") ? "+27" + digitsOnly[1..] : digitsOnly;

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
    public async Task<IActionResult> VerifyPhone()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        if (user.PhoneNumberConfirmed) return RedirectToAction("Index", "Dashboard", new { area = "Supplier" });

        return View(BuildVerifyModel(user, new VerifyPhoneViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> VerifyPhone(VerifyPhoneViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        // A supplier always has a phone number from registration; without one there is nothing to verify.
        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            ModelState.AddModelError(string.Empty, "No phone number on file.");
            return View(BuildVerifyModel(user, model));
        }

        if (model.Code.Length != 6 || !model.Code.All(char.IsDigit))
        {
            ModelState.AddModelError(string.Empty, "Enter all 6 digits of the code.");
            return View(BuildVerifyModel(user, model));
        }

        var result = await _userManager.ChangePhoneNumberAsync(user, user.PhoneNumber, model.Code);
        if (result.Succeeded)
        {
            TempData.Remove(MockOtpSender.TempDataKey);
            await _audit.LogAsync("Account.PhoneVerified", "ApplicationUser", user.Id, null);
            await _signInManager.RefreshSignInAsync(user);

            // Design flow: registration (step 1 of 2) → company profile (step 2 of 2), unless they have one.
            var hasCompany = await _db.SupplierProfiles.AnyAsync(p => p.UserId == user.Id && p.CompanyId != null);
            return hasCompany
                ? RedirectToAction("Index", "Dashboard", new { area = "Supplier" })
                : RedirectToAction("Create", "Company", new { area = "Supplier", welcome = true });
        }

        ModelState.AddModelError(string.Empty, "That code is incorrect or has expired.");
        return View(BuildVerifyModel(user, model));
    }

    /// <summary>Adds the display-only parts of the Verify phone page (number, demo code).</summary>
    private VerifyPhoneViewModel BuildVerifyModel(ApplicationUser user, VerifyPhoneViewModel model)
    {
        model.PhoneDisplay = DisplayFormat.Cellphone(user.PhoneNumber);
        // Peek keeps the demo code for the next request too, so it stays visible after a wrong attempt.
        model.DemoCode = TempData.Peek(MockOtpSender.TempDataKey) as string;
        model.Digits = Array.Empty<string>(); // never echo digits back into the boxes
        return model;
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

    // ------------------------------------------------------------------ password reset and staff invitations

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    /// <summary>
    /// Always shows the same "check your email" message, so the page cannot be used to find out which addresses
    /// have accounts. A link is only emailed to an existing, active account.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not null && StaffAccounts.IsActive(user) && user.Email is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = _links.Absolute($"/Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&code={Encode(token)}");
            await _notifications.PasswordResetRequestedAsync(user, link, ct);
            await _audit.LogAsync("Account.PasswordResetRequested", "ApplicationUser", user.Id, user.OrganisationId);
        }

        return View(new ForgotPasswordViewModel { Sent = true, Email = model.Email, ShowDemoMailbox = DemoMailboxAvailable });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? userId, string? code) =>
        string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code)
            ? LinkProblem()
            : View("SetPassword", new SetPasswordViewModel { UserId = userId, Code = code });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public Task<IActionResult> ResetPassword(SetPasswordViewModel model, CancellationToken ct) => SetPasswordAsync(model, invitation: false, ct);

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvite(string? userId, string? code)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code)) return LinkProblem();
        var user = await _userManager.FindByIdAsync(userId);
        return View("SetPassword", new SetPasswordViewModel
        {
            UserId = userId, Code = code, IsInvitation = true,
            Greeting = user is null ? null : $"Welcome, {user.FullName}. Choose the password you will use to sign in to your organisation's eProcure workspace."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public Task<IActionResult> AcceptInvite(SetPasswordViewModel model, CancellationToken ct) => SetPasswordAsync(model, invitation: true, ct);

    /// <summary>
    /// Sets the password from an emailed link. The token is checked by Identity (signed, expires, and stops working
    /// once used because the password change updates the security stamp). A wrong or expired link gets the same
    /// message whether or not the account exists.
    /// </summary>
    private async Task<IActionResult> SetPasswordAsync(SetPasswordViewModel model, bool invitation, CancellationToken ct)
    {
        model.IsInvitation = invitation;
        if (!ModelState.IsValid) return View("SetPassword", model);

        var user = await _userManager.FindByIdAsync(model.UserId);
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code)); }
        catch (FormatException) { return LinkProblem(); }
        if (user is null || StaffAccounts.IsDeactivated(user)) return LinkProblem();

        IdentityResult result;
        if (invitation)
        {
            var valid = await _userManager.VerifyUserTokenAsync(user, InviteTokenProvider<ApplicationUser>.ProviderName, InviteTokenProvider<ApplicationUser>.Purpose, token);
            if (!valid || !StaffAccounts.IsInvited(user)) return LinkProblem();
            result = await _userManager.AddPasswordAsync(user, model.Password);
        }
        else
        {
            result = await _userManager.ResetPasswordAsync(user, token, model.Password);
            if (!result.Succeeded && result.Errors.Any(e => e.Code == "InvalidToken")) return LinkProblem();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(nameof(model.Password), error.Description);
            return View("SetPassword", model);
        }

        // Opening the emailed link proves the address works.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }
        await _userManager.UpdateSecurityStampAsync(user); // ends any other open sessions
        await _audit.LogAsync(invitation ? "Staff.InvitationAccepted" : "Account.PasswordReset", "ApplicationUser", user.Id, user.OrganisationId);
        if (!invitation) await _notifications.PasswordChangedAsync(user, ct);

        TempData["Flash"] = invitation ? "Your password is set. Sign in to your organisation's workspace." : "Your password has been changed. Sign in with the new password.";
        return RedirectToAction(nameof(Login));
    }

    private IActionResult LinkProblem()
    {
        TempData["FlashError"] = "That link is not valid or has expired. Links work once; ask for a new one.";
        return RedirectToAction(nameof(ForgotPassword));
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private bool DemoMailboxAvailable =>
        HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() && _emailSender is MockEmailSender;

    private bool IsLocalReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);
    }
}
