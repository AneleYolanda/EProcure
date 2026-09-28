using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Tests.Rules;

/// <summary>
/// Digitise, do not automate decisions: risky declarations are flagged for the committee, never auto-rejected.
/// (Status changes are covered by EvaluationServiceTests: they only happen through evaluation and award.)
/// </summary>
public class SubmissionStatusesTests
{
    [Theory]
    [InlineData(SubmissionStatus.Awarded, "Awarded")]
    [InlineData(SubmissionStatus.Unsuccessful, "Not awarded")]
    [InlineData(SubmissionStatus.UnderEvaluation, "Under evaluation")]
    public void Statuses_have_plain_labels(SubmissionStatus status, string label)
    {
        Assert.Equal(label, SubmissionStatuses.Label(status));
    }

    [Fact]
    public void Risky_answers_are_flagged_not_hidden()
    {
        var flags = SubmissionStatuses.RedFlags(new Submission
        {
            IsCsdRegistered = false,
            IsTaxCompliant = true,
            HasDeclaredInterest = true,
            ConfirmsNotRestricted = false,
            ConfirmsIndependentBid = true
        });

        Assert.Equal(3, flags.Count);
        Assert.Contains("Not registered on the CSD", flags);
    }

    [Fact]
    public void Unanswered_questions_are_not_flagged()
    {
        Assert.Empty(SubmissionStatuses.RedFlags(new Submission()));
    }
}
