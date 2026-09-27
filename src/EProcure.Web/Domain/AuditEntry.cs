namespace EProcure.Web.Domain;

/// <summary>
/// Append-only audit trail: WHO did WHAT to WHICH record, WHEN. Required by the brief
/// ("tender created, submission created, status changed, viewed by admin").
///
/// Deliberately has NO foreign keys: an audit row must survive even if the user, tender or
/// organisation it refers to is later changed or removed. UserEmail is copied for the same reason.
///
/// POPIA: holds the acting user's id/email and IP address. Access: platform operators and the
/// organisation's own OrgAdmins for rows with their OrganisationId. Retain per the records
/// management policy.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? UserId { get; set; }
    public string? UserEmail { get; set; }

    /// <summary>Tenant the action happened in (NULL for supplier-only actions such as registering).</summary>
    public int? OrganisationId { get; set; }

    /// <summary>Short verb, e.g. "Tender.Created", "Submission.Viewed".</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Optional JSON/text detail, e.g. old and new status.</summary>
    public string? Details { get; set; }

    public string? IpAddress { get; set; }
}
