using Microsoft.AspNetCore.Identity;

namespace EProcure.Web.Domain;

/// <summary>
/// The "User" entity. Extends ASP.NET Core Identity's IdentityUser, which already provides
/// Email, PasswordHash (PBKDF2, never the plain password), lockout and security stamp.
/// Stored in the "Users" table.
///
/// Named ApplicationUser (not User) because every MVC controller already has a property
/// called User (the signed-in ClaimsPrincipal); a class with the same name causes confusing code.
///
/// Tenant rule: OrgAdmin / Evaluator users have an OrganisationId. Suppliers have NULL.
/// POPIA: holds personal information (name, email, phone). Access: the user themself;
/// never listed to other organisations or other suppliers.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>NULL for suppliers; set for OrgAdmin and Evaluator users.</summary>
    public int? OrganisationId { get; set; }
    public Organisation? Organisation { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Only suppliers have one (1-to-0..1).</summary>
    public SupplierProfile? SupplierProfile { get; set; }
}

/// <summary>
/// The "Role" entity (table "Roles"). Subclassed so we can add fields later without a
/// breaking change. Role names live in <see cref="AppRoles"/>.
/// </summary>
public class ApplicationRole : IdentityRole
{
    public ApplicationRole() { }
    public ApplicationRole(string roleName) : base(roleName) { }
}

/// <summary>Role names as constants so a typo becomes a compile error, not a security hole.</summary>
public static class AppRoles
{
    public const string Supplier = "Supplier";
    public const string OrgAdmin = "OrgAdmin";
    public const string Evaluator = "Evaluator";

    /// <summary>Roles that are bound to exactly one Organisation.</summary>
    public static readonly string[] OrganisationScoped = { OrgAdmin, Evaluator };
}
