using EProcure.Web.Data;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using Microsoft.Extensions.Logging.Abstractions;

namespace EProcure.Tests.Support;

/// <summary>Links stay relative in tests (there is no web request).</summary>
public sealed class RelativeLinks : ILinkBuilder
{
    public string Absolute(string path) => path;
}

public static class Notifications
{
    /// <summary>The real NotificationService, writing into an in-memory mailbox the test can inspect.</summary>
    public static NotificationService Into(MockEmailSender mailbox, EProcureDbContext db) =>
        new(db, mailbox, new RelativeLinks(), NullLogger<NotificationService>.Instance);
}
