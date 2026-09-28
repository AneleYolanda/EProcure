using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Infrastructure;

public class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions()
    {
        Name = InviteTokenProvider<Domain.ApplicationUser>.ProviderName;
        TokenLifespan = TimeSpan.FromDays(3);
    }
}

/// <summary>
/// Signed, expiring tokens for staff invitations ("choose your password"). Same mechanism as password-reset
/// tokens, but with its own lifetime (3 days instead of 1 hour) and purpose, so an invitation link can never be
/// used as a reset link or the other way round. Like reset tokens, it stops working once the password is set,
/// because that changes the user's security stamp.
/// </summary>
public class InviteTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
{
    public const string ProviderName = "Invite";
    public const string Purpose = "AcceptInvitation";

    public InviteTokenProvider(IDataProtectionProvider dataProtectionProvider, IOptions<InviteTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<TUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
