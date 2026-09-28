using System.Security.Claims;
using EProcure.Web.Domain;

namespace EProcure.Web.Infrastructure;

/// <summary>
/// The design speaks in public-sector job titles; our security roles are shorter.
/// These are DISPLAY names only: authorisation always uses the real roles (OrgAdmin, Evaluator).
/// </summary>
public static class RoleLabels
{
    public const string OrgAdmin = "SCM Officer";
    public const string Evaluator = "BEC member";

    public static string For(ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.OrgAdmin) ? OrgAdmin
        : user.IsInRole(AppRoles.Evaluator) ? Evaluator
        : "Supplier";
}
