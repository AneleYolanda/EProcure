using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using EProcure.Web.Domain;
using EProcure.Web.Tenancy;

namespace EProcure.Web.Infrastructure;

/// <summary>
/// When a user signs in, this factory adds tenant claims for the user's organisation.
/// Specifically, it adds the claim "eprocure:org_id" with the OrganisationId value if present.
/// This keeps tenant resolution robust and centralised.
/// </summary>
public class OrganisationClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public OrganisationClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        // Call the base implementation to get the standard identity (including role claims).
        var identity = await base.GenerateClaimsAsync(user);

        // If the user has an organisation id, add it as a claim. We store the integer as a string.
        if (user.OrganisationId != null)
        {
            var existingClaim = identity.FindFirst(TenantClaimTypes.OrganisationId);
            if (existingClaim is not null)
            {
                identity.RemoveClaim(existingClaim);
            }
            identity.AddClaim(new Claim(TenantClaimTypes.OrganisationId, user.OrganisationId.Value.ToString()));
        }

        return identity;
    }
}
