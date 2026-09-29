namespace EProcure.Web.Domain.Enums;

// All enums are stored in SQL Server as strings (see EProcureDbContext.ConfigureConventions)
// so that a reviewer reading the database directly sees "Published", not "1".

/// <summary>
/// B-BBEE status level. Level 1 is the BEST contribution level, Level 8 the lowest,
/// NonCompliant means no recognised level. The numeric value is used for comparisons:
/// a LOWER number is a BETTER level.
/// </summary>
public enum BbbeeLevel
{
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6,
    Level7 = 7,
    Level8 = 8,
    NonCompliant = 9
}

/// <summary>
/// Preferential procurement point system (Preferential Procurement Regulations, 2022).
/// 80/20 applies below the organisation's threshold, 90/10 above it.
/// Stored as organisation configuration; the system does NOT calculate a winner.
/// </summary>
public enum PreferencePointSystem
{
    EightyTwenty = 1,
    NinetyTen = 2
}

public enum TenderStatus
{
    Draft = 1,      // being prepared by the OrgAdmin, invisible to suppliers
    Published = 2,  // open for applications until ClosingDateUtc
    Closed = 3,     // closing date passed, under evaluation
    Awarded = 4,    // a human award decision has been recorded (AwardRecord)
    Cancelled = 5
}

public enum SubmissionStatus
{
    Draft = 1,            // supplier is still filling in answers / uploading documents
    AwaitingPayment = 2,  // complete, tender fee not yet paid
    Submitted = 3,        // paid; from here on the organisation can see it
    UnderEvaluation = 4,
    Unsuccessful = 5,
    Awarded = 6,
    Withdrawn = 7
}

public enum PaymentStatus
{
    NotRequired = 1,  // tender fee is R0.00
    Pending = 2,
    Paid = 3,
    Failed = 4
}

public enum EnterpriseSize
{
    EME = 1,      // Exempted Micro Enterprise
    QSE = 2,      // Qualifying Small Enterprise
    Generic = 3   // Default / Other
}


/// <summary>What a track-record document proves (shown to the evaluation committee).</summary>
public enum CompanyDocumentKind
{
    ReferenceLetter = 1,       // a client's letter confirming the work and its quality
    CompletionCertificate = 2, // certificate of practical / final completion
    CompanyProfile = 3,        // the company's own profile or portfolio
    Other = 4
}
