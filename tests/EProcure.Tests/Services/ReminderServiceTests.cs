using EProcure.Tests.Support;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EProcure.Tests.Services;

/// <summary>The scheduled emails: closing-date reminders and "tender closed" notices, each sent exactly once.</summary>
public sealed class ReminderServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly MockEmailSender _mail = new();
    private readonly ReminderService _job;
    private readonly CancellationToken _ct = CancellationToken.None;

    public ReminderServiceTests()
    {
        _job = new ReminderService(_db.Options, db => Notifications.Into(_mail, db), Options.Create(new ReminderOptions()),
            NullLogger<ReminderService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private (string User, int Submission) Application(int tender, SubmissionStatus status)
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        return (user, _db.AddSubmission(tender, company, user, status));
    }

    private List<EmailMessage> Mail(string subjectStart) =>
        _mail.Sent.Select(m => m.Message).Where(m => m.Subject.StartsWith(subjectStart)).ToList();

    // ------------------------------------------------------------------ closing-date reminders

    [Fact]
    public async Task An_unfinished_application_is_reminded_once_when_the_tender_closes_within_48_hours()
    {
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(1));
        var (user, submission) = Application(tender, SubmissionStatus.Draft);

        var first = await _job.RunAsync(DateTime.UtcNow, _ct);
        var second = await _job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Equal(1, first.ClosingReminders);
        Assert.Equal(0, second.ClosingReminders);
        var email = Assert.Single(Mail("Not submitted yet"));
        Assert.Equal($"{user}@example.test", email.To);
        Assert.Equal($"/Supplier/Applications/Step/{submission}?n=1", email.LinkUrl);
    }

    [Fact]
    public async Task An_unpaid_application_is_reminded_to_pay()
    {
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(1), fee: 500m);
        var (_, submission) = Application(tender, SubmissionStatus.AwaitingPayment);

        await _job.RunAsync(DateTime.UtcNow, _ct);

        var email = Assert.Single(Mail("Not submitted yet"));
        Assert.Contains("tender fee has not been paid", email.Body);
        Assert.Equal($"/Supplier/Applications/Pay/{submission}", email.LinkUrl);
    }

    [Fact]
    public async Task Submitted_applications_and_tenders_outside_the_window_get_no_reminder()
    {
        Application(_db.AddTender(TestDb.Rbidz, TestDb.InDays(1)), SubmissionStatus.Submitted);  // already submitted
        Application(_db.AddTender(TestDb.Rbidz, TestDb.InDays(5)), SubmissionStatus.Draft);      // closes later
        Application(_db.AddTender(TestDb.Rbidz, TestDb.InDays(-1)), SubmissionStatus.Draft);     // already closed

        var result = await _job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Equal(0, result.ClosingReminders);
        Assert.Empty(Mail("Not submitted yet"));
    }

    // ------------------------------------------------------------------ tender closed

    [Fact]
    public async Task A_closed_tender_with_bids_is_announced_once_to_the_scm_officers_and_the_bec()
    {
        var scm = _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        var bec = _db.AddStaff(TestDb.Rbidz, TestDb.EvaluatorRoleId);
        var otherOrg = _db.AddStaff(TestDb.Mvlm, TestDb.OrgAdminRoleId);
        var tender = _db.AddTender(TestDb.Rbidz, DateTime.UtcNow.AddHours(-2));
        Application(tender, SubmissionStatus.Submitted);
        Application(tender, SubmissionStatus.Submitted);
        Application(tender, SubmissionStatus.Withdrawn); // not counted
        Application(tender, SubmissionStatus.Draft);     // not a bid

        var first = await _job.RunAsync(DateTime.UtcNow, _ct);
        var second = await _job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Equal(1, first.ClosedNotices);
        Assert.Equal(0, second.ClosedNotices);
        Assert.Equal($"{scm}@example.test", Assert.Single(Mail("Closed with 2 bids")).To);
        Assert.Equal($"{bec}@example.test", Assert.Single(Mail("Bids open for evaluation")).To);
        Assert.DoesNotContain(_mail.Sent, m => m.Message.To == $"{otherOrg}@example.test");
        Assert.True(await _db.Marketplace().SentNotifications.AnyAsync(n => n.Key == ReminderService.ClosedNoticeKey(tender)));
    }

    [Fact]
    public async Task A_tender_that_closed_without_bids_is_announced_to_the_scm_officers_only()
    {
        _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        _db.AddStaff(TestDb.Rbidz, TestDb.EvaluatorRoleId);
        _db.AddTender(TestDb.Rbidz, DateTime.UtcNow.AddHours(-1));

        await _job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Contains("can be cancelled and re-advertised", Assert.Single(Mail("Closed with no bids")).Body);
        Assert.Empty(Mail("Bids open for evaluation"));
    }

    [Fact]
    public async Task Old_closed_tenders_and_drafts_are_not_announced()
    {
        _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        _db.AddTender(TestDb.Rbidz, TestDb.InDays(-10));                                 // closed long ago
        _db.AddTender(TestDb.Rbidz, TestDb.InDays(-1), status: TenderStatus.Draft);      // never published
        _db.AddTender(TestDb.Rbidz, TestDb.InDays(-1), status: TenderStatus.Cancelled);  // cancelled

        var result = await _job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Equal(0, result.ClosedNotices);
    }

    [Fact]
    public async Task Deactivated_staff_get_no_notices()
    {
        var active = _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        var deactivated = _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        await using (var arrange = _db.Marketplace())
        {
            (await arrange.Users.SingleAsync(u => u.Id == deactivated)).LockoutEnd = StaffAccounts.DeactivatedUntil;
            await arrange.SaveChangesAsync();
        }
        _db.AddTender(TestDb.Rbidz, DateTime.UtcNow.AddHours(-1));

        await _job.RunAsync(DateTime.UtcNow, _ct);

        Assert.Equal($"{active}@example.test", Assert.Single(Mail("Closed with")).To);
    }
}
