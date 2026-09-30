using EProcure.Tests.Support;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EProcure.Tests.Services;

/// <summary>
/// Compliance documents on the supplier profile: validity rules, replacing, expiry reminders (30 days, 7 days, expired; each
/// once), attaching to a bid from the profile, and that organisations never read the table. Plus the proposal rule.
/// </summary>
public sealed class ComplianceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly InMemoryFileStorage _files = new();
    private readonly MockEmailSender _mail = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static DateTime Today => SaTime.ToSast(DateTime.UtcNow).Date;

    public void Dispose() => _db.Dispose();

    private ComplianceService Service(out Web.Data.EProcureDbContext context)
    {
        context = _db.Marketplace();
        return new ComplianceService(context, new AuditService(context, new HttpContextAccessor()), _files, Options.Create(new UploadOptions()));
    }

    /// <summary>A current compliance document stored directly (with a real stored file, so it can be copied into a bid).</summary>
    private async Task<int> AddDocumentAsync(int companyId, string userId, ComplianceDocumentType type, DateTime? expiresOn, DateTime? archivedAtUtc = null)
    {
        var key = await _files.SaveAsync(new MemoryStream("%PDF-1.4 profile"u8.ToArray()), ".pdf", _ct);
        await using var db = _db.Marketplace();
        var document = new ComplianceDocument
        {
            CompanyId = companyId, Type = type, IssuedOn = Today.AddMonths(-1), ExpiresOn = expiresOn,
            OriginalFileName = "csd-report.pdf", StorageKey = key, SizeBytes = 16, Sha256 = new string('c', 64),
            UploadedByUserId = userId, UploadedAtUtc = DateTime.UtcNow, ArchivedAtUtc = archivedAtUtc
        };
        db.ComplianceDocuments.Add(document);
        await db.SaveChangesAsync(_ct);
        return document.Id;
    }

    // ------------------------------------------------------------------ rules

    [Fact]
    public void Expiry_follows_the_rule_for_each_type()
    {
        var issued = new DateTime(2026, 3, 1);
        Assert.Equal(new DateTime(2027, 2, 28), ComplianceRules.ExpiresOn(ComplianceDocumentType.BbbeeAffidavit, issued, null)); // 12 months
        Assert.Equal(new DateTime(2026, 3, 31), ComplianceRules.ExpiresOn(ComplianceDocumentType.CsdReport, issued, null));      // 30 days
        Assert.Equal(new DateTime(2026, 5, 31), ComplianceRules.ExpiresOn(ComplianceDocumentType.CertifiedIds, issued, null));
        Assert.Null(ComplianceRules.ExpiresOn(ComplianceDocumentType.CipcRegistration, issued, null));                            // never
        Assert.Equal(new DateTime(2027, 4, 30), ComplianceRules.ExpiresOn(ComplianceDocumentType.CoidaLetter, issued, new DateTime(2027, 4, 30))); // printed
    }

    [Fact]
    public void Status_and_wording_follow_the_days_left()
    {
        var today = new DateTime(2026, 10, 1);
        Assert.Equal(ComplianceStatus.Valid, ComplianceRules.Status(today.AddDays(31), today));
        Assert.Equal(ComplianceStatus.ExpiringSoon, ComplianceRules.Status(today.AddDays(30), today));
        Assert.Equal(ComplianceStatus.ExpiringSoon, ComplianceRules.Status(today, today)); // the last valid day is still valid
        Assert.Equal(ComplianceStatus.Expired, ComplianceRules.Status(today.AddDays(-1), today));
        Assert.Equal(ComplianceStatus.NoExpiry, ComplianceRules.Status(null, today));
        Assert.Equal("Expires in 7 days (8 Oct 2026)", ComplianceRules.Describe(today.AddDays(7), today));
        Assert.Equal("Expired on 30 Sep 2026", ComplianceRules.Describe(today.AddDays(-1), today));
    }

    [Fact]
    public void A_short_lived_document_is_only_flagged_in_its_last_week()
    {
        var today = new DateTime(2026, 10, 1);
        var csdIssued = today.AddDays(-5); // 30-day report: 25 days left
        Assert.Equal(ComplianceStatus.Valid, ComplianceRules.Status(csdIssued.AddDays(30), today, csdIssued));
        Assert.Equal(ComplianceStatus.ExpiringSoon, ComplianceRules.Status(today.AddDays(7), today, today.AddDays(-23)));
        // A 12-month document with 25 days left is flagged, as before.
        Assert.Equal(ComplianceStatus.ExpiringSoon, ComplianceRules.Status(today.AddDays(25), today, today.AddMonths(-11)));
    }

    [Fact]
    public void Checklist_items_map_to_the_documents_that_can_fill_them()
    {
        Assert.Equal(new[] { ComplianceDocumentType.CsdReport }, ComplianceRules.TypesFor("CSD registration summary"));
        Assert.Equal(new[] { ComplianceDocumentType.BbbeeCertificate, ComplianceDocumentType.BbbeeAffidavit },
            ComplianceRules.TypesFor("B-BBEE certificate or sworn affidavit"));
        Assert.Empty(ComplianceRules.TypesFor("Pricing schedule"));
        Assert.Equal(new[] { ComplianceDocumentType.CsdReport }, ComplianceRules.TypesFor("CSD summary report")); // a common variant
        // Every type can fill at least one standard checklist item, so none is left unmatched.
        Assert.All(ComplianceRules.All, info => Assert.Contains(info.RequirementNames, name => TenderCatalog.StandardDocuments.Contains(name)));
    }

    // ------------------------------------------------------------------ uploading

    [Fact]
    public async Task Uploading_works_out_the_expiry_and_replaces_the_previous_document()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var service = Service(out var context);
        await using var _ = context;

        var first = await service.AddAsync(user, new ComplianceInput(ComplianceDocumentType.CsdReport, Today.AddDays(-10), null), Upload.Pdf("csd.pdf"), _ct);
        var second = await service.AddAsync(user, new ComplianceInput(ComplianceDocumentType.CsdReport, Today, null), Upload.Pdf("csd-new.pdf"), _ct);

        Assert.True(first.Succeeded && second.Succeeded);
        var current = Assert.Single(await service.ListCurrentAsync(user, _ct));
        Assert.Equal("csd-new.pdf", current.OriginalFileName);
        Assert.Equal(Today.AddDays(30), current.ExpiresOn); // CSD report: 30 days
        Assert.NotNull((await context.ComplianceDocuments.SingleAsync(d => d.Id == first.Id)).ArchivedAtUtc);
        Assert.Equal(company, current.CompanyId);
    }

    [Fact]
    public async Task Expired_documents_future_dates_missing_printed_expiry_and_fake_pdfs_are_refused()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var service = Service(out var context);
        await using var _ = context;

        var expired = await service.AddAsync(user, new ComplianceInput(ComplianceDocumentType.BankLetter, Today.AddMonths(-4), null), Upload.Pdf(), _ct);
        var future = await service.AddAsync(user, new ComplianceInput(ComplianceDocumentType.BankLetter, Today.AddDays(2), null), Upload.Pdf(), _ct);
        var noExpiry = await service.AddAsync(user, new ComplianceInput(ComplianceDocumentType.TaxCompliance, Today, null), Upload.Pdf(), _ct);
        var fake = await service.AddAsync(user, new ComplianceInput(ComplianceDocumentType.BankLetter, Today, null),
            Upload.File("letter.pdf", "application/pdf", "MZ not a pdf"), _ct);

        Assert.Contains("expired on", expired.Errors.Single().Message);
        Assert.Equal("IssuedOn", future.Errors.Single().Field);
        Assert.Equal("ExpiresOn", noExpiry.Errors.Single().Field);
        Assert.Equal("File", fake.Errors.Single().Field);
        Assert.Equal(0, await context.ComplianceDocuments.CountAsync());
    }

    [Fact]
    public async Task Only_the_owner_can_remove_or_open_a_document()
    {
        var (owner, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var (other, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var id = await AddDocumentAsync(company, owner, ComplianceDocumentType.CsdReport, Today.AddDays(40));
        var service = Service(out var context);
        await using var _ = context;

        Assert.True((await service.RemoveAsync(other, id, _ct)).NotFound);
        Assert.Null(await service.FindOwnAsync(other, id, _ct));
        Assert.True((await service.RemoveAsync(owner, id, _ct)).Succeeded);
        Assert.Empty(await service.ListCurrentAsync(owner, _ct));
    }

    [Fact]
    public async Task Organisation_staff_never_read_compliance_documents()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        await AddDocumentAsync(company, user, ComplianceDocumentType.CertifiedIds, Today.AddDays(40));
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(-2));
        _db.AddSubmission(tender, company, user, SubmissionStatus.Submitted);

        await using var rbidz = _db.Context(TestTenant.Org(TestDb.Rbidz));
        Assert.Equal(0, await rbidz.ComplianceDocuments.CountAsync());
    }

    // ------------------------------------------------------------------ reminders

    [Fact]
    public async Task Expiry_reminders_go_out_at_30_days_7_days_and_on_expiry_each_once()
    {
        var job = new ReminderService(_db.Options, db => Notifications.Into(_mail, db), Options.Create(new ReminderOptions()), NullLogger<ReminderService>.Instance);
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        await AddDocumentAsync(company, user, ComplianceDocumentType.CsdReport, Today.AddDays(25));                       // 30-day stage
        await AddDocumentAsync(company, user, ComplianceDocumentType.TaxCompliance, Today.AddDays(5));                   // 7-day stage
        await AddDocumentAsync(company, user, ComplianceDocumentType.BankLetter, Today.AddDays(-2));                     // expired
        await AddDocumentAsync(company, user, ComplianceDocumentType.MunicipalAccount, Today.AddDays(-30));              // expired long ago: no email
        await AddDocumentAsync(company, user, ComplianceDocumentType.CertifiedIds, Today.AddDays(90));                   // not due
        await AddDocumentAsync(company, user, ComplianceDocumentType.BbbeeAffidavit, Today.AddDays(3), DateTime.UtcNow); // replaced: ignored

        var first = await job.RunAsync(DateTime.UtcNow, _ct);
        var second = await job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Equal(3, first.DocumentReminders);
        Assert.Equal(0, second.DocumentReminders);
        var subjects = _mail.Sent.Select(m => m.Message.Subject).ToList();
        Assert.Contains(subjects, s => s.StartsWith("Expires in 25 days: your CSD registration report"));
        Assert.Contains(subjects, s => s.StartsWith("Expires in 5 days: your SARS tax compliance status"));
        Assert.Contains(subjects, s => s.StartsWith("Expired: your Bank confirmation letter"));
        Assert.All(_mail.Sent, m => Assert.Equal($"{user}@example.test", m.Message.To));
    }

    [Fact]
    public async Task A_fresh_30_day_csd_report_gets_no_early_reminder()
    {
        var job = new ReminderService(_db.Options, db => Notifications.Into(_mail, db), Options.Create(new ReminderOptions()), NullLogger<ReminderService>.Instance);
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        await using (var db = _db.Marketplace())
        {
            db.ComplianceDocuments.Add(new ComplianceDocument
            {
                CompanyId = company, Type = ComplianceDocumentType.CsdReport, IssuedOn = Today, ExpiresOn = Today.AddDays(30),
                OriginalFileName = "csd.pdf", StorageKey = "csd-fresh", SizeBytes = 10, Sha256 = new string('d', 64),
                UploadedByUserId = user, UploadedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(_ct);
        }

        var now = await job.RunAsync(DateTime.UtcNow, _ct);
        var inTheLastWeek = await job.RunAsync(DateTime.UtcNow.AddDays(24), _ct);

        Assert.Equal(0, now.DocumentReminders);
        Assert.Equal(1, inTheLastWeek.DocumentReminders);
    }

    [Fact]
    public async Task A_document_moving_into_the_7_day_window_gets_its_second_reminder()
    {
        var job = new ReminderService(_db.Options, db => Notifications.Into(_mail, db), Options.Create(new ReminderOptions()), NullLogger<ReminderService>.Instance);
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        await AddDocumentAsync(company, user, ComplianceDocumentType.CsdReport, Today.AddDays(10));

        var now = await job.RunAsync(DateTime.UtcNow, _ct);
        var fourDaysLater = await job.RunAsync(DateTime.UtcNow.AddDays(4), _ct);

        Assert.Equal(1, now.DocumentReminders);
        Assert.Equal(1, fourDaysLater.DocumentReminders);
        Assert.Contains(_mail.Sent, m => m.Message.Subject.StartsWith("Expires in 6 days"));
    }

    // ------------------------------------------------------------------ attaching to a bid

    private (ApplicationService Service, Web.Data.EProcureDbContext Context, string User, int Company, int Submission, int CsdRequirement) DraftApplication()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(20));
        int requirement;
        using (var db = _db.Marketplace())
        {
            var row = new TenderRequirement { TenderId = tender, Name = "CSD registration summary", IsMandatory = true, SortOrder = 9 };
            db.TenderRequirements.Add(row);
            db.SaveChanges();
            requirement = row.Id;
        }
        var submission = _db.AddSubmission(tender, company, user, SubmissionStatus.Draft);
        var context = _db.Marketplace();
        var service = new ApplicationService(context, new AuditService(context, new HttpContextAccessor()), _files, new MockPaymentGateway(),
            Options.Create(new UploadOptions()), Notifications.Into(_mail, context));
        return (service, context, user, company, submission, requirement);
    }

    [Fact]
    public async Task A_profile_document_is_copied_into_the_bid()
    {
        var app = DraftApplication();
        await using var _ = app.Context;
        var profileId = await AddDocumentAsync(app.Company, app.User, ComplianceDocumentType.CsdReport, Today.AddDays(40));
        var submission = (await app.Service.LoadAsync(app.Submission, app.User, _ct))!;

        var result = await app.Service.UseProfileDocumentAsync(submission, app.CsdRequirement, profileId, app.User, _ct);

        Assert.True(result.Succeeded);
        var attached = await app.Context.UploadedDocuments.SingleAsync(d => d.TenderRequirementId == app.CsdRequirement);
        var profile = await app.Context.ComplianceDocuments.SingleAsync(d => d.Id == profileId);
        Assert.NotEqual(profile.StorageKey, attached.StorageKey); // the bid has its own copy
        Assert.Equal(profile.Sha256, attached.Sha256);
        Assert.Equal(_files.Files[profile.StorageKey], _files.Files[attached.StorageKey]);
    }

    [Fact]
    public async Task Expired_mismatched_or_another_companys_documents_cannot_be_attached()
    {
        var app = DraftApplication();
        await using var _ = app.Context;
        var expired = await AddDocumentAsync(app.Company, app.User, ComplianceDocumentType.CsdReport, Today.AddDays(-1));
        var bankLetter = await AddDocumentAsync(app.Company, app.User, ComplianceDocumentType.BankLetter, Today.AddDays(40));
        var (otherUser, otherCompany) = _db.AddSupplier(BbbeeLevel.Level1);
        var someoneElses = await AddDocumentAsync(otherCompany, otherUser, ComplianceDocumentType.CsdReport, Today.AddDays(40));
        var submission = (await app.Service.LoadAsync(app.Submission, app.User, _ct))!;

        Assert.Contains("expired", (await app.Service.UseProfileDocumentAsync(submission, app.CsdRequirement, expired, app.User, _ct)).Errors.Single().Message);
        Assert.Contains("cannot be used", (await app.Service.UseProfileDocumentAsync(submission, app.CsdRequirement, bankLetter, app.User, _ct)).Errors.Single().Message);
        Assert.True((await app.Service.UseProfileDocumentAsync(submission, app.CsdRequirement, someoneElses, app.User, _ct)).NotFound);
        Assert.False(await app.Context.UploadedDocuments.AnyAsync(d => d.TenderRequirementId == app.CsdRequirement));
    }

    // ------------------------------------------------------------------ the proposal

    [Fact]
    public void A_tender_that_evaluates_functionality_always_asks_for_the_technical_proposal()
    {
        var form = new TenderFormViewModel { SelectedDocuments = new() { "Pricing schedule" }, UseFunctionality = true };
        Assert.Contains(TenderCatalog.ProposalDocument, TenderService.BuildRequirements(form));

        form.SelectedDocuments.Add(TenderCatalog.ProposalDocument);
        Assert.Single(TenderService.BuildRequirements(form), TenderCatalog.IsProposal); // not twice

        var withoutFunctionality = new TenderFormViewModel { SelectedDocuments = new() { "Pricing schedule" } };
        Assert.DoesNotContain(TenderCatalog.ProposalDocument, TenderService.BuildRequirements(withoutFunctionality));
    }
}
