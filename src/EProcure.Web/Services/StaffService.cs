using System.ComponentModel.DataAnnotations;
using System.Text;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Services;

public record StaffMember(string Id, string FullName, string Email, string Role, bool IsActive, bool IsInvited, bool IsYou, DateTime CreatedAtUtc);

/// <summary>
/// "Roles and users": an SCM Officer manages the people in THEIR organisation's workspace.
///
/// Tenant safety: the Identity user table has no query filter (suppliers and every organisation's staff live in it),
/// so EVERY query here is limited to OrganisationId = the signed-in user's organisation, taken from ITenantContext
/// (never from the form). A user id from another organisation is "not found".
/// Guard rails: you cannot change your own role or deactivate yourself, and the organisation always keeps at least
/// one active SCM Officer, so nobody can lock the organisation out of its own workspace.
/// Staff are never deleted (their names stay on evaluations, awards and the audit trail); they are deactivated.
/// </summary>
public interface IStaffService
{
    Task<IReadOnlyList<StaffMember>?> ListAsync(string actorId, CancellationToken ct);
    Task<ServiceResult> InviteAsync(string? fullName, string? email, string? role, string actorId, CancellationToken ct);
    Task<ServiceResult> ChangeRoleAsync(string userId, string? role, string actorId, CancellationToken ct);
    Task<ServiceResult> SetActiveAsync(string userId, bool active, string actorId, CancellationToken ct);
    Task<ServiceResult> ResendInviteAsync(string userId, string actorId, CancellationToken ct);
}

public class StaffService : IStaffService
{
    public static readonly string[] StaffRoles = { AppRoles.OrgAdmin, AppRoles.Evaluator };

    private readonly UserManager<ApplicationUser> _users;
    private readonly EProcureDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly ILinkBuilder _links;

    public StaffService(UserManager<ApplicationUser> users, EProcureDbContext db, ITenantContext tenant, IAuditService audit,
        INotificationService notifications, ILinkBuilder links)
    {
        _users = users;
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _notifications = notifications;
        _links = links;
    }

    public static string Label(string role) => role == AppRoles.OrgAdmin ? RoleLabels.OrgAdmin : RoleLabels.Evaluator;

    public async Task<IReadOnlyList<StaffMember>?> ListAsync(string actorId, CancellationToken ct)
    {
        if (OrganisationId is not int organisationId) return null;
        var people = await _users.Users.Where(u => u.OrganisationId == organisationId).OrderBy(u => u.FullName).ToListAsync(ct);
        var roles = await RolesOfAsync(people.Select(p => p.Id).ToList(), ct);
        return people.Select(p => new StaffMember(p.Id, p.FullName, p.Email ?? string.Empty, roles.GetValueOrDefault(p.Id, AppRoles.Evaluator),
            StaffAccounts.IsActive(p), StaffAccounts.IsInvited(p), p.Id == actorId, p.CreatedAtUtc)).ToList();
    }

    public async Task<ServiceResult> InviteAsync(string? fullName, string? email, string? role, string actorId, CancellationToken ct)
    {
        if (OrganisationId is not int organisationId) return ServiceResult.Missing();

        var result = new ServiceResult();
        var name = fullName?.Trim() ?? string.Empty;
        var address = email?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 100) result.With("FullName", "Enter the person's full name (2 to 100 characters).");
        if (address.Length > 256 || !new EmailAddressAttribute().IsValid(address) || !address.Contains('.'))
            result.With("Email", "Enter a valid work email address.");
        if (role is null || !StaffRoles.Contains(role)) result.With("Role", "Choose SCM Officer or BEC member.");
        if (!result.Succeeded) return result;

        if (await _users.FindByEmailAsync(address) is not null)
            return result.With("Email", "An eProcure account already uses this email address. Use the person's work address for this organisation.");

