namespace EProcure.Web.Services;

/// <summary>Fixed lists used by the tender form. Kept in code (not the database) for the MVP.</summary>
public static class TenderCatalog
{
    public static readonly string[] Categories =
    {
        "Construction", "Security Services", "ICT", "Professional Services", "Goods & Supplies",
        "Maintenance", "Cleaning & Hygiene", "Transport", "Other"
    };

    /// <summary>
    /// The standard South African bid documents shown as tick-boxes (design step "Documents").
    /// Anything else can be typed as an extra requirement, one per line.
    /// </summary>
    public static readonly string[] StandardDocuments =
    {
        "Signed bidding document",
        "CSD registration summary",
        "SARS tax compliance PIN",
        "B-BBEE certificate or sworn affidavit",
        "CIPC registration certificate",
        "SBD 4 — Declaration of interest",
        "SBD 6.1 — Preference points claim",
        "SBD 8 — Past supply chain practices",
        "SBD 9 — Independent bid determination",
        "Pricing schedule",
        ProposalDocument,
        "Letter of intent from a financial institution",
        "Health and safety plan",
        "Certified ID copies of directors",
        "Bank confirmation letter",
        "COIDA letter of good standing",
        "Municipal rates statement or clearance",
        "CIDB registration certificate"
    };

    /// <summary>
    /// The bidder's pitch: how they will do the work, their plan, team and why they should win. It is what the BEC reads to score
    /// functionality, so it is added to the checklist automatically when a tender evaluates functionality.
    /// </summary>
    public const string ProposalDocument = "Technical proposal (approach, work plan and team)";

    public static bool IsProposal(string requirementName) => string.Equals(requirementName, ProposalDocument, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ticked by default on a new tender.</summary>
    public static readonly string[] DefaultDocuments =
    {
        "Signed bidding document", "CSD registration summary", "SARS tax compliance PIN",
        "B-BBEE certificate or sworn affidavit", "SBD 4 — Declaration of interest",
        "SBD 9 — Independent bid determination", "Pricing schedule"
    };

    public const int MaxRequirements = 20;
    public const int MaxRequirementLength = 200;
}
