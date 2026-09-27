namespace EProcure.Web.Tenancy;

/// <summary>
/// Answers one question for the current request: "which organisation's data may this user see?"
/// The DbContext reads this to apply tenant filters to every query automatically.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// True for OrgAdmin / Evaluator users. When true, queries are restricted to
    /// <see cref="OrganisationId"/>. False for suppliers and anonymous visitors (marketplace view).
    /// </summary>
    bool IsOrganisationScoped { get; }

    /// <summary>
    /// The user's organisation. If a scoped user somehow has no organisation this is NULL,
    /// and because no row has OrganisationId = NULL they see NOTHING (fail closed, not open).
    /// </summary>
    int? OrganisationId { get; }
}

/// <summary>Name of the claim carrying the user's OrganisationId (added at sign-in in step 2).</summary>
public static class TenantClaimTypes
{
    public const string OrganisationId = "eprocure:org_id";
}