        var user = new ApplicationUser
        {
            UserName = address,
            Email = address,
            FullName = name,
            OrganisationId = organisationId,
            EmailConfirmed = false, // confirmed when they open the invitation and choose a password
            LockoutEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var created = await _users.CreateAsync(user); // no password: they choose it from the invitation
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors) result.With("Email", error.Description);
            return result;
        }
        await _users.AddToRoleAsync(user, role!);

        await _audit.LogAsync("Staff.Invited", "ApplicationUser", user.Id, organisationId, $"{address} as {Label(role!)}");
        await SendInvitationAsync(user, role!, ct);
        return ServiceResult.Ok(0);
    }

    public async Task<ServiceResult> ResendInviteAsync(string userId, string actorId, CancellationToken ct)
    {
        var user = await FindAsync(userId, ct);
        if (user is null) return ServiceResult.Missing();
        if (!StaffAccounts.IsInvited(user)) return new ServiceResult().With(string.Empty, $"{user.FullName} has already chosen a password.");
        if (StaffAccounts.IsDeactivated(user)) return new ServiceResult().With(string.Empty, $"{user.FullName} is deactivated. Reactivate the account first.");

        var role = (await RolesOfAsync(new List<string> { user.Id }, ct)).GetValueOrDefault(user.Id, AppRoles.Evaluator);
        await _audit.LogAsync("Staff.InvitationResent", "ApplicationUser", user.Id, user.OrganisationId, user.Email);
        await SendInvitationAsync(user, role, ct);
        return ServiceResult.Ok(0);
    }

    public async Task<ServiceResult> ChangeRoleAsync(string userId, string? role, string actorId, CancellationToken ct)
    {
        var user = await FindAsync(userId, ct);
        if (user is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (role is null || !StaffRoles.Contains(role)) return result.With(string.Empty, "Choose SCM Officer or BEC member.");
        if (user.Id == actorId) return result.With(string.Empty, "You cannot change your own role. Ask another SCM Officer.");

        var current = (await RolesOfAsync(new List<string> { user.Id }, ct)).GetValueOrDefault(user.Id, AppRoles.Evaluator);
        if (current == role) return ServiceResult.Ok(0);
        if (current == AppRoles.OrgAdmin && StaffAccounts.IsActive(user) && await ActiveAdminCountAsync(user.OrganisationId!.Value, ct) <= 1)
            return result.With(string.Empty, "The organisation must keep at least one active SCM Officer.");

        await _users.RemoveFromRolesAsync(user, StaffRoles.Where(r => r != role));
        if (!await _users.IsInRoleAsync(user, role)) await _users.AddToRoleAsync(user, role);
        await _users.UpdateSecurityStampAsync(user); // their open sessions pick up the new role within a minute

        await _audit.LogAsync("Staff.RoleChanged", "ApplicationUser", user.Id, user.OrganisationId, $"{user.Email}: {Label(current)} -> {Label(role)}");
        return ServiceResult.Ok(0);
    }

    public async Task<ServiceResult> SetActiveAsync(string userId, bool active, string actorId, CancellationToken ct)
    {
        var user = await FindAsync(userId, ct);
        if (user is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (user.Id == actorId) return result.With(string.Empty, "You cannot deactivate your own account.");
        if (StaffAccounts.IsActive(user) == active) return ServiceResult.Ok(0);

        if (!active)
        {
            var isAdmin = await _users.IsInRoleAsync(user, AppRoles.OrgAdmin);
            if (isAdmin && await ActiveAdminCountAsync(user.OrganisationId!.Value, ct) <= 1)
                return result.With(string.Empty, "The organisation must keep at least one active SCM Officer.");
            await _users.SetLockoutEnabledAsync(user, true);
            await _users.SetLockoutEndDateAsync(user, StaffAccounts.DeactivatedUntil);
            await _users.UpdateSecurityStampAsync(user); // ends their open sessions within a minute
        }
        else
        {
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
        }

        await _audit.LogAsync(active ? "Staff.Reactivated" : "Staff.Deactivated", "ApplicationUser", user.Id, user.OrganisationId, user.Email);
        return ServiceResult.Ok(0);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>NULL (fail closed) unless the signed-in user is organisation staff.</summary>
    private int? OrganisationId => _tenant.IsOrganisationScoped ? _tenant.OrganisationId : null;

    private Task<ApplicationUser?> FindAsync(string userId, CancellationToken ct) =>
        OrganisationId is int organisationId
            ? _users.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganisationId == organisationId, ct)
            : Task.FromResult<ApplicationUser?>(null);

    private async Task<Dictionary<string, string>> RolesOfAsync(List<string> userIds, CancellationToken ct)
    {
        var pairs = await (from ur in _db.UserRoles
                           join r in _db.Roles on ur.RoleId equals r.Id
                           where userIds.Contains(ur.UserId) && StaffRoles.Contains(r.Name!)
                           select new { ur.UserId, r.Name }).ToListAsync(ct);
        // If someone somehow holds both roles, the SCM Officer role is shown (and both are checked for the guard rails).
        return pairs.GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.Any(p => p.Name == AppRoles.OrgAdmin) ? AppRoles.OrgAdmin : AppRoles.Evaluator);
    }

    private async Task<int> ActiveAdminCountAsync(int organisationId, CancellationToken ct)
    {
        var admins = await (from u in _db.Users
                            join ur in _db.UserRoles on u.Id equals ur.UserId
                            join r in _db.Roles on ur.RoleId equals r.Id
                            where r.Name == AppRoles.OrgAdmin && u.OrganisationId == organisationId
                            select u).ToListAsync(ct);
        return admins.Count(StaffAccounts.IsActive);
    }

    private async Task SendInvitationAsync(ApplicationUser user, string role, CancellationToken ct)
    {
        var token = await _users.GenerateUserTokenAsync(user, InviteTokenProvider<ApplicationUser>.ProviderName, InviteTokenProvider<ApplicationUser>.Purpose);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = _links.Absolute($"/Account/AcceptInvite?userId={Uri.EscapeDataString(user.Id)}&code={code}");
        var organisationName = await _db.Organisations.Where(o => o.Id == user.OrganisationId).Select(o => o.Name).SingleAsync(ct);
        await _notifications.StaffInvitedAsync(user, organisationName, Label(role), link, ct);
    }
}
