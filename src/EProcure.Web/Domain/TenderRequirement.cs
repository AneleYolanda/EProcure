namespace EProcure.Web.Domain;

/// <summary>
/// One line of a tender's required-documents checklist, e.g. "SBD 4 Declaration of Interest"
/// or "CIPC registration certificate". Belongs to exactly one Tender (and so to its organisation).
/// POPIA: no personal information.
/// </summary>
public class TenderRequirement
{
    public int Id { get; set; }

    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Mandatory documents must be uploaded before the submission can be completed.</summary>
    public bool IsMandatory { get; set; } = true;

    public int SortOrder { get; set; }
}
