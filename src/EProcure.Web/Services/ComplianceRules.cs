using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Services;

/// <summary>How long one type of compliance document stays valid, in words and as a rule.</summary>
/// <param name="ValidityDays">Valid for this many days from the date on the document (used instead of months for short-lived
/// documents such as the CSD report).</param>
/// <param name="ValidityMonths">Valid for this many months from the date on the document; NULL when it never expires or the
/// expiry date is printed on it.</param>
/// <param name="ExpiryOnDocument">The supplier enters the expiry date printed on the document.</param>
/// <param name="RequirementNames">Tender checklist items this document can satisfy (TenderCatalog names).</param>
public record ComplianceTypeInfo(ComplianceDocumentType Type, string Label, string DateLabel, string Validity,
    int? ValidityMonths, bool ExpiryOnDocument, string[] RequirementNames, int? ValidityDays = null)
{
    public bool NeverExpires => ValidityMonths is null && ValidityDays is null && !ExpiryOnDocument;
}

public enum ComplianceStatus { Valid, ExpiringSoon, Expired, NoExpiry }

/// <summary>
/// The validity rules for compliance documents, in ONE place so they are easy to check and to change.
///
/// Sources (September 2026): B-BBEE certificates and sworn affidavits are valid 12 months from the date of issue or of the
/// Commissioner of Oaths' stamp (B-BBEE Commission practice guides); a SARS tax compliance status PIN is valid for the period
/// printed on it (usually 12 months) but is checked live, so it can turn non-compliant at any time; a CSD registration does
/// not expire but tenders ask for a recent report (commonly 30 days to 3 months); certified copies, bank confirmation letters
/// and municipal statements are commonly required to be no older than 3 months (tender practice, not law); COIDA letters of
/// good standing and CIDB registrations carry their own expiry date. A tender's own wording always wins; these rules drive
/// the reminders and stop an expired document being attached to a bid.
/// </summary>
public static class ComplianceRules
{
    /// <summary>Reminder points before expiry, in days (plus one on the day it expires).</summary>
    public static readonly int[] ReminderDays = { 30, 7 };

    public const int ExpiringSoonDays = 30;

    /// <summary>Documents valid for about a month or less are only flagged in their last week (a 30-day warning would fire on upload).</summary>
    public const int ShortLivedWarningDays = 7;

    /// <summary>True when the document is valid for about a month or less from its date (e.g. a 30-day CSD report).</summary>
    public static bool IsShortLived(DateTime issuedOn, DateTime expiresOn) => (expiresOn.Date - issuedOn.Date).TotalDays <= 31;

    public static readonly IReadOnlyList<ComplianceTypeInfo> All = new[]
    {
        new ComplianceTypeInfo(ComplianceDocumentType.CsdReport, "CSD registration report", "Report date",
            "30 days from the report date, because most tenders ask for a report no older than 30 days. Download a fresh one from the CSD before you bid (it is free).",
            null, false, new[] { "CSD registration summary", "CSD summary report", "CSD registration report" }, ValidityDays: 30),
        new ComplianceTypeInfo(ComplianceDocumentType.TaxCompliance, "SARS tax compliance status (TCS PIN)", "Date issued",
            "Until the expiry date on the TCS letter (usually 12 months). SARS checks it live, so it can become non-compliant sooner.",
            null, true, new[] { "SARS tax compliance PIN", "SARS tax compliance status PIN", "Tax compliance status PIN" }),
        new ComplianceTypeInfo(ComplianceDocumentType.BbbeeCertificate, "B-BBEE certificate", "Date issued",
            "12 months from the date of issue.",
            12, false, new[] { "B-BBEE certificate or sworn affidavit" }),
        new ComplianceTypeInfo(ComplianceDocumentType.BbbeeAffidavit, "B-BBEE sworn affidavit (EME or QSE)", "Date commissioned",
            "12 months from the date of the Commissioner of Oaths' stamp.",
            12, false, new[] { "B-BBEE certificate or sworn affidavit" }),
        new ComplianceTypeInfo(ComplianceDocumentType.CipcRegistration, "CIPC registration certificate", "Date issued",
            "Does not expire. Keep your annual returns up to date with CIPC.",
            null, false, new[] { "CIPC registration certificate" }),
        new ComplianceTypeInfo(ComplianceDocumentType.CertifiedIds, "Certified ID copies of directors", "Date certified",
            "3 months from the date of certification (most tenders; some accept up to 6 months).",
            3, false, new[] { "Certified ID copies of directors" }),
        new ComplianceTypeInfo(ComplianceDocumentType.BankLetter, "Bank confirmation letter", "Letter date",
            "3 months from the date of the letter.",
            3, false, new[] { "Bank confirmation letter" }),
        new ComplianceTypeInfo(ComplianceDocumentType.CoidaLetter, "COIDA letter of good standing", "Date issued",
            "Until the expiry date on the letter (usually 30 April each year).",
            null, true, new[] { "COIDA letter of good standing" }),
        new ComplianceTypeInfo(ComplianceDocumentType.MunicipalAccount, "Municipal rates statement or clearance", "Statement date",
            "3 months from the date of the statement.",
            3, false, new[] { "Municipal rates statement or clearance" }),
        new ComplianceTypeInfo(ComplianceDocumentType.CidbRegistration, "CIDB registration certificate", "Date issued",
            "Until the expiry date on the certificate.",
            null, true, new[] { "CIDB registration certificate" })
    };

