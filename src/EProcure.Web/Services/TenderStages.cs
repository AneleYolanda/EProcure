using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Services;

/// <summary>What an administrator sees as the tender's stage (design "Tender register" badges).</summary>
public enum TenderStage
{
    Draft,
    Advertised,       // Published and the closing date is still ahead
    UnderEvaluation,  // Closed, or Published with the closing date passed
    Awarded,
    Cancelled
}

/// <summary>
/// Turns the stored status + closing date into the stage shown on screen. A published tender
/// becomes "Under evaluation" the moment its closing date passes, without anyone having to
/// press a button, so the register is never out of date.
/// </summary>
public static class TenderStages
{
    public static TenderStage For(TenderStatus status, DateTime closingUtc, DateTime nowUtc) => status switch
    {
        TenderStatus.Draft => TenderStage.Draft,
        TenderStatus.Published => closingUtc > nowUtc ? TenderStage.Advertised : TenderStage.UnderEvaluation,
        TenderStatus.Closed => TenderStage.UnderEvaluation,
        TenderStatus.Awarded => TenderStage.Awarded,
        _ => TenderStage.Cancelled
    };

    public static string Label(TenderStage stage) => stage switch
    {
        TenderStage.Draft => "Draft",
        TenderStage.Advertised => "Advertised",
        TenderStage.UnderEvaluation => "Under evaluation",
        TenderStage.Awarded => "Awarded",
        _ => "Cancelled"
    };

    /// <summary>Badge colours from the design's STAGE table.</summary>
    public static string BadgeCss(TenderStage stage) => stage switch
    {
        TenderStage.Advertised => "ep-badge ep-badge--info",
        TenderStage.UnderEvaluation => "ep-badge ep-badge--navy",
        TenderStage.Awarded => "ep-badge ep-badge--success",
        TenderStage.Cancelled => "ep-badge ep-badge--warning",
        _ => "ep-badge"
    };

    /// <summary>Short key used in the register's filter links (?stage=advertised).</summary>
    public static string Key(TenderStage stage) => stage switch
    {
        TenderStage.UnderEvaluation => "evaluation",
        _ => stage.ToString().ToLowerInvariant()
    };

    public static readonly TenderStage[] All =
        { TenderStage.Draft, TenderStage.Advertised, TenderStage.UnderEvaluation, TenderStage.Awarded, TenderStage.Cancelled };
}
