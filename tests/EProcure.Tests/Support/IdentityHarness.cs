using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EProcure.Tests.Support;

/// <summary>
/// Real ASP.NET Core Identity (UserManager, password rules, token providers) on the test database, as one signed-in
/// user of one organisation, with the real StaffService and an in-memory mailbox.
/// </summary>
public sealed class IdentityHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public UserManager<ApplicationUser> Users { get; }
    public EProcureDbContext Db { get; }
    public MockEmailSender Mailbox { get; } = new();
    public StaffService Staff { get; }

    public IdentityHarness(TestDb testDb, ITenantContext tenant)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(tenant);
        services.AddScoped(_ => testDb.Context(tenant));
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<EProcureDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InviteTokenProvider<ApplicationUser>>(InviteTokenProvider<ApplicationUser>.ProviderName);
        services.Configure<InviteTokenProviderOptions>(_ => { });

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        Users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Db = _scope.ServiceProvider.GetRequiredService<EProcureDbContext>();
        Staff = new StaffService(Users, Db, tenant, new AuditService(Db, new HttpContextAccessor()),
            Notifications.Into(Mailbox, Db), new RelativeLinks());
    }

    /// <summary>A staff member of an organisation with a password, in the given role. Returns the user id.</summary>
    public async Task<string> AddStaffAsync(int organisationId, string role, string email)
    {
        var user = new ApplicationUser { UserName = email, Email = email, FullName = email.Split('@')[0], OrganisationId = organisationId, CreatedAtUtc = DateTime.UtcNow, LockoutEnabled = true };
        var created = await Users.CreateAsync(user, "CorrectHorse2026");
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        await Users.AddToRoleAsync(user, role);
        return user.Id;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
