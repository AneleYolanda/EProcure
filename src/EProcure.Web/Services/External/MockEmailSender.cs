using System.Collections.Concurrent;

namespace EProcure.Web.Services.External;

/// <summary>
/// Development/demo stand-in for an email provider. Nothing leaves the laptop: each message is written to the log
/// and kept in memory (the latest 200) so the Development-only demo mailbox (/dev/mailbox) can show it, including
/// the links in password-reset and invitation emails. Messages are lost when the app restarts.
/// Registered as a singleton so every request sees the same mailbox.
/// </summary>
public class MockEmailSender : IEmailSender
{
    public const int Capacity = 200;

    public record SentEmail(DateTime SentAtUtc, EmailMessage Message);

    private readonly ConcurrentQueue<SentEmail> _sent = new();
    private readonly ILogger<MockEmailSender>? _logger;

    public MockEmailSender(ILogger<MockEmailSender>? logger = null) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Enqueue(new SentEmail(DateTime.UtcNow, message));
        while (_sent.Count > Capacity && _sent.TryDequeue(out _)) { }
        _logger?.LogInformation("DEMO MODE: email to {To}: {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SentEmail> Sent => _sent.Reverse().ToList();
}
