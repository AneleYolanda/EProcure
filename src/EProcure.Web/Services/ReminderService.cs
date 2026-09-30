using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Services;

public class ReminderOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;
    /// <summary>Suppliers with an unfinished application are reminded this long before the closing date.</summary>
    public int HoursBeforeClosing { get; set; } = 48;
    /// <summary>Tenders that closed longer ago than this get no "closed" email (e.g. on the first run after an upgrade).</summary>
    public int ClosedNoticeMaxAgeDays { get; set; } = 7;
}

/// <summary>What a run did, including what it skipped because it was already sent (shown by the demo "run now" button).</summary>
public record ReminderRunResult(int ClosingReminders, int ClosedNotices, int ClosedTendersFound = 0, int AlreadySent = 0, int DocumentReminders = 0);

/// <summary>
/// The scheduled emails, run every few minutes by ReminderWorker (or on demand in Development):
///   - "not submitted yet": once per unfinished (draft or unpaid) application, when its tender closes within the window;
///   - "tender closed": once per tender, to its SCM Officers (with the number of bids) and, if there are bids, its BEC members.
///   - "document expiring": once per compliance document at 30 days and 7 days before it expires, and once when it has
///     expired (within a week of expiry), to the supplier's users.
/// Each email is recorded in SentNotifications under a unique key BEFORE the next one is attempted, so a restart, a
/// second server or a second run never sends it twice. The job works across all organisations with its own
/// DbContext (SystemTenantContext), independent of whoever may be signed in.
/// </summary>
public class ReminderService
{
    private readonly DbContextOptions<EProcureDbContext> _options;
    private readonly Func<EProcureDbContext, INotificationService> _notifications;
    private readonly ReminderOptions _settings;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(DbContextOptions<EProcureDbContext> options, Func<EProcureDbContext, INotificationService> notifications,
        IOptions<ReminderOptions> settings, ILogger<ReminderService> logger)
    {
        _options = options;
        _notifications = notifications;
        _settings = settings.Value;
        _logger = logger;
    }

    public static string ClosingReminderKey(int submissionId) => $"closing-reminder:submission:{submissionId}";
    public static string ClosedNoticeKey(int tenderId) => $"tender-closed:tender:{tenderId}";
    public static string DocumentReminderKey(int documentId, string stage) => $"compliance-{stage}:document:{documentId}";

    public async Task<ReminderRunResult> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var db = new EProcureDbContext(_options, SystemTenantContext.Instance);
        var notifications = _notifications(db);

        // 1. Unfinished applications for tenders that close within the window.
        var windowEnd = nowUtc.AddHours(_settings.HoursBeforeClosing);
        var unfinished = await db.Submissions
            .Include(s => s.Tender)
            .Where(s => (s.Status == SubmissionStatus.Draft || s.Status == SubmissionStatus.AwaitingPayment)
                        && s.Tender.Status == TenderStatus.Published
                        && s.Tender.ClosingDateUtc > nowUtc && s.Tender.ClosingDateUtc <= windowEnd)
            .ToListAsync(ct);
        var reminders = 0;
        foreach (var submission in unfinished)
        {
            if (await ClaimAsync(db, ClosingReminderKey(submission.Id), nowUtc, ct))
            {
                await notifications.ClosingReminderAsync(submission, ct);
                reminders++;
            }
        }

        // 2. Tenders that have closed (recently): tell the organisation, and the BEC when there is something to evaluate.
        var oldest = nowUtc.AddDays(-_settings.ClosedNoticeMaxAgeDays);
        var closed = await db.Tenders
            .Where(t => t.Status == TenderStatus.Published && t.ClosingDateUtc <= nowUtc && t.ClosingDateUtc > oldest)
            .Select(t => new
            {
                Tender = t,
                Bids = t.Submissions.Count(s => s.Status != SubmissionStatus.Draft && s.Status != SubmissionStatus.AwaitingPayment
                                                && s.Status != SubmissionStatus.Withdrawn)
            })
            .ToListAsync(ct);
        var notices = 0;
        var alreadySent = 0;
        foreach (var item in closed)
        {
            if (await ClaimAsync(db, ClosedNoticeKey(item.Tender.Id), nowUtc, ct))
            {
                await notifications.TenderClosedAsync(item.Tender, item.Bids, ct);
                notices++;
            }
            else alreadySent++;
        }

        // 3. Compliance documents that expire soon or just expired (the current document of each type only).
        var documentReminders = await RemindExpiringDocumentsAsync(db, notifications, nowUtc, ct);

        if (reminders + notices + documentReminders > 0)
            _logger.LogInformation("Scheduled emails: {Reminders} closing reminder(s), {Notices} tender-closed notice(s), {Documents} document reminder(s).",
                reminders, notices, documentReminders);
        return new ReminderRunResult(reminders, notices, closed.Count, alreadySent, documentReminders);
    }

    private static async Task<int> RemindExpiringDocumentsAsync(EProcureDbContext db, INotificationService notifications, DateTime nowUtc, CancellationToken ct)
    {
        var today = Infrastructure.SaTime.ToSast(nowUtc).Date;
        var horizon = today.AddDays(ComplianceRules.ReminderDays.Max());
        var oldestExpired = today.AddDays(-7); // do not email about documents that expired long ago (e.g. after an upgrade)
        var due = await db.ComplianceDocuments
            .Where(d => d.ArchivedAtUtc == null && d.ExpiresOn != null && d.ExpiresOn <= horizon && d.ExpiresOn >= oldestExpired)
            .ToListAsync(ct);
        var sent = 0;
        foreach (var document in due)
        {
            var daysLeft = ComplianceRules.DaysLeft(document.ExpiresOn!.Value, today);
            // The most urgent stage reached; earlier stages that were missed are not sent late.
            var stage = daysLeft < 0 ? "expired" : daysLeft == 0 ? "today" : daysLeft <= 7 ? "7-days" : "30-days";
            // A document valid for about a month (the 30-day CSD report) gets no 30-day warning: it would arrive on upload.
            if (stage == "30-days" && ComplianceRules.IsShortLived(document.IssuedOn, document.ExpiresOn.Value)) continue;
            if (await ClaimAsync(db, DocumentReminderKey(document.Id, stage), nowUtc, ct))
            {
                await notifications.ComplianceExpiringAsync(document, daysLeft, ct);
                sent++;
            }
        }
        return sent;
    }

    /// <summary>Records the key; false when it was already sent (the unique index is the final guarantee).</summary>
    private static async Task<bool> ClaimAsync(EProcureDbContext db, string key, DateTime nowUtc, CancellationToken ct)
    {
        if (await db.SentNotifications.AnyAsync(n => n.Key == key, ct)) return false;
        var claim = new SentNotification { Key = key, SentAtUtc = nowUtc };
        db.SentNotifications.Add(claim);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(claim).State = EntityState.Detached; // another run claimed it at the same moment
            return false;
        }
    }
}

/// <summary>Runs ReminderService on a timer inside the web app (no separate scheduler to install).</summary>
public class ReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ReminderOptions _settings;
    private readonly ILogger<ReminderWorker> _logger;

    public ReminderWorker(IServiceScopeFactory scopes, IOptions<ReminderOptions> settings, ILogger<ReminderWorker> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // let start-up (migrations, demo data) finish first
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes)));
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "The scheduled email run failed; it will be tried again at the next interval.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // the app is shutting down
        }
    }
}