    public static ComplianceTypeInfo Info(ComplianceDocumentType type) => All.Single(i => i.Type == type);

    /// <summary>The document types that can satisfy a tender checklist item (empty when none can).</summary>
    public static IReadOnlyList<ComplianceDocumentType> TypesFor(string requirementName) =>
        All.Where(i => i.RequirementNames.Contains(requirementName, StringComparer.OrdinalIgnoreCase)).Select(i => i.Type).ToList();

    /// <summary>
    /// The last valid day: the printed expiry date for documents that carry one, otherwise the date on the document plus
    /// the validity period (a document valid 12 months from 1 March is valid up to and including 28 February), or NULL.
    /// </summary>
    public static DateTime? ExpiresOn(ComplianceDocumentType type, DateTime issuedOn, DateTime? printedExpiry)
    {
        var info = Info(type);
        if (info.ExpiryOnDocument) return printedExpiry?.Date;
        if (info.ValidityDays is int days) return issuedOn.Date.AddDays(days); // "not older than 30 days": dated 1 Oct, still fine on 31 Oct
        return info.ValidityMonths is int months ? issuedOn.Date.AddMonths(months).AddDays(-1) : null;
    }

    public static ComplianceStatus Status(DateTime? expiresOn, DateTime todaySast, DateTime? issuedOn = null)
    {
        if (expiresOn is not DateTime last) return ComplianceStatus.NoExpiry;
        if (last.Date < todaySast.Date) return ComplianceStatus.Expired;
        var warnDays = issuedOn is DateTime issued && IsShortLived(issued, last) ? ShortLivedWarningDays : ExpiringSoonDays;
        return (last.Date - todaySast.Date).TotalDays <= warnDays ? ComplianceStatus.ExpiringSoon : ComplianceStatus.Valid;
    }

    /// <summary>Days from today until the last valid day (0 = expires today; negative = expired).</summary>
    public static int DaysLeft(DateTime expiresOn, DateTime todaySast) => (int)(expiresOn.Date - todaySast.Date).TotalDays;

    public static string Day(DateTime date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"Valid until 31 Dec 2026", "Expires in 7 days", "Expired on 1 Oct 2026", "Does not expire".</summary>
    public static string Describe(DateTime? expiresOn, DateTime todaySast)
    {
        if (expiresOn is not DateTime last) return "Does not expire";
        var days = DaysLeft(last, todaySast);
        return days switch
        {
            < 0 => $"Expired on {Day(last)}",
            0 => "Expires today",
            1 => "Expires tomorrow",
            <= ExpiringSoonDays => $"Expires in {days} days ({Day(last)})",
            _ => $"Valid until {Day(last)}"
        };
    }
}
