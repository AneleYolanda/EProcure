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

public record ReminderRunResult(int ClosingReminders, int ClosedNotices);

/// <summary>
/// The scheduled emails, run every few minutes by ReminderWorker (or on demand in Development):
///   - "not submitted yet": once per unfinished (draft or unpaid) application, when its tender closes within the window;
///   - "tender closed": once per tender, to its SCM Officers (with the number of bids) and, if there are bids, its BEC members.
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
        foreach (var item in closed)
        {
            if (await ClaimAsync(db, ClosedNoticeKey(item.Tender.Id), nowUtc, ct))
            {
                await notifications.TenderClosedAsync(item.Tender, item.Bids, ct);
                notices++;
            }
        }

        if (reminders + notices > 0)
            _logger.LogInformation("Scheduled emails: {Reminders} closing reminder(s), {Notices} tender-closed notice(s).", reminders, notices);
        return new ReminderRunResult(reminders, notices);
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
