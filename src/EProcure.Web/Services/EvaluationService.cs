using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Services;

/// <summary>Where a tender is in the evaluation and award process.</summary>
public enum EvaluationStage
{
    NotClosed,            // bids are sealed until the closing date
    Evaluating,           // the BEC is capturing its evaluation
    AwaitingAdjudication, // the BEC has submitted its scoresheet and recommendation to the BAC
    Awarded,
    Cancelled
}

/// <summary>The BEC's input for one bid.</summary>
public record BidEvaluationInput(bool? IsResponsive, string? NonResponsiveReason, decimal? BidPrice, string? Notes);

/// <summary>The BAC decision as captured by the SCM Officer.</summary>
public record AwardInput(int? SubmissionId, string? CommitteeReference, DateTime? DecisionDateLocal, string? Rationale);

/// <summary>One bid on the scoresheet: who, what the BEC recorded, and the calculated (or frozen) points.</summary>
public record ScoresheetBid(int SubmissionId, string? ReferenceNumber, string CompanyName, BbbeeLevel Level,
    SubmissionStatus Status, int RedFlagCount, BidEvaluation? Evaluation, string? EvaluatedBy, ScoreLine Score, bool IsRecommended);

public record AwardSummary(int SubmissionId, string CompanyName, decimal? Amount, decimal? TotalPoints, DateTime DecisionDateUtc,
    string? CommitteeReference, string Rationale, string RecordedBy, DateTime RecordedAtUtc, bool FollowedRecommendation);

/// <summary>A tender's complete scoresheet for the BEC and BAC pages.</summary>
public class Scoresheet
{
    public int TenderId { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTime ClosingDateUtc { get; init; }
    public PreferencePointSystem PointSystem { get; init; }
    public EvaluationStage Stage { get; init; }
    public IReadOnlyList<ScoresheetBid> Bids { get; init; } = Array.Empty<ScoresheetBid>();
    public DateTime? SubmittedAtUtc { get; init; }
    public string? SubmittedBy { get; init; }
    public string? RecommendationReason { get; init; }
    public string? BacReturnNote { get; init; }
    public AwardSummary? Award { get; init; }

    public int EvaluatedCount => Bids.Count(b => b.Evaluation is not null);
    public bool AllEvaluated => Bids.Count > 0 && EvaluatedCount == Bids.Count;
    public ScoresheetBid? TopRanked => Bids.FirstOrDefault(b => b.Score.Rank == 1);
    public ScoresheetBid? Recommended => Bids.FirstOrDefault(b => b.IsRecommended);
}

public record EvaluationListRow(int TenderId, string ReferenceNumber, string Title, DateTime ClosingDateUtc, int Bids, int Evaluated, EvaluationStage Stage);

public interface IEvaluationService
{
    Task<IReadOnlyList<EvaluationListRow>> ListAsync(CancellationToken ct);
    Task<Scoresheet?> GetScoresheetAsync(int tenderId, CancellationToken ct);
    Task<ServiceResult> CaptureAsync(int submissionId, BidEvaluationInput input, string userId, CancellationToken ct);
    Task<ServiceResult> SubmitRecommendationAsync(int tenderId, int? recommendedSubmissionId, string? reason, string userId, CancellationToken ct);
    Task<ServiceResult> ReturnToBecAsync(int tenderId, string? reason, string userId, CancellationToken ct);
    Task<ServiceResult> AwardAsync(int tenderId, AwardInput input, string userId, CancellationToken ct);
}

/// <summary>
/// Bid opening, evaluation (BEC) and adjudication (BAC) for the signed-in user's organisation.
///
/// Rules, all checked here on every request:
///   - bids stay sealed until the closing date: no evaluation (and no opening, see SubmissionsController) before it;
///   - every submitted bid must be evaluated before the BEC can submit; the scoresheet is then frozen;
///   - recommending anything other than the single highest-ranked bid needs written reasons;
///   - only a responsive, scored bid can be awarded; the BAC's reasons, minute reference and date are required;
///   - after the award, every bidder's status and a note explaining the outcome are recorded.
/// Tenant safety comes from the query filters: another organisation's tender or bid is simply "not found".
/// Which ROLE may do what (BEC member captures and recommends, SCM Officer records the BAC decision) is
/// enforced by the controller's policies.
/// </summary>
public class EvaluationService : IEvaluationService
{
    private readonly EProcureDbContext _db;
    private readonly IAuditService _audit;

