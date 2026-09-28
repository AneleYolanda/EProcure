using EProcure.Web.Domain;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// "Roles and users": the SCM Officer manages the people in their own organisation's workspace.
/// All rules (own organisation only, no self-changes, at least one active SCM Officer) live in StaffService.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgAdminOnly")]
public class StaffController : Controller
{
    private readonly IStaffService _staff;
    private readonly UserManager<ApplicationUser> _userManager;

    public StaffController(IStaffService staff, UserManager<ApplicationUser> userManager)
    {
        _staff = staff;
        _userManager = userManager;
    }

    private string ActorId => _userManager.GetUserId(User)!;

    // GET /Admin/Staff
    public async Task<IActionResult> Index([FromServices] IEmailSender email, [FromServices] IWebHostEnvironment environment, CancellationToken ct)
    {
        var model = await PageAsync(new InviteStaffForm(), ct);
        if (model is null) return NotFound();
        model.ShowDemoMailbox = environment.IsDevelopment() && email is MockEmailSender;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Invite([Bind(Prefix = "Invite")] InviteStaffForm form, CancellationToken ct)
    {
        var result = await _staff.InviteAsync(form.FullName, form.Email, form.Role, ActorId, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            var model = await PageAsync(form, ct);
            if (model is null) return NotFound();
            foreach (var (field, message) in result.Errors)
                ModelState.AddModelError(field.Length == 0 ? string.Empty : $"Invite.{field}", message);
            return View(nameof(Index), model);
        }

        TempData["Flash"] = $"Invitation sent to {form.Email!.Trim()}. They choose their own password from the email (the link lasts 3 days).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Role(string id, string? role, CancellationToken ct) =>
        Done(await _staff.ChangeRoleAsync(id, role, ActorId, ct), "Role changed. It applies within a minute, also to a session that is already open.");

    [HttpPost]
    public async Task<IActionResult> Deactivate(string id, CancellationToken ct) =>
        Done(await _staff.SetActiveAsync(id, active: false, ActorId, ct), "Account deactivated. They are signed out within a minute and cannot sign in. Their past work stays on record.");

    [HttpPost]
    public async Task<IActionResult> Reactivate(string id, CancellationToken ct) =>
        Done(await _staff.SetActiveAsync(id, active: true, ActorId, ct), "Account reactivated. They can sign in again.");

    [HttpPost]
    public async Task<IActionResult> Resend(string id, CancellationToken ct) =>
        Done(await _staff.ResendInviteAsync(id, ActorId, ct), "A new invitation has been sent. Earlier links stop working once they choose a password.");

    private IActionResult Done(ServiceResult result, string success)
    {
        if (result.NotFound) return NotFound();
        if (result.Succeeded) TempData["Flash"] = success;
        else TempData["FlashError"] = string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Index));
    }

    private async Task<StaffPageViewModel?> PageAsync(InviteStaffForm form, CancellationToken ct)
    {
        var people = await _staff.ListAsync(ActorId, ct);
        return people is null ? null : new StaffPageViewModel { People = people, Invite = form };
    }
}
