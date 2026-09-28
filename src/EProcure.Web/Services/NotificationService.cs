using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services.External;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Services;

/// <summary>
/// Every email eProcure sends, in one place: who receives it and what it says.
///
/// Rules:
///   - an email is a courtesy copy, never the record: the application timeline and the audit trail are. So a failed
///     email is logged and the action still stands (a provider outage must not undo an award or a submission);
///   - minimum information (POPIA): the email says what happened and links to the page; bid prices, other bidders'
///     documents and committee notes are never emailed;
///   - staff names are not sent to bidders (DECISIONS D38): messages come from the organisation.
/// </summary>
public interface INotificationService
{
    Task ApplicationSubmittedAsync(Submission submission, CancellationToken ct = default);
    Task BidOutcomeAsync(Submission submission, string note, CancellationToken ct = default);
    Task BidWithdrawnAsync(Submission submission, CancellationToken ct = default);
    Task RecommendationSubmittedAsync(Tender tender, string recommendedCompany, CancellationToken ct = default);
    Task StaffInvitedAsync(ApplicationUser user, string organisationName, string roleLabel, string link, CancellationToken ct = default);
    Task PasswordResetRequestedAsync(ApplicationUser user, string link, CancellationToken ct = default);
    Task PasswordChangedAsync(ApplicationUser user, CancellationToken ct = default);
}

public class NotificationService : INotificationService
{
    private readonly EProcureDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILinkBuilder _links;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(EProcureDbContext db, IEmailSender email, ILinkBuilder links, ILogger<NotificationService> logger)
    {
        _db = db;
        _email = email;
        _links = links;
        _logger = logger;
    }

    public async Task ApplicationSubmittedAsync(Submission s, CancellationToken ct = default)
    {
        var (to, name) = await RecipientAsync(s.SubmittedByUserId, ct);
        var organisation = await OrganisationNameAsync(s.Tender.OrganisationId, ct);
        await SendAsync(new EmailMessage(to, $"Application submitted: {s.Tender.ReferenceNumber} ({s.ReferenceNumber})",
            $"Hello {name},\n\nYour application for {s.Tender.ReferenceNumber} \"{s.Tender.Title}\" has been submitted to {organisation} " +
            $"on behalf of {s.Company.Name}. Your reference is {s.ReferenceNumber}.\n\n" +
            $"Bids stay sealed until the closing date ({DisplayFormat.DateTime(s.Tender.ClosingDateUtc)}). You can withdraw and resubmit " +
            "until then. Every step of the evaluation appears on your application.",
            "View your application", _links.Absolute($"/Supplier/Applications/Details/{s.Id}")), ct);
    }

    public async Task BidOutcomeAsync(Submission s, string note, CancellationToken ct = default)
    {
        var (to, name) = await RecipientAsync(s.SubmittedByUserId, ct);
        var awarded = s.Status == Domain.Enums.SubmissionStatus.Awarded;
        await SendAsync(new EmailMessage(to, $"{(awarded ? "Awarded" : "Outcome")}: {s.Tender.ReferenceNumber}",
            $"Hello {name},\n\nThe tender {s.Tender.ReferenceNumber} \"{s.Tender.Title}\" has been decided.\n\n{note}",
            "View your application", _links.Absolute($"/Supplier/Applications/Details/{s.Id}")), ct);
    }

    public async Task BidWithdrawnAsync(Submission s, CancellationToken ct = default)
    {
        var (to, name) = await RecipientAsync(s.SubmittedByUserId, ct);
        await SendAsync(new EmailMessage(to, $"Bid withdrawn: {s.Tender.ReferenceNumber} ({s.ReferenceNumber})",
            $"Hello {name},\n\nYour bid {s.ReferenceNumber} for {s.Tender.ReferenceNumber} has been withdrawn and will not be evaluated. " +
            $"You can reopen and resubmit it until the closing date ({DisplayFormat.DateTime(s.Tender.ClosingDateUtc)}).\n\n" +
            "If you did not do this, sign in and change your password straight away.",
            "View your application", _links.Absolute($"/Supplier/Applications/Details/{s.Id}")), ct);
    }

    public async Task RecommendationSubmittedAsync(Tender tender, string recommendedCompany, CancellationToken ct = default)
    {
        // SCM Officers of the tender's organisation who can sign in (deactivated accounts are skipped).
        var admins = await (from u in _db.Users
                            join ur in _db.UserRoles on u.Id equals ur.UserId
                            join r in _db.Roles on ur.RoleId equals r.Id
                            where r.Name == AppRoles.OrgAdmin && u.OrganisationId == tender.OrganisationId && u.Email != null
                            select u).ToListAsync(ct);
        foreach (var admin in admins.Where(StaffAccounts.IsActive))
        {
            await SendAsync(new EmailMessage(admin.Email!, $"BAC decision needed: {tender.ReferenceNumber}",
                $"Hello {admin.FullName},\n\nThe Bid Evaluation Committee has submitted its scoresheet for {tender.ReferenceNumber} " +
                $"\"{tender.Title}\" and recommends {recommendedCompany}. Record the Bid Adjudication Committee's decision, or return the " +
                "evaluation to the BEC, in eProcure.",
                "Open the scoresheet", _links.Absolute($"/Admin/Evaluation/Tender/{tender.Id}")), ct);
        }
    }

    public Task StaffInvitedAsync(ApplicationUser user, string organisationName, string roleLabel, string link, CancellationToken ct = default) =>
        SendAsync(new EmailMessage(user.Email!, $"You have been added to {organisationName} on eProcure",
            $"Hello {user.FullName},\n\n{organisationName} has added you to its eProcure workspace as {roleLabel}. " +
            "Choose your password with the link below. The link works once and expires in 3 days.\n\n" +
            "If you were not expecting this, ignore this email.",
            "Choose your password", link), ct);

    public Task PasswordResetRequestedAsync(ApplicationUser user, string link, CancellationToken ct = default) =>
        SendAsync(new EmailMessage(user.Email!, "Reset your eProcure password",
            $"Hello {user.FullName},\n\nSomeone (hopefully you) asked to reset the password for this eProcure account. " +
            "The link below works once and expires in 1 hour.\n\nIf you did not ask for this, ignore this email: your password " +
            "stays the same.",
            "Choose a new password", link), ct);

    public Task PasswordChangedAsync(ApplicationUser user, CancellationToken ct = default) =>
        SendAsync(new EmailMessage(user.Email!, "Your eProcure password was changed",
            $"Hello {user.FullName},\n\nThe password for your eProcure account was just changed. If this was not you, reset it " +
            "straight away and tell your organisation's SCM Officer or eProcure support.",
            "Sign in", _links.Absolute("/Account/Login")), ct);

    // ------------------------------------------------------------------ helpers

    private async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.To)) return;
        try
        {
            await _email.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Email '{Subject}' could not be sent; the action it reports still stands.", message.Subject);
        }
    }

    private async Task<(string Email, string Name)> RecipientAsync(string userId, CancellationToken ct)
    {
        var user = await _db.Users.Where(u => u.Id == userId).Select(u => new { u.Email, u.FullName }).SingleOrDefaultAsync(ct);
        return (user?.Email ?? string.Empty, user?.FullName ?? "there");
    }

    private Task<string> OrganisationNameAsync(int organisationId, CancellationToken ct) =>
        _db.Organisations.Where(o => o.Id == organisationId).Select(o => o.Name).SingleAsync(ct);
}
