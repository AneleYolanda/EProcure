using System.Text;
using EProcure.Tests.Support;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Tests.Services;

/// <summary>"Roles and users" and the emailed password links, with real ASP.NET Core Identity on the test database.</summary>
public sealed class StaffServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly IdentityHarness _rbidz;
    private readonly CancellationToken _ct = CancellationToken.None;

    public StaffServiceTests() => _rbidz = new IdentityHarness(_db, TestTenant.Org(TestDb.Rbidz));

    public void Dispose()
    {
        _rbidz.Dispose();
        _db.Dispose();
    }

    private Task<string> Admin(string email = "scm@rbidz.test") => _rbidz.AddStaffAsync(TestDb.Rbidz, AppRoles.OrgAdmin, email);

    // ------------------------------------------------------------------ inviting

    [Fact]
    public async Task An_invited_person_joins_the_inviters_organisation_without_a_password()
    {
        var actor = await Admin();

        var result = await _rbidz.Staff.InviteAsync("Nomsa Dlamini", "nomsa@rbidz.test", AppRoles.Evaluator, actor, _ct);

        Assert.True(result.Succeeded);
        var user = await _rbidz.Users.FindByEmailAsync("nomsa@rbidz.test");
        Assert.NotNull(user);
        Assert.Equal(TestDb.Rbidz, user!.OrganisationId);
        Assert.True(StaffAccounts.IsInvited(user));
        Assert.True(await _rbidz.Users.IsInRoleAsync(user, AppRoles.Evaluator));
        var email = Assert.Single(_rbidz.Mailbox.Sent).Message;
        Assert.Equal("nomsa@rbidz.test", email.To);
        Assert.StartsWith("/Account/AcceptInvite?userId=", email.LinkUrl);
        Assert.True(await _rbidz.Db.AuditEntries.AnyAsync(a => a.Action == "Staff.Invited" && a.OrganisationId == TestDb.Rbidz));
    }

    [Fact]
    public async Task The_invitation_link_sets_the_password_once()
    {
        var actor = await Admin();
        await _rbidz.Staff.InviteAsync("Nomsa Dlamini", "nomsa@rbidz.test", AppRoles.Evaluator, actor, _ct);
        var user = (await _rbidz.Users.FindByEmailAsync("nomsa@rbidz.test"))!;
        var code = QueryValue(_rbidz.Mailbox.Sent.Single().Message.LinkUrl!, "code");
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        const string provider = InviteTokenProvider<ApplicationUser>.ProviderName, purpose = InviteTokenProvider<ApplicationUser>.Purpose;

        Assert.True(await _rbidz.Users.VerifyUserTokenAsync(user, provider, purpose, token));
        Assert.True((await _rbidz.Users.AddPasswordAsync(user, "MyNewPassword26")).Succeeded);

        Assert.False(StaffAccounts.IsInvited(user));
        Assert.False(await _rbidz.Users.VerifyUserTokenAsync(user, provider, purpose, token)); // used: the security stamp changed
        Assert.False(await _rbidz.Users.VerifyUserTokenAsync(user, "Default", "ResetPassword", token)); // not a reset token
    }

    [Theory]
    [InlineData("", "a@rbidz.test", AppRoles.Evaluator, "FullName")]
    [InlineData("Nomsa Dlamini", "not-an-email", AppRoles.Evaluator, "Email")]
    [InlineData("Nomsa Dlamini", "a@rbidz.test", AppRoles.Supplier, "Role")]
    public async Task Invitations_are_validated(string name, string email, string role, string field)
    {
        var actor = await Admin();

        var result = await _rbidz.Staff.InviteAsync(name, email, role, actor, _ct);

        Assert.Equal(field, result.Errors.Single().Field);
        Assert.Empty(_rbidz.Mailbox.Sent);
    }

    [Fact]
    public async Task An_address_that_already_has_an_account_cannot_be_invited()
    {
        var actor = await Admin();
        var supplier = new ApplicationUser { UserName = "owner@supplier.test", Email = "owner@supplier.test", FullName = "Supplier Owner", CreatedAtUtc = DateTime.UtcNow };
        Assert.True((await _rbidz.Users.CreateAsync(supplier, "SupplierPass2026")).Succeeded);

        var result = await _rbidz.Staff.InviteAsync("Someone", "Owner@Supplier.test", AppRoles.Evaluator, actor, _ct);

        Assert.Equal("Email", result.Errors.Single().Field); // matched regardless of capitals
        Assert.Null(supplier.OrganisationId); // the supplier account is untouched
    }

    // ------------------------------------------------------------------ tenant isolation

    [Fact]
    public async Task The_list_shows_only_the_organisations_own_people()
    {
        var actor = await Admin();
        await _rbidz.AddStaffAsync(TestDb.Rbidz, AppRoles.Evaluator, "bec@rbidz.test");
        await _rbidz.AddStaffAsync(TestDb.Mvlm, AppRoles.OrgAdmin, "scm@mvlm.test");
        _db.AddUser(); // a supplier

        var people = (await _rbidz.Staff.ListAsync(actor, _ct))!;

        Assert.Equal(new[] { "bec@rbidz.test", "scm@rbidz.test" }, people.Select(p => p.Email).OrderBy(e => e));
        Assert.True(people.Single(p => p.Id == actor).IsYou);
    }

    [Fact]
    public async Task Another_organisations_people_cannot_be_changed()
    {
        var actor = await Admin();
        var mvlmAdmin = await _rbidz.AddStaffAsync(TestDb.Mvlm, AppRoles.OrgAdmin, "scm@mvlm.test");

        Assert.True((await _rbidz.Staff.SetActiveAsync(mvlmAdmin, false, actor, _ct)).NotFound);
        Assert.True((await _rbidz.Staff.ChangeRoleAsync(mvlmAdmin, AppRoles.Evaluator, actor, _ct)).NotFound);
        Assert.True((await _rbidz.Staff.ResendInviteAsync(mvlmAdmin, actor, _ct)).NotFound);
    }

    [Fact]
    public async Task Someone_who_is_not_organisation_staff_gets_nothing()
    {
        using var supplierSide = new IdentityHarness(_db, TestTenant.Marketplace);

        Assert.Null(await supplierSide.Staff.ListAsync("anyone", _ct));
        Assert.True((await supplierSide.Staff.InviteAsync("X Y", "x@y.test", AppRoles.OrgAdmin, "anyone", _ct)).NotFound);
    }

    // ------------------------------------------------------------------ roles and deactivation

    [Fact]
    public async Task A_role_can_be_changed_but_not_your_own()
    {
        var actor = await Admin();
        var bec = await _rbidz.AddStaffAsync(TestDb.Rbidz, AppRoles.Evaluator, "bec@rbidz.test");

        Assert.True((await _rbidz.Staff.ChangeRoleAsync(bec, AppRoles.OrgAdmin, actor, _ct)).Succeeded);
        var user = (await _rbidz.Users.FindByIdAsync(bec))!;
        Assert.True(await _rbidz.Users.IsInRoleAsync(user, AppRoles.OrgAdmin));
        Assert.False(await _rbidz.Users.IsInRoleAsync(user, AppRoles.Evaluator));

        Assert.Contains("own role", (await _rbidz.Staff.ChangeRoleAsync(actor, AppRoles.Evaluator, actor, _ct)).Errors.Single().Message);
    }

    [Fact]
    public async Task Deactivating_locks_the_account_and_ends_sessions_and_it_can_be_reversed()
    {
        var actor = await Admin();
        var bec = await _rbidz.AddStaffAsync(TestDb.Rbidz, AppRoles.Evaluator, "bec@rbidz.test");
        var stampBefore = (await _rbidz.Users.FindByIdAsync(bec))!.SecurityStamp;

        Assert.True((await _rbidz.Staff.SetActiveAsync(bec, false, actor, _ct)).Succeeded);
        var user = (await _rbidz.Users.FindByIdAsync(bec))!;
        Assert.True(StaffAccounts.IsDeactivated(user));
        Assert.True(await _rbidz.Users.IsLockedOutAsync(user));
        Assert.NotEqual(stampBefore, user.SecurityStamp);

        Assert.True((await _rbidz.Staff.SetActiveAsync(bec, true, actor, _ct)).Succeeded);
        Assert.False(await _rbidz.Users.IsLockedOutAsync((await _rbidz.Users.FindByIdAsync(bec))!));
        Assert.True(await _rbidz.Db.AuditEntries.AnyAsync(a => a.Action == "Staff.Deactivated"));
    }

    [Fact]
    public async Task You_cannot_deactivate_yourself()
    {
        var actor = await Admin();

        Assert.Contains("your own account", (await _rbidz.Staff.SetActiveAsync(actor, false, actor, _ct)).Errors.Single().Message);
    }

    [Fact]
    public async Task The_last_active_scm_officer_cannot_be_deactivated_or_demoted()
    {
        var onlyAdmin = await Admin();
        var bec = await _rbidz.AddStaffAsync(TestDb.Rbidz, AppRoles.Evaluator, "bec@rbidz.test");

        // (The controller only lets SCM Officers call this; the service still protects the organisation.)
        Assert.Contains("at least one active SCM Officer", (await _rbidz.Staff.SetActiveAsync(onlyAdmin, false, bec, _ct)).Errors.Single().Message);
        Assert.Contains("at least one active SCM Officer", (await _rbidz.Staff.ChangeRoleAsync(onlyAdmin, AppRoles.Evaluator, bec, _ct)).Errors.Single().Message);
    }

    // ------------------------------------------------------------------ password reset (Identity configuration)

    [Fact]
    public async Task A_password_reset_link_works_once()
    {
        var id = await Admin();
        var user = (await _rbidz.Users.FindByIdAsync(id))!;
        var token = await _rbidz.Users.GeneratePasswordResetTokenAsync(user);

        Assert.True((await _rbidz.Users.ResetPasswordAsync(user, token, "BrandNewPassword1")).Succeeded);
        Assert.False((await _rbidz.Users.ResetPasswordAsync(user, token, "AnotherPassword22")).Succeeded);
        Assert.True(await _rbidz.Users.CheckPasswordAsync(user, "BrandNewPassword1"));
    }

    private static string QueryValue(string url, string key) =>
        QueryHelpers.ParseQuery(new Uri("http://x" + url).Query)[key].ToString();
}
