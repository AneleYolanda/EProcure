using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;

namespace EProcure.Tests.Rules;

/// <summary>Digitise, do not automate decisions: staff record limited status changes, never an award.</summary>
public class SubmissionStatusesTests
{
    [Fact]
    public void Submitted_can_move_to_under_evaluation_or_not_awarded()
    {
        Assert.Equal(new[] { SubmissionStatus.UnderEvaluation, SubmissionStatus.Unsuccessful },
            SubmissionStatuses.AllowedNext(SubmissionStatus.Submitted));
    }

    [Fact]
    public void No_status_ever_offers_awarded()
    {
        foreach (var status in Enum.GetValues<SubmissionStatus>())
            Assert.DoesNotContain(SubmissionStatus.Awarded, SubmissionStatuses.AllowedNext(status));
    }

    [Theory]
    [InlineData(SubmissionStatus.Draft)]
    [InlineData(SubmissionStatus.AwaitingPayment)]
    [InlineData(SubmissionStatus.Unsuccessful)]
    [InlineData(SubmissionStatus.Awarded)]
    [InlineData(SubmissionStatus.Withdrawn)]
    public void Unsubmitted_and_final_statuses_cannot_be_changed_by_staff(SubmissionStatus status)
    {
        Assert.Empty(SubmissionStatuses.AllowedNext(status));
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
