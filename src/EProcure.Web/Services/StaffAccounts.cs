using EProcure.Web.Domain;

namespace EProcure.Web.Services;

/// <summary>
/// Account states for organisation staff, built on ASP.NET Core Identity's own fields (no extra columns):
///   - Deactivated: locked out "forever" (LockoutEnd = the maximum date). Sign-in is refused and, because the
///     security stamp is changed at the same time, existing sessions end within a minute.
///   - Invited: no password yet; they choose one from the invitation email.
/// A temporary lockout after 5 wrong passwords (15 minutes) is NOT deactivation.
/// </summary>
public static class StaffAccounts
{
    public static readonly DateTimeOffset DeactivatedUntil = DateTimeOffset.MaxValue;

    public static bool IsDeactivated(ApplicationUser user) => user.LockoutEnd is { } end && end.Year >= 9999;

    public static bool IsActive(ApplicationUser user) => !IsDeactivated(user);

    public static bool IsInvited(ApplicationUser user) => string.IsNullOrEmpty(user.PasswordHash);
}
