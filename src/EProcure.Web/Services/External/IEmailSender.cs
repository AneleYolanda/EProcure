namespace EProcure.Web.Services.External;

/// <summary>One email. Plain text only: no tracking pixels, no remote images, nothing that needs HTML escaping.</summary>
public record EmailMessage(string To, string Subject, string Body, string? LinkText = null, string? LinkUrl = null);

/// <summary>
/// Sends email. The app never depends on a specific provider: appsettings "ExternalServices:Email" chooses the
/// implementation (Mock now; SMTP, SendGrid or Azure Communication Services later).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
