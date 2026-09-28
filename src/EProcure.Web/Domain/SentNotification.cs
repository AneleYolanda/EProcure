namespace EProcure.Web.Domain;

/// <summary>
/// Remembers that a scheduled email was sent, so the reminder job never sends the same one twice (also across restarts).
/// The key names the message and what it is about, e.g. "closing-reminder:submission:12" or "tender-closed:tender:5";
/// a unique index makes "send once" a database guarantee. No personal information is stored here.
/// </summary>
public class SentNotification
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}
