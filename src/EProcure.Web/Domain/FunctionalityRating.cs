namespace EProcure.Web.Domain;

/// <summary>
/// The BEC's rating of one bid against one functionality criterion, on the usual 0 to 5 scale
/// (0 not addressed, 1 very poor, 2 poor, 3 average, 4 good, 5 excellent). The weighted percentage is
/// calculated from these ratings (EvaluationRules.FunctionalityScore), never typed in.
/// Access: follows its BidEvaluation (the tender's organisation only).
/// </summary>
public class FunctionalityRating
{
    public int Id { get; set; }

    public int BidEvaluationId { get; set; }
    public BidEvaluation BidEvaluation { get; set; } = null!;

    public int TenderFunctionalityCriterionId { get; set; }
    public TenderFunctionalityCriterion Criterion { get; set; } = null!;

    public int Rating { get; set; }
}