    public EvaluationService(EProcureDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public static bool IsSealed(DateTime closingUtc, DateTime nowUtc) => closingUtc > nowUtc;

    public static EvaluationStage StageOf(Tender tender, DateTime nowUtc) => tender.Status switch
    {
        TenderStatus.Cancelled => EvaluationStage.Cancelled,
        TenderStatus.Awarded => EvaluationStage.Awarded,
        TenderStatus.Draft => EvaluationStage.NotClosed,
        _ when IsSealed(tender.ClosingDateUtc, nowUtc) => EvaluationStage.NotClosed,
        _ when tender.EvaluationSubmittedAtUtc is not null => EvaluationStage.AwaitingAdjudication,
        _ => EvaluationStage.Evaluating
    };

    public static string Label(EvaluationStage stage) => stage switch
    {
        EvaluationStage.NotClosed => "Sealed until closing",
        EvaluationStage.Evaluating => "BEC evaluating",
        EvaluationStage.AwaitingAdjudication => "Awaiting BAC decision",
        EvaluationStage.Awarded => "Awarded",
        _ => "Cancelled"
    };

    public static string BadgeCss(EvaluationStage stage) => stage switch
    {
        EvaluationStage.Evaluating => "ep-badge ep-badge--navy",
        EvaluationStage.AwaitingAdjudication => "ep-badge ep-badge--warning",
        EvaluationStage.Awarded => "ep-badge ep-badge--success",
        EvaluationStage.NotClosed => "ep-badge ep-badge--info",
        _ => "ep-badge"
    };

    public async Task<IReadOnlyList<EvaluationListRow>> ListAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tenders = await _db.Tenders.AsNoTracking()
            .Where(t => t.Status != TenderStatus.Draft && t.Status != TenderStatus.Cancelled && t.ClosingDateUtc <= now)
            .Select(t => new
            {
                Tender = t,
                Bids = t.Submissions.Count(),
                Evaluated = t.Submissions.Count(s => s.Evaluation != null)
            })
            .ToListAsync(ct);

        return tenders
            .Where(x => x.Bids > 0)
            .Select(x => new EvaluationListRow(x.Tender.Id, x.Tender.ReferenceNumber, x.Tender.Title, x.Tender.ClosingDateUtc,
                x.Bids, x.Evaluated, StageOf(x.Tender, now)))
            .OrderBy(r => r.Stage).ThenByDescending(r => r.ClosingDateUtc)
            .ToList();
    }

