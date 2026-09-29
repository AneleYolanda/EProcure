using EProcure.Tests.Support;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Tests.Services;

/// <summary>
/// The company's track record: uploaded once by the supplier, checked like bid documents, never truly deleted, and
/// shown to a committee as it stood at the tender's closing date, to that tender's organisation only.
/// </summary>
public sealed class TrackRecordServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly InMemoryFileStorage _files = new();

    public void Dispose() => _db.Dispose();

    private TrackRecordService Service(out Web.Data.EProcureDbContext context)
    {
        context = _db.Marketplace();
        return new TrackRecordService(context, new AuditService(context, new HttpContextAccessor()), _files, Options.Create(new UploadOptions()));
    }

    private static TrackRecordInput Reference(string title = "Resurfacing of 12 km of roads") =>
        new(CompanyDocumentKind.ReferenceLetter, title, "uMhlathuze Municipality", 2024, 1_200_000m);

    [Fact]
    public async Task A_supplier_adds_a_document_to_their_own_company()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var service = Service(out var context);
        await using var _ = context;

        var result = await service.AddAsync(user, Reference(), Upload.Pdf("letter.pdf"), _ct);

        Assert.True(result.Succeeded);
        var document = await context.CompanyDocuments.SingleAsync();
        Assert.Equal(company, document.CompanyId);
        Assert.Equal("letter.pdf", document.OriginalFileName);
        Assert.Equal(64, document.Sha256.Length);
        Assert.True(_files.Files.ContainsKey(document.StorageKey));
        Assert.Single(await service.ListOwnAsync(user, _ct));
    }

    [Fact]
    public async Task The_file_must_be_a_real_pdf_and_the_details_are_checked()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var service = Service(out var context);
        await using var _ = context;

        var fake = await service.AddAsync(user, Reference(), Upload.File("letter.pdf", "application/pdf", "MZ not a pdf"), _ct);
        var noClient = await service.AddAsync(user, Reference() with { ClientName = " " }, Upload.Pdf(), _ct);
        var future = await service.AddAsync(user, Reference() with { YearCompleted = DateTime.UtcNow.Year + 2 }, Upload.Pdf(), _ct);
        var noFile = await service.AddAsync(user, Reference(), null, _ct);

        Assert.Equal("File", fake.Errors.Single().Field);
        Assert.Equal("ClientName", noClient.Errors.Single().Field);
        Assert.Equal("YearCompleted", future.Errors.Single().Field);
        Assert.Equal("File", noFile.Errors.Single().Field);
        Assert.Equal(0, await context.CompanyDocuments.CountAsync());
    }

    [Fact]
    public async Task A_company_profile_needs_no_client()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var service = Service(out var context);
        await using var _ = context;

        var result = await service.AddAsync(user, new TrackRecordInput(CompanyDocumentKind.CompanyProfile, "Company profile 2026", null, null, null), Upload.Pdf(), _ct);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Removing_keeps_the_file_and_only_the_owner_can_remove()
    {
        var (owner, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var (other, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var service = Service(out var context);
        await using var _ = context;
        var added = await service.AddAsync(owner, Reference(), Upload.Pdf(), _ct);

        Assert.True((await service.RemoveAsync(other, added.Id, _ct)).NotFound);
        Assert.Null(await service.FindOwnAsync(other, added.Id, _ct));
        Assert.True((await service.RemoveAsync(owner, added.Id, _ct)).Succeeded);

        var document = await context.CompanyDocuments.SingleAsync();
        Assert.NotNull(document.RemovedAtUtc);
        Assert.True(_files.Files.ContainsKey(document.StorageKey));
        Assert.Empty(await service.ListOwnAsync(owner, _ct));
    }

    [Fact]
    public async Task The_committee_sees_the_track_record_as_it_stood_at_the_closing_date()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var closing = TestDb.InDays(-2);
        var before = _db.AddCompanyDocument(company, user, TestDb.InDays(-10), title: "Held at closing");
        var removedAfter = _db.AddCompanyDocument(company, user, TestDb.InDays(-10), removedAtUtc: TestDb.InDays(-1), title: "Removed after closing");
        _db.AddCompanyDocument(company, user, TestDb.InDays(-10), removedAtUtc: TestDb.InDays(-5), title: "Removed before closing");
        _db.AddCompanyDocument(company, user, TestDb.InDays(-1), title: "Added after closing");
        var service = Service(out var context);
        await using var _ = context;

        var held = await service.HeldAtAsync(company, closing, _ct);

        Assert.Equal(new[] { before, removedAfter }.OrderBy(id => id), held.Select(d => d.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Organisation_staff_see_a_track_record_only_through_a_bid_to_their_own_tender()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        _db.AddCompanyDocument(company, user, TestDb.InDays(-10));
        var (draftUser, draftCompany) = _db.AddSupplier(BbbeeLevel.Level1);
        _db.AddCompanyDocument(draftCompany, draftUser, TestDb.InDays(-10));
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(-2));
        _db.AddSubmission(tender, company, user, SubmissionStatus.Submitted);
        _db.AddSubmission(tender, draftCompany, draftUser, SubmissionStatus.Draft); // never submitted: stays private

        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));
        await using var mvlm = _db.Context(TestTenant.Org(TestDb.Mvlm));

        Assert.Equal(company, (await rbidz.CompanyDocuments.SingleAsync()).CompanyId);
        Assert.Equal(0, await mvlm.CompanyDocuments.CountAsync());
    }
}
