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

    public void Dispose() => _db.Dispose();

    private TenderService ServiceFor(TestTenant tenant, out Web.Data.EProcureDbContext context)
    {
        context = _db.Context(tenant);
        return new TenderService(context, tenant, new AuditService(context, new HttpContextAccessor()));
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
}