    public async Task<Scoresheet?> GetScoresheetAsync(int tenderId, CancellationToken ct)
    {
        var tender = await LoadTenderAsync(tenderId, tracking: false, ct);
        if (tender is null) return null;

        var now = DateTime.UtcNow;
        var stage = StageOf(tender, now);
        var frozen = tender.EvaluationSubmittedAtUtc is not null || tender.Status == TenderStatus.Awarded;
        var scores = frozen ? FrozenScores(tender.Submissions) : LiveScores(tender);

        var evaluators = await NamesAsync(tender.Submissions.Where(s => s.Evaluation is not null).Select(s => s.Evaluation!.EvaluatedByUserId)
            .Append(tender.EvaluationSubmittedByUserId ?? string.Empty), ct);

        var bids = scores.Select(line =>
        {
            var s = tender.Submissions.Single(x => x.Id == line.SubmissionId);
            return new ScoresheetBid(s.Id, s.ReferenceNumber, s.Company.Name, s.DeclaredBbbeeLevel, s.Status,
                SubmissionStatuses.RedFlags(s).Count, s.Evaluation,
                s.Evaluation is null ? null : evaluators.GetValueOrDefault(s.Evaluation.EvaluatedByUserId),
                line, s.Evaluation?.IsRecommended == true);
        }).ToList();

        AwardSummary? award = null;
        var record = await _db.AwardRecords.AsNoTracking()
            .Where(a => a.TenderId == tenderId)
            .Select(a => new { a.SubmissionId, a.AwardedAmount, a.DecisionDateUtc, a.CommitteeReference, a.Rationale, a.RecordedAtUtc, RecordedBy = a.RecordedByUser.FullName })
            .SingleOrDefaultAsync(ct);
        if (record is not null)
        {
            var winner = bids.Single(b => b.SubmissionId == record.SubmissionId);
            award = new AwardSummary(record.SubmissionId, winner.CompanyName, record.AwardedAmount, winner.Score.TotalPoints,
                record.DecisionDateUtc, record.CommitteeReference, record.Rationale, record.RecordedBy, record.RecordedAtUtc, winner.IsRecommended);
        }

        return new Scoresheet
        {
            TenderId = tender.Id,
            ReferenceNumber = tender.ReferenceNumber,
            Title = tender.Title,
            ClosingDateUtc = tender.ClosingDateUtc,
            PointSystem = tender.PointSystem,
            Stage = stage,
            Bids = bids,
            SubmittedAtUtc = tender.EvaluationSubmittedAtUtc,
            SubmittedBy = tender.EvaluationSubmittedByUserId is null ? null : evaluators.GetValueOrDefault(tender.EvaluationSubmittedByUserId),
            RecommendationReason = tender.RecommendationReason,
            BacReturnNote = tender.BacReturnNote,
            Award = award
        };
    }

    public async Task<ServiceResult> CaptureAsync(int submissionId, BidEvaluationInput input, string userId, CancellationToken ct)
    {
        var submission = await _db.Submissions
            .Include(s => s.Tender)
            .Include(s => s.Evaluation)
            .SingleOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (StageMessage(StageOf(submission.Tender, DateTime.UtcNow), EvaluationStage.Evaluating) is string blocked)
            return result.With(string.Empty, blocked);

        var reason = input.NonResponsiveReason?.Trim();
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (input.IsResponsive is null)
            result.With(nameof(input.IsResponsive), "Record whether the bid is responsive.");
        else if (input.IsResponsive.Value && (input.BidPrice is null || input.BidPrice <= 0))
            result.With(nameof(input.BidPrice), "Enter the total bid price (incl. VAT) from the bidder's pricing schedule.");
        else if (input.IsResponsive.Value && input.BidPrice >= 1_000_000_000_000m)
            result.With(nameof(input.BidPrice), "The bid price is too large. Check the amount.");
        else if (!input.IsResponsive.Value && (reason is null || reason.Length < 5))
            result.With(nameof(input.NonResponsiveReason), "Give the reason the bid is not responsive. The bidder is told this reason.");
        if (reason?.Length > 1000) result.With(nameof(input.NonResponsiveReason), "The reason must be 1000 characters or fewer.");
        if (notes?.Length > 2000) result.With(nameof(input.Notes), "Notes must be 2000 characters or fewer.");
        if (!result.Succeeded) return result;

        var evaluation = submission.Evaluation ?? new BidEvaluation { SubmissionId = submission.Id };
        evaluation.IsResponsive = input.IsResponsive!.Value;
        evaluation.BidPrice = evaluation.IsResponsive ? Math.Round(input.BidPrice!.Value, 2) : null;
        evaluation.NonResponsiveReason = evaluation.IsResponsive ? null : reason;
        evaluation.Notes = notes;
        evaluation.EvaluatedByUserId = userId;
        evaluation.EvaluatedAtUtc = DateTime.UtcNow;
        if (submission.Evaluation is null) _db.BidEvaluations.Add(evaluation);

        // The bidder sees that the bid has been opened and is being evaluated (no scores are shown to bidders).
        if (submission.Status == SubmissionStatus.Submitted)
            ChangeStatus(submission, SubmissionStatus.UnderEvaluation, userId, "Your bid has been opened and is being evaluated by the Bid Evaluation Committee.");

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Evaluation.Captured", "Submission", submission.Id.ToString(), submission.Tender.OrganisationId,
            evaluation.IsResponsive
                ? $"{submission.ReferenceNumber}: responsive, price {DisplayFormat.Money(evaluation.BidPrice!.Value)}"
                : $"{submission.ReferenceNumber}: not responsive ({evaluation.NonResponsiveReason})");
        return ServiceResult.Ok(submission.Id);
    }

