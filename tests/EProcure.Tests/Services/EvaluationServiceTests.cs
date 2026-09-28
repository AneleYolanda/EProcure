using EProcure.Tests.Support;
using EProcure.Web.Data;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Tests.Services;

/// <summary>
/// Bid opening, BEC evaluation and BAC award through the real EvaluationService, as the organisation's staff.
/// Scenario (80/20): three bids, the cheapest is NOT ranked first once B-BBEE preference points are added.
/// </summary>
public sealed class EvaluationServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly string _evaluator;
    private readonly string _scmOfficer;
    private readonly EProcure.Web.Services.External.MockEmailSender _mail = new();

    public EvaluationServiceTests()
    {
        _evaluator = _db.AddUser(TestDb.Rbidz);
        _scmOfficer = _db.AddUser(TestDb.Rbidz);
    }

    public void Dispose() => _db.Dispose();

    private (EvaluationService Service, EProcureDbContext Context) As(int organisationId)
    {
        var context = _db.Context(TestTenant.Org(organisationId));
        return (new EvaluationService(context, new AuditService(context, new HttpContextAccessor()), Notifications.Into(_mail, context)), context);
    }

    /// <summary>A closed RBIDZ tender with three submitted bids. Returns (tender, levelOne, levelSix, levelTwo).</summary>
    private (int Tender, int Umhlathi, int Khanya, int Siyakha) ClosedTenderWithThreeBids(double closedDaysAgo = 2)
    {
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(-closedDaysAgo));
        int Bid(BbbeeLevel level)
        {
            var (user, company) = _db.AddSupplier(level);
            return _db.AddSubmission(tender, company, user, SubmissionStatus.Submitted, level);
        }
        return (tender, Bid(BbbeeLevel.Level1), Bid(BbbeeLevel.Level6), Bid(BbbeeLevel.Level2));
    }

    private static BidEvaluationInput Responsive(decimal price) => new(true, null, price, null);

    private async Task EvaluateAllAsync(EvaluationService service, (int Tender, int Umhlathi, int Khanya, int Siyakha) t)
    {
        Assert.True((await service.CaptureAsync(t.Umhlathi, Responsive(1_380_000m), _evaluator, _ct)).Succeeded);
        Assert.True((await service.CaptureAsync(t.Khanya, Responsive(1_150_000m), _evaluator, _ct)).Succeeded);
        Assert.True((await service.CaptureAsync(t.Siyakha, Responsive(1_240_000m), _evaluator, _ct)).Succeeded);
    }

    private AwardInput Award(int submissionId, string rationale = "Highest-ranked responsive bid, as recommended by the BEC.") =>
        new(submissionId, "BAC 2026/41", SaTime.ToSast(DateTime.UtcNow).Date, rationale);

    // ------------------------------------------------------------------ bid opening

    [Fact]
    public async Task Bids_are_sealed_until_the_closing_date()
    {
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(5));
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var bid = _db.AddSubmission(tender, company, user, SubmissionStatus.Submitted);
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        var result = await service.CaptureAsync(bid, Responsive(100m), _evaluator, _ct);

        Assert.Contains("sealed", result.Errors.Single().Message);
        Assert.Equal(0, await context.BidEvaluations.CountAsync());
        Assert.True(EvaluationService.IsSealed(TestDb.InDays(5), DateTime.UtcNow));
    }

    [Fact]
    public async Task Another_organisation_cannot_evaluate_or_see_the_scoresheet()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Mvlm);
        await using var _ = context;

        Assert.True((await service.CaptureAsync(t.Khanya, Responsive(100m), _evaluator, _ct)).NotFound);
        Assert.Null(await service.GetScoresheetAsync(t.Tender, _ct));
        Assert.True((await service.AwardAsync(t.Tender, Award(t.Khanya), _scmOfficer, _ct)).NotFound);
    }

    // ------------------------------------------------------------------ capturing

    [Fact]
    public async Task A_responsive_bid_needs_a_price_and_a_non_responsive_bid_needs_a_reason()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        var noPrice = await service.CaptureAsync(t.Khanya, new BidEvaluationInput(true, null, null, null), _evaluator, _ct);
        var noReason = await service.CaptureAsync(t.Khanya, new BidEvaluationInput(false, " ", null, null), _evaluator, _ct);
        var noDecision = await service.CaptureAsync(t.Khanya, new BidEvaluationInput(null, null, 100m, null), _evaluator, _ct);

        Assert.Equal("BidPrice", noPrice.Errors.Single().Field);
        Assert.Equal("NonResponsiveReason", noReason.Errors.Single().Field);
        Assert.Equal("IsResponsive", noDecision.Errors.Single().Field);
    }

    [Fact]
    public async Task Evaluating_a_bid_tells_the_bidder_it_has_been_opened()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        await service.CaptureAsync(t.Khanya, Responsive(1_150_000m), _evaluator, _ct);

        var bid = await context.Submissions.Include(s => s.StatusHistory).SingleAsync(s => s.Id == t.Khanya);
        Assert.Equal(SubmissionStatus.UnderEvaluation, bid.Status);
        Assert.Contains(bid.StatusHistory, h => h.Note!.Contains("opened and is being evaluated"));
    }

    [Fact]
    public async Task The_live_scoresheet_ranks_by_total_points_not_by_price()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);

        var sheet = (await service.GetScoresheetAsync(t.Tender, _ct))!;

        Assert.Equal(EvaluationStage.Evaluating, sheet.Stage);
        Assert.True(sheet.AllEvaluated);
        Assert.Equal(new[] { t.Siyakha, t.Khanya, t.Umhlathi }, sheet.Bids.Select(b => b.SubmissionId));
        Assert.Equal(91.74m, sheet.TopRanked!.Score.TotalPoints);
    }

    // ------------------------------------------------------------------ BEC recommendation

    [Fact]
    public async Task The_recommendation_waits_until_every_bid_is_evaluated()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await service.CaptureAsync(t.Siyakha, Responsive(1_240_000m), _evaluator, _ct);

        var result = await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        Assert.Contains("2 bids have not been evaluated", result.Errors.Single().Message);
    }

    [Fact]
    public async Task Recommending_a_bid_that_is_not_ranked_first_needs_reasons()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);

        var withoutReason = await service.SubmitRecommendationAsync(t.Tender, t.Khanya, "cheaper", _evaluator, _ct);
        Assert.Equal("Reason", withoutReason.Errors.Single().Field);

        var withReason = await service.SubmitRecommendationAsync(t.Tender, t.Khanya,
            "The top-ranked bidder's CSD registration expired on the closing date (verified 30 Sep).", _evaluator, _ct);
        Assert.True(withReason.Succeeded);
    }

    [Fact]
    public async Task Submitting_freezes_the_scoresheet_and_locks_the_evaluation()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);

        Assert.True((await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct)).Succeeded);

        var frozen = await context.BidEvaluations.SingleAsync(e => e.SubmissionId == t.Siyakha);
        Assert.Equal(91.74m, frozen.TotalPoints);
        Assert.Equal(1, frozen.Rank);
        Assert.True(frozen.IsRecommended);

        var sheet = (await service.GetScoresheetAsync(t.Tender, _ct))!;
        Assert.Equal(EvaluationStage.AwaitingAdjudication, sheet.Stage);
        var locked = await service.CaptureAsync(t.Khanya, Responsive(1m), _evaluator, _ct);
        Assert.Contains("locked", locked.Errors.Single().Message);
    }

    [Fact]
    public async Task Without_a_responsive_bid_nothing_can_be_recommended()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        foreach (var bid in new[] { t.Umhlathi, t.Khanya, t.Siyakha })
            await service.CaptureAsync(bid, new BidEvaluationInput(false, "Pricing schedule not signed", null, null), _evaluator, _ct);

        var result = await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        Assert.Contains("No bid is responsive", result.Errors.Single().Message);
    }

    // ------------------------------------------------------------------ BAC decision

    [Fact]
    public async Task The_award_cannot_be_recorded_before_the_bec_has_submitted()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);

        var result = await service.AwardAsync(t.Tender, Award(t.Siyakha), _scmOfficer, _ct);

        Assert.Contains("not submitted its evaluation", result.Errors.Single().Message);
    }

    [Fact]
    public async Task Awarding_records_the_decision_and_tells_every_bidder_the_outcome()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await service.CaptureAsync(t.Umhlathi, new BidEvaluationInput(false, "No valid tax compliance status PIN", null, null), _evaluator, _ct);
        await service.CaptureAsync(t.Khanya, Responsive(1_150_000m), _evaluator, _ct);
        await service.CaptureAsync(t.Siyakha, Responsive(1_240_000m), _evaluator, _ct);
        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        var result = await service.AwardAsync(t.Tender, Award(t.Siyakha), _scmOfficer, _ct);

        Assert.True(result.Succeeded);
        var award = await context.AwardRecords.SingleAsync();
        Assert.Equal(t.Siyakha, award.SubmissionId);
        Assert.Equal(1_240_000m, award.AwardedAmount);
        Assert.Equal("BAC 2026/41", award.CommitteeReference);
        Assert.Equal(TenderStatus.Awarded, (await context.Tenders.SingleAsync(x => x.Id == t.Tender)).Status);

        var bids = await context.Submissions.Include(s => s.StatusHistory).Where(s => s.TenderId == t.Tender).ToListAsync();
        string LastNote(int id) => bids.Single(b => b.Id == id).StatusHistory.OrderBy(h => h.Id).Last().Note!;
        Assert.Equal(SubmissionStatus.Awarded, bids.Single(b => b.Id == t.Siyakha).Status);
        Assert.Equal(SubmissionStatus.Unsuccessful, bids.Single(b => b.Id == t.Khanya).Status);
        Assert.Equal(SubmissionStatus.Unsuccessful, bids.Single(b => b.Id == t.Umhlathi).Status);
        Assert.Contains("R1 240 000.00", LastNote(t.Siyakha));
        Assert.Contains("ranked 2 of 2", LastNote(t.Khanya));
        Assert.Contains("No valid tax compliance status PIN", LastNote(t.Umhlathi));

        var sheet = (await service.GetScoresheetAsync(t.Tender, _ct))!;
        Assert.Equal(EvaluationStage.Awarded, sheet.Stage);
        Assert.True(sheet.Award!.FollowedRecommendation);
        Assert.True(await context.AuditEntries.AnyAsync(a => a.Action == "Tender.Awarded"));
    }

    [Fact]
    public async Task Awarding_against_the_recommendation_needs_full_reasons_and_is_flagged()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);
        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        var brief = await service.AwardAsync(t.Tender, Award(t.Khanya, "Cheaper."), _scmOfficer, _ct);
        Assert.Equal("Rationale", brief.Errors.Single().Field);

        var full = await service.AwardAsync(t.Tender, Award(t.Khanya,
            "The BAC found the recommended bidder restricted by National Treasury on 1 October (database check attached to minute 41)."), _scmOfficer, _ct);
        Assert.True(full.Succeeded);
        Assert.True(await context.AuditEntries.AnyAsync(a => a.Action == "Tender.AwardedAgainstRecommendation"));
    }

    [Fact]
    public async Task A_non_responsive_bid_cannot_be_awarded()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await service.CaptureAsync(t.Umhlathi, new BidEvaluationInput(false, "Late pricing schedule", null, null), _evaluator, _ct);
        await service.CaptureAsync(t.Khanya, Responsive(1_150_000m), _evaluator, _ct);
        await service.CaptureAsync(t.Siyakha, Responsive(1_240_000m), _evaluator, _ct);
        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        var result = await service.AwardAsync(t.Tender, Award(t.Umhlathi, new string('x', 60)), _scmOfficer, _ct);

        Assert.Contains(result.Errors, e => e.Field == "SubmissionId");
    }

    [Fact]
    public async Task The_decision_date_cannot_be_in_the_future_or_before_closing()
    {
        var t = ClosedTenderWithThreeBids(closedDaysAgo: 2);
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);
        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);
        var today = SaTime.ToSast(DateTime.UtcNow).Date;

        var future = await service.AwardAsync(t.Tender, Award(t.Siyakha) with { DecisionDateLocal = today.AddDays(1) }, _scmOfficer, _ct);
        var early = await service.AwardAsync(t.Tender, Award(t.Siyakha) with { DecisionDateLocal = today.AddDays(-10) }, _scmOfficer, _ct);

        Assert.Contains("future", future.Errors.Single().Message);
        Assert.Contains("before the tender closed", early.Errors.Single().Message);
    }

    [Fact]
    public async Task A_withdrawn_bid_is_left_out_of_the_evaluation()
    {
        var t = ClosedTenderWithThreeBids();
        await using (var arrange = _db.Marketplace())
        {
            (await arrange.Submissions.SingleAsync(s => s.Id == t.Umhlathi)).Status = SubmissionStatus.Withdrawn;
            await arrange.SaveChangesAsync();
        }
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        var capture = await service.CaptureAsync(t.Umhlathi, Responsive(100m), _evaluator, _ct);
        await service.CaptureAsync(t.Khanya, Responsive(1_150_000m), _evaluator, _ct);
        await service.CaptureAsync(t.Siyakha, Responsive(1_240_000m), _evaluator, _ct);
        var sheet = (await service.GetScoresheetAsync(t.Tender, _ct))!;

        Assert.Contains("withdrawn", capture.Errors.Single().Message);
        Assert.Equal(2, sheet.Bids.Count);
        Assert.True(sheet.AllEvaluated); // the withdrawn bid does not block the recommendation
        Assert.True((await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct)).Succeeded);
    }

    [Fact]
    public async Task The_recommendation_is_emailed_to_the_organisations_active_scm_officers_only()
    {
        var t = ClosedTenderWithThreeBids();
        const string scmRoleId = "8e445865-a24d-4543-a6c6-9443d048cdb9"; // seeded OrgAdmin role
        var otherOrgAdmin = _db.AddUser(TestDb.Mvlm);
        var deactivated = _db.AddUser(TestDb.Rbidz);
        await using (var arrange = _db.Marketplace())
        {
            foreach (var id in new[] { _scmOfficer, otherOrgAdmin, deactivated })
                arrange.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = id, RoleId = scmRoleId });
            (await arrange.Users.SingleAsync(u => u.Id == deactivated)).LockoutEnd = StaffAccounts.DeactivatedUntil;
            await arrange.SaveChangesAsync();
        }
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);

        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        var email = Assert.Single(_mail.Sent, m => m.Message.Subject.StartsWith("BAC decision needed")).Message;
        Assert.Equal($"{_scmOfficer}@example.test", email.To);
        Assert.Equal($"/Admin/Evaluation/Tender/{t.Tender}", email.LinkUrl);
    }

    [Fact]
    public async Task Every_bidder_is_emailed_the_outcome_with_the_same_note_as_the_timeline()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);
        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        await service.AwardAsync(t.Tender, Award(t.Siyakha), _scmOfficer, _ct);

        var outcomes = _mail.Sent.Where(m => m.Message.Subject.Contains(": TEST/")).Select(m => m.Message).ToList();
        Assert.Equal(3, outcomes.Count);
        Assert.Single(outcomes, m => m.Subject.StartsWith("Awarded"));
        Assert.Contains(outcomes, m => m.Body.Contains("ranked 2 of 3"));
        Assert.All(outcomes, m => Assert.DoesNotContain("R1 380 000", m.Body)); // no competitor's price in the losers' emails
    }

    [Fact]
    public async Task The_bac_can_return_the_evaluation_to_the_bec()
    {
        var t = ClosedTenderWithThreeBids();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await EvaluateAllAsync(service, t);
        await service.SubmitRecommendationAsync(t.Tender, t.Siyakha, null, _evaluator, _ct);

        Assert.False((await service.ReturnToBecAsync(t.Tender, "No", _scmOfficer, _ct)).Succeeded);
        Assert.True((await service.ReturnToBecAsync(t.Tender, "Re-check Siyakha's pricing schedule: VAT not included.", _scmOfficer, _ct)).Succeeded);

        var sheet = (await service.GetScoresheetAsync(t.Tender, _ct))!;
        Assert.Equal(EvaluationStage.Evaluating, sheet.Stage);
        Assert.Equal("Re-check Siyakha's pricing schedule: VAT not included.", sheet.BacReturnNote);
        Assert.Null(sheet.Recommended);
        Assert.True((await service.CaptureAsync(t.Siyakha, Responsive(1_426_000m), _evaluator, _ct)).Succeeded);
    }
}
