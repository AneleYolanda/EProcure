using EProcure.Tests.Support;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Tests.Services;

/// <summary>Tender rules for an organisation's staff: own organisation only, unique references, closing date, locking.</summary>
public sealed class TenderServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly EProcure.Web.Services.External.MockEmailSender _mail = new();

    public void Dispose() => _db.Dispose();

    private TenderService ServiceFor(TestTenant tenant, out Web.Data.EProcureDbContext context)
    {
        context = _db.Context(tenant);
        return new TenderService(context, tenant, new AuditService(context, new HttpContextAccessor()), Notifications.Into(_mail, context));
    }

    private static TenderFormViewModel Form(string reference = "RBIDZ/2026/100", double closesInDays = 14) => new()
    {
        Title = "Cleaning services",
        ReferenceNumber = reference,
        Category = TenderCatalog.Categories[0],
        Description = "Cleaning of offices",
        ClosingDateLocal = SaTime.ToSast(DateTime.UtcNow.AddDays(closesInDays)),
        TenderFee = 0m,
        MinimumBbbeeLevel = BbbeeLevel.Level4,
        SelectedDocuments = new() { TenderCatalog.StandardDocuments[0] }
    };

    [Fact]
    public async Task New_tender_belongs_to_the_signed_in_users_organisation_and_starts_as_draft()
    {
        var service = ServiceFor(TestTenant.Org(TestDb.Mvlm), out var context);
        await using var _ = context;

        var result = await service.CreateAsync(Form(), _db.AddUser(TestDb.Mvlm), _ct);

        Assert.True(result.Succeeded);
        var tender = await context.Tenders.SingleAsync(t => t.Id == result.Id);
        Assert.Equal(TestDb.Mvlm, tender.OrganisationId);
        Assert.Equal(TenderStatus.Draft, tender.Status);
        Assert.True(await context.AuditEntries.AnyAsync(a => a.Action == "Tender.Created" && a.OrganisationId == TestDb.Mvlm));
    }

    [Fact]
    public async Task A_user_without_an_organisation_cannot_create_tenders()
    {
        var service = ServiceFor(TestTenant.Marketplace, out var context);
        await using var _ = context;

        Assert.True((await service.CreateAsync(Form(), _db.AddUser(), _ct)).NotFound);
    }

    [Fact]
    public async Task Staff_cannot_publish_edit_or_cancel_another_organisations_tender()
    {
        var mvlmTender = _db.AddTender(TestDb.Mvlm, TestDb.InDays(10), status: TenderStatus.Draft);
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        Assert.True((await service.PublishAsync(mvlmTender, _ct)).NotFound);
        Assert.True((await service.UpdateDraftAsync(mvlmTender, Form(), _ct)).NotFound);
        Assert.True((await service.CancelAsync(mvlmTender, "not mine", _ct)).NotFound);

        await using var check = _db.Marketplace();
        Assert.Equal(TenderStatus.Draft, (await check.Tenders.SingleAsync(t => t.Id == mvlmTender)).Status);
    }

    [Fact]
    public async Task Reference_must_be_unique_within_an_organisation_but_not_across_organisations()
    {
        var rbidz = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var rbidzContext);
        var mvlm = ServiceFor(TestTenant.Org(TestDb.Mvlm), out var mvlmContext);
        await using var a = rbidzContext;
        await using var b = mvlmContext;

        Assert.True((await rbidz.CreateAsync(Form("SHARED/001"), _db.AddUser(TestDb.Rbidz), _ct)).Succeeded);
        var duplicate = await rbidz.CreateAsync(Form("SHARED/001"), _db.AddUser(TestDb.Rbidz), _ct);
        var otherOrg = await mvlm.CreateAsync(Form("SHARED/001"), _db.AddUser(TestDb.Mvlm), _ct);

        Assert.Equal(nameof(TenderFormViewModel.ReferenceNumber), duplicate.Errors.Single().Field);
        Assert.True(otherOrg.Succeeded);
    }

    [Fact]
    public async Task Closing_date_must_be_at_least_an_hour_away()
    {
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        var result = await service.CreateAsync(Form(closesInDays: 0.01), _db.AddUser(TestDb.Rbidz), _ct);

        Assert.Contains(result.Errors, e => e.Field == nameof(TenderFormViewModel.ClosingDateLocal));
    }

    [Fact]
    public async Task A_tender_needs_at_least_one_required_document()
    {
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;
        var form = Form();
        form.SelectedDocuments.Clear();

        var result = await service.CreateAsync(form, _db.AddUser(TestDb.Rbidz), _ct);

        Assert.Contains(result.Errors, e => e.Field == nameof(TenderFormViewModel.SelectedDocuments));
    }

    [Fact]
    public async Task A_draft_whose_closing_date_has_passed_cannot_be_published()
    {
        var draft = _db.AddTender(TestDb.Rbidz, TestDb.InDays(-1), status: TenderStatus.Draft);
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        var result = await service.PublishAsync(draft, _ct);

        Assert.False(result.Succeeded);
        Assert.Equal(TenderStatus.Draft, (await context.Tenders.SingleAsync(t => t.Id == draft)).Status);
    }

    [Fact]
    public async Task A_published_tender_is_locked_against_editing()
    {
        var published = _db.AddTender(TestDb.Rbidz, TestDb.InDays(10));
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        var result = await service.UpdateDraftAsync(published, Form(), _ct);

        Assert.Contains("Only draft tenders can be edited", result.Errors.Single().Message);
    }

    [Fact]
    public async Task Cancelling_keeps_the_tender_on_record_with_its_reason()
    {
        var published = _db.AddTender(TestDb.Rbidz, TestDb.InDays(10));
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        Assert.True((await service.CancelAsync(published, "  Budget withdrawn  ", _ct)).Succeeded);

        var tender = await context.Tenders.SingleAsync(t => t.Id == published);
        Assert.Equal(TenderStatus.Cancelled, tender.Status);
        Assert.Equal("Budget withdrawn", tender.CancellationReason);
    }

    // ------------------------------------------------------------------ functionality stage

    private static TenderFormViewModel WithFunctionality(int? threshold, params (string? Name, int? Weight)[] rows)
    {
        var form = Form();
        form.UseFunctionality = true;
        form.FunctionalityThreshold = threshold;
        form.FunctionalityCriteria = rows.Select(r => new TenderFormViewModel.CriterionInput { Name = r.Name, Weight = r.Weight }).ToList();
        return form;
    }

    [Fact]
    public async Task Functionality_criteria_and_threshold_are_saved_with_the_tender()
    {
        var user = _db.AddUser(TestDb.Rbidz);
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        var result = await service.CreateAsync(WithFunctionality(70, ("Experience", 60), ("Methodology", 40), ("", null)), user, _ct);

        Assert.True(result.Succeeded);
        var tender = await context.Tenders.Include(t => t.FunctionalityCriteria).SingleAsync(t => t.Id == result.Id);
        Assert.Equal(70, tender.FunctionalityThreshold);
        Assert.Equal(new[] { ("Experience", 60), ("Methodology", 40) },
            tender.FunctionalityCriteria.OrderBy(c => c.SortOrder).Select(c => (c.Name, c.Weight)));
    }

    [Theory]
    [InlineData(70, 60, 30, "add up to 100")]
    [InlineData(null, 60, 40, "")]         // threshold missing: error on the threshold field
    [InlineData(101, 60, 40, "")]
    [InlineData(70, 0, 100, "weight from 1 to 100")]
    public async Task Functionality_is_checked(int? threshold, int first, int second, string message)
    {
        var user = _db.AddUser(TestDb.Rbidz);
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        var result = await service.CreateAsync(WithFunctionality(threshold, ("Experience", first), ("Methodology", second)), user, _ct);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => message.Length == 0 ? e.Field == "FunctionalityThreshold" : e.Message.Contains(message));
        Assert.Equal(0, await context.Tenders.CountAsync());
    }

    [Fact]
    public async Task A_weighted_row_needs_a_name_and_names_must_differ()
    {
        var user = _db.AddUser(TestDb.Rbidz);
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;

        var unnamed = await service.CreateAsync(WithFunctionality(70, ("Experience", 60), (" ", 40)), user, _ct);
        var twice = await service.CreateAsync(WithFunctionality(70, ("Experience", 50), ("experience", 50)), user, _ct);

        Assert.Contains("needs a criterion name", unnamed.Errors.Single().Message);
        Assert.Contains("different name", twice.Errors.Single().Message);
    }

    [Fact]
    public async Task Unticking_functionality_removes_the_stage_from_a_draft()
    {
        var user = _db.AddUser(TestDb.Rbidz);
        var service = ServiceFor(TestTenant.Org(TestDb.Rbidz), out var context);
        await using var _ = context;
        var created = await service.CreateAsync(WithFunctionality(70, ("Experience", 100)), user, _ct);

        var form = WithFunctionality(70, ("Experience", 100));
        form.UseFunctionality = false;
        Assert.True((await service.UpdateDraftAsync(created.Id, form, _ct)).Succeeded);

        var tender = await context.Tenders.Include(t => t.FunctionalityCriteria).SingleAsync(t => t.Id == created.Id);
        Assert.Null(tender.FunctionalityThreshold);
        Assert.Empty(tender.FunctionalityCriteria);
        await using var everyone = _db.Marketplace();
        Assert.Equal(0, await everyone.TenderFunctionalityCriteria.CountAsync());
    }
}