    public async Task<ServiceResult> SubmitRecommendationAsync(int tenderId, int? recommendedSubmissionId, string? reason, string userId, CancellationToken ct)
    {
        var tender = await LoadTenderAsync(tenderId, tracking: true, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (StageMessage(StageOf(tender, DateTime.UtcNow), EvaluationStage.Evaluating) is string blocked)
            return result.With(string.Empty, blocked);

        var missing = tender.Submissions.Count(s => s.Evaluation is null);
        if (missing > 0)
            return result.With(string.Empty, $"{missing} bid{(missing == 1 ? " has" : "s have")} not been evaluated yet. Every bid must be evaluated before the recommendation is submitted.");

        var scores = LiveScores(tender);
        if (!scores.Any(s => s.Rank is not null))
            return result.With(string.Empty, "No bid is responsive, so none can be recommended for award. Record this with the SCM Officer (the tender can be cancelled and re-advertised).");

        var chosen = scores.FirstOrDefault(s => s.SubmissionId == recommendedSubmissionId && s.Rank is not null);
        if (chosen is null)
            return result.With("RecommendedSubmissionId", "Choose a responsive bid to recommend.");

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var needsReason = chosen.Rank != 1 || chosen.TiedForRank;
        if (needsReason && (trimmed is null || trimmed.Length < 20))
            return result.With("Reason", chosen.TiedForRank
                ? "These bids are tied on points. Record how the tie was broken (the regulations require drawing lots), in at least 20 characters."
                : "You are recommending a bid that is not ranked first. Give the objective reasons for this, in at least 20 characters.");
        if (trimmed?.Length > 2000) return result.With("Reason", "The reasons must be 2000 characters or fewer.");

        // Freeze the scoresheet exactly as the BEC signs it off.
        foreach (var line in scores)
        {
            var evaluation = tender.Submissions.Single(s => s.Id == line.SubmissionId).Evaluation!;
            evaluation.PricePoints = line.PricePoints;
            evaluation.PreferencePoints = line.PreferencePoints;
            evaluation.TotalPoints = line.TotalPoints;
            evaluation.Rank = line.Rank;
            evaluation.IsRecommended = line.SubmissionId == chosen.SubmissionId;
        }
        tender.EvaluationSubmittedAtUtc = DateTime.UtcNow;
        tender.EvaluationSubmittedByUserId = userId;
        tender.RecommendationReason = trimmed;
        tender.BacReturnNote = null;
        await _db.SaveChangesAsync(ct);

        var recommended = tender.Submissions.Single(s => s.Id == chosen.SubmissionId);
        await _audit.LogAsync("Evaluation.Submitted", "Tender", tender.Id.ToString(), tender.OrganisationId,
            $"{tender.ReferenceNumber}: recommends {recommended.ReferenceNumber} {recommended.Company.Name}, {chosen.TotalPoints} points, rank {chosen.Rank}"
            + (trimmed is null ? string.Empty : $". Reasons: {trimmed}"));
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> ReturnToBecAsync(int tenderId, string? reason, string userId, CancellationToken ct)
    {
        var tender = await LoadTenderAsync(tenderId, tracking: true, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (StageMessage(StageOf(tender, DateTime.UtcNow), EvaluationStage.AwaitingAdjudication) is string blocked)
            return result.With(string.Empty, blocked);
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < 10 || trimmed.Length > 2000)
            return result.With("ReturnReason", "Tell the BEC what must be reconsidered (10 to 2000 characters).");

        foreach (var evaluation in tender.Submissions.Select(s => s.Evaluation).OfType<BidEvaluation>())
        {
            evaluation.PricePoints = evaluation.PreferencePoints = evaluation.TotalPoints = null;
            evaluation.Rank = null;
            evaluation.IsRecommended = false;
        }
        tender.EvaluationSubmittedAtUtc = null;
        tender.EvaluationSubmittedByUserId = null;
        tender.RecommendationReason = null;
        tender.BacReturnNote = trimmed;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Evaluation.Returned", "Tender", tender.Id.ToString(), tender.OrganisationId, $"{tender.ReferenceNumber}: {trimmed}");
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> AwardAsync(int tenderId, AwardInput input, string userId, CancellationToken ct)
    {
        var tender = await LoadTenderAsync(tenderId, tracking: true, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (StageMessage(StageOf(tender, DateTime.UtcNow), EvaluationStage.AwaitingAdjudication) is string blocked)
            return result.With(string.Empty, blocked);

        var winner = tender.Submissions.FirstOrDefault(s => s.Id == input.SubmissionId && s.Evaluation?.Rank is not null);
        if (winner is null) result.With(nameof(input.SubmissionId), "Choose a responsive bid from the scoresheet.");

        var reference = input.CommitteeReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > 100)
            result.With(nameof(input.CommitteeReference), "Enter the BAC minute or resolution reference (up to 100 characters).");

        // The BAC decides on a calendar day (South African time); stored as UTC like every other date.
        var decisionDate = input.DecisionDateLocal?.Date;
        if (decisionDate is null)
            result.With(nameof(input.DecisionDateLocal), "Enter the date of the BAC decision.");
        else if (decisionDate > SaTime.ToSast(DateTime.UtcNow).Date)
            result.With(nameof(input.DecisionDateLocal), "The decision date cannot be in the future.");
        else if (decisionDate < SaTime.ToSast(tender.ClosingDateUtc).Date)
            result.With(nameof(input.DecisionDateLocal), "The decision date cannot be before the tender closed.");

        var deviates = winner is not null && winner.Evaluation!.IsRecommended == false;
        var rationale = input.Rationale?.Trim();
        var minimum = deviates ? 50 : 10;
        if (rationale is null || rationale.Length < minimum || rationale.Length > 4000)
            result.With(nameof(input.Rationale), deviates
                ? "You are awarding to a bid the BEC did not recommend. Record the BAC's objective reasons in full (at least 50 characters)."
                : "Record the BAC's reasons for the decision (10 to 4000 characters).");
        if (!result.Succeeded) return result;

        var organisationName = await _db.Organisations.Where(o => o.Id == tender.OrganisationId).Select(o => o.Name).SingleAsync(ct);
        var winningEvaluation = winner!.Evaluation!;
        _db.AwardRecords.Add(new AwardRecord
        {
            TenderId = tender.Id,
            SubmissionId = winner.Id,
            DecisionDateUtc = SaTime.FromSast(decisionDate!.Value),
            CommitteeReference = reference,
            Rationale = rationale!,
            AwardedAmount = winningEvaluation.BidPrice,
            RecordedByUserId = userId,
            RecordedAtUtc = DateTime.UtcNow
        });
        tender.Status = TenderStatus.Awarded;

        var ranked = tender.Submissions.Count(s => s.Evaluation?.Rank is not null);
        foreach (var bid in tender.Submissions)
        {
            if (bid.Id == winner.Id)
            {
                ChangeStatus(bid, SubmissionStatus.Awarded, userId,
                    $"Awarded: {organisationName} awarded this tender to your company for {DisplayFormat.Money(winningEvaluation.BidPrice!.Value)} (decision reference {reference}). You will be contacted about the contract.");
            }
            else if (bid.Evaluation is { IsResponsive: false } excluded)
            {
                ChangeStatus(bid, SubmissionStatus.Unsuccessful, userId, $"Not awarded: your bid was found non-responsive. Reason: {excluded.NonResponsiveReason}");
            }
            else
            {
                ChangeStatus(bid, SubmissionStatus.Unsuccessful, userId,
                    $"Not awarded: the tender was awarded to {winner.Company.Name} ({DisplayFormat.Points(winningEvaluation.TotalPoints)} points). " +
                    $"Your bid scored {DisplayFormat.Points(bid.Evaluation?.TotalPoints)} points and ranked {bid.Evaluation?.Rank} of {ranked}.");
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(deviates ? "Tender.AwardedAgainstRecommendation" : "Tender.Awarded", "Tender", tender.Id.ToString(), tender.OrganisationId,
            $"{tender.ReferenceNumber} awarded to {winner.ReferenceNumber} {winner.Company.Name} for {DisplayFormat.Money(winningEvaluation.BidPrice!.Value)}, decision reference {reference}");
        return ServiceResult.Ok(tender.Id);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Loads the tender with every bid the organisation can see (tenant and "submitted only" filters apply).</summary>
    private Task<Tender?> LoadTenderAsync(int tenderId, bool tracking, CancellationToken ct)
    {
        var query = _db.Tenders
            .Include(t => t.Submissions).ThenInclude(s => s.Company)
            .Include(t => t.Submissions).ThenInclude(s => s.Evaluation)
            .AsSplitQuery();
        if (!tracking) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(t => t.Id == tenderId, ct);
    }

    private static IReadOnlyList<ScoreLine> LiveScores(Tender tender) =>
        EvaluationRules.Score(tender.Submissions.Select(s => new BidInput(s.Id, s.Evaluation is not null,
            s.Evaluation?.IsResponsive == true, s.Evaluation?.BidPrice, s.DeclaredBbbeeLevel)), tender.PointSystem);

    /// <summary>The scoresheet as the BEC signed it off (points and ranks stored at submission).</summary>
    private static IReadOnlyList<ScoreLine> FrozenScores(IEnumerable<Submission> submissions)
    {
        var list = submissions.ToList();
        return list
            .Select(s => new ScoreLine(s.Id, s.Evaluation is not null, s.Evaluation?.IsResponsive == true, s.Evaluation?.BidPrice,
                s.Evaluation?.PricePoints, s.Evaluation?.PreferencePoints, s.Evaluation?.TotalPoints, s.Evaluation?.Rank,
                s.Evaluation?.Rank is int rank && list.Count(o => o.Evaluation?.Rank == rank) > 1))
            .OrderBy(l => l.Rank ?? int.MaxValue).ThenBy(l => l.SubmissionId)
            .ToList();
    }

    private static string? StageMessage(EvaluationStage actual, EvaluationStage required) => actual == required ? null : actual switch
    {
        EvaluationStage.NotClosed => "The bids are sealed until the tender's closing date.",
        EvaluationStage.Evaluating => "The BEC has not submitted its evaluation yet.",
        EvaluationStage.AwaitingAdjudication => "The evaluation has been submitted to the BAC and is locked.",
        EvaluationStage.Awarded => "This tender has already been awarded.",
        _ => "This tender has been cancelled."
    };

    private static void ChangeStatus(Submission submission, SubmissionStatus next, string userId, string note)
    {
        submission.StatusHistory.Add(new SubmissionStatusHistory
        {
            FromStatus = submission.Status,
            ToStatus = next,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow,
            Note = note
        });
        submission.Status = next;
    }

    private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        return await _db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }
}
