using EProcure.Tests.Support;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Tests.Data;

/// <summary>
/// The global query filters in EProcureDbContext are the tenant wall. These tests use the real model on a
/// real (SQLite) database and look at the same rows as staff of two different organisations.
/// </summary>
public sealed class TenantIsolationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _rbidzTender;
    private readonly int _mvlmTender;
    private readonly int _rbidzSubmitted;
    private readonly int _rbidzDraft;
    private readonly int _rbidzUnpaid;
    private readonly int _mvlmSubmitted;

    public TenantIsolationTests()
    {
        _rbidzTender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(10));
        _mvlmTender = _db.AddTender(TestDb.Mvlm, TestDb.InDays(10));

        var (userA, companyA) = _db.AddSupplier(BbbeeLevel.Level1);
        var (userB, companyB) = _db.AddSupplier(BbbeeLevel.Level1);
        var (userC, companyC) = _db.AddSupplier(BbbeeLevel.Level1);
        _rbidzSubmitted = _db.AddSubmission(_rbidzTender, companyA, userA, SubmissionStatus.Submitted);
        _rbidzDraft = _db.AddSubmission(_rbidzTender, companyB, userB, SubmissionStatus.Draft);
        _rbidzUnpaid = _db.AddSubmission(_rbidzTender, companyC, userC, SubmissionStatus.AwaitingPayment);
        _mvlmSubmitted = _db.AddSubmission(_mvlmTender, companyA, userA, SubmissionStatus.Submitted);

        using var arrange = _db.Marketplace();
        arrange.AuditEntries.AddRange(
            Audit(TestDb.Rbidz, "Tender.Created"),
            Audit(TestDb.Mvlm, "Tender.Created"),
            Audit(null, "Account.Login"));
        arrange.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Staff_see_only_their_own_organisations_tenders()
    {
        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));

        var tenders = await rbidz.Tenders.ToListAsync();

        Assert.NotEmpty(tenders);
        Assert.All(tenders, t => Assert.Equal(TestDb.Rbidz, t.OrganisationId));
        Assert.Contains(tenders, t => t.Id == _rbidzTender);
    }

    [Fact]
    public async Task Another_organisations_tender_does_not_exist_even_by_id()
    {
        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));

        Assert.Null(await rbidz.Tenders.SingleOrDefaultAsync(x => x.Id == _mvlmTender));
        Assert.Null(await rbidz.Tenders.FindAsync(_mvlmTender));
        Assert.Empty(await rbidz.TenderRequirements.Where(r => r.TenderId == _mvlmTender).ToListAsync());
    }

    [Fact]
    public async Task Staff_see_submitted_applications_for_their_organisation_only()
    {
        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));

        var ids = await rbidz.Submissions.Select(s => s.Id).ToListAsync();

        Assert.Equal(new[] { _rbidzSubmitted }, ids);
    }

    [Fact]
    public async Task Drafts_and_unpaid_applications_are_invisible_to_the_organisation()
    {
        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));

        Assert.Null(await rbidz.Submissions.SingleOrDefaultAsync(s => s.Id == _rbidzDraft));
        Assert.Null(await rbidz.Submissions.SingleOrDefaultAsync(s => s.Id == _rbidzUnpaid));
    }

    [Fact]
    public async Task Documents_and_history_follow_the_same_rule_as_their_application()
    {
        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));

        var documentOwners = await rbidz.UploadedDocuments.Select(d => d.SubmissionId).Distinct().ToListAsync();
        var historyOwners = await rbidz.SubmissionStatusHistory.Select(h => h.SubmissionId).Distinct().ToListAsync();

        Assert.Equal(new[] { _rbidzSubmitted }, documentOwners);
        Assert.Equal(new[] { _rbidzSubmitted }, historyOwners);
    }

    [Fact]
    public async Task The_other_organisation_cannot_open_the_first_ones_documents()
    {
        await using var mvlm = _db.Context(TestTenant.Org(TestDb.Mvlm));

        var visible = await mvlm.UploadedDocuments.Select(d => d.SubmissionId).Distinct().ToListAsync();

        Assert.Equal(new[] { _mvlmSubmitted }, visible);
    }

    [Fact]
    public async Task Audit_trail_shows_only_the_organisations_own_entries()
    {
        await using var mvlm = _db.Context(TestTenant.Org(TestDb.Mvlm));

        var entries = await mvlm.AuditEntries.ToListAsync();

        Assert.Single(entries);
        Assert.Equal(TestDb.Mvlm, entries[0].OrganisationId);
    }

    [Fact]
    public async Task Staff_member_without_an_organisation_sees_nothing_fail_closed()
    {
        await using var broken = _db.Context(TestTenant.Org(null));

        Assert.Empty(await broken.Tenders.ToListAsync());
        Assert.Empty(await broken.Submissions.ToListAsync());
        Assert.Empty(await broken.UploadedDocuments.ToListAsync());
        Assert.Empty(await broken.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task Marketplace_sees_tenders_from_every_organisation()
    {
        await using var marketplace = _db.Marketplace();

        var organisations = await marketplace.Tenders.Select(t => t.OrganisationId).Distinct().ToListAsync();

        Assert.Contains(TestDb.Rbidz, organisations);
        Assert.Contains(TestDb.Mvlm, organisations);
    }

    private static AuditEntry Audit(int? organisationId, string action) => new()
    {
        OccurredAtUtc = DateTime.UtcNow,
        OrganisationId = organisationId,
        Action = action,
        EntityType = "Test",
        EntityId = "1"
    };
}
