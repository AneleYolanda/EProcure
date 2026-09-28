using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using EProcure.Web.Domain;
using Microsoft.AspNetCore.Identity;

namespace EProcure.Web.Infrastructure.Filters;

/// <summary>
/// Action filter that redirects suppliers with unverified phone numbers to VerifyPhone.
/// Applied globally to all Supplier area controllers.
/// </summary>
public class EnsurePhoneVerifiedFilter : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _userManager;

    public EnsurePhoneVerifiedFilter(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Only apply to authenticated users in the Supplier area.
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            await next();
            return;
        }

        var appUser = await _userManager.GetUserAsync(user);
        if (appUser == null)
        {
            await next();
            return;
        }

        // If the phone number is already confirmed, proceed normally.
        if (appUser.PhoneNumberConfirmed)
        {
            await next();
            return;
        }

        // Redirect to VerifyPhone unless we're already there.
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var action = context.RouteData.Values["action"]?.ToString() ?? "";

        if (controller.Equals("Account", StringComparison.OrdinalIgnoreCase) &&
            action.Equals("VerifyPhone", StringComparison.OrdinalIgnoreCase))
        {
            // Already on VerifyPhone, proceed.
            await next();
            return;
        }

        // Not verified and not on VerifyPhone; redirect.
        context.Result = new RedirectToActionResult("VerifyPhone", "Account", null);
    }
}
