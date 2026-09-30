using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Tenancy;
using EProcure.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Services;

/// <summary>Outcome of a tender action: success, "not found", or field errors to show on the form.</summary>
public class ServiceResult
{
    public bool Succeeded => !NotFound && Errors.Count == 0;
    public bool NotFound { get; init; }
    public List<(string Field, string Message)> Errors { get; } = new();
    public int Id { get; init; }

    public static ServiceResult Missing() => new() { NotFound = true };
    public static ServiceResult Ok(int id) => new() { Id = id };
    public ServiceResult With(string field, string message) { Errors.Add((field, message)); return this; }
}

public interface ITenderService
{
    Task<ServiceResult> CreateAsync(TenderFormViewModel form, string userId, CancellationToken ct);
    Task<ServiceResult> UpdateDraftAsync(int id, TenderFormViewModel form, CancellationToken ct);
    Task<ServiceResult> PublishAsync(int id, CancellationToken ct);
    Task<ServiceResult> CancelAsync(int id, string reason, CancellationToken ct);
    IReadOnlyList<TenderDetailsViewModel.PublishCheck> PublishChecks(Tender tender, DateTime nowUtc);

    // Publication approval (four-eyes), when the organisation requires it.
    Task<bool> RequiresApprovalAsync(CancellationToken ct);
    Task<ServiceResult> SetApprovalRuleAsync(bool required, CancellationToken ct);
    Task<ServiceResult> RequestApprovalAsync(int id, string userId, CancellationToken ct);
    Task<ServiceResult> ApproveAsync(int id, string userId, CancellationToken ct);
    Task<ServiceResult> ReturnForChangesAsync(int id, string userId, string? note, CancellationToken ct);
    Task<ServiceResult> WithdrawApprovalRequestAsync(int id, string userId, CancellationToken ct);
    Task<IReadOnlyList<ApprovalQueueRow>> ApprovalQueueAsync(CancellationToken ct);
}

public record ApprovalQueueRow(int TenderId, string ReferenceNumber, string Title, string RequestedBy, string RequestedById,
    DateTime RequestedAtUtc, DateTime ClosingDateUtc);

/// <summary>
/// All business rules for creating, editing, publishing and cancelling tenders.
///
/// Tenant safety: every read goes through the EF tenant filter (an OrgAdmin can only load their own
/// organisation's tenders, so another organisation's id simply "does not exist"), and a NEW tender
/// always gets OrganisationId from ITenantContext, never from the form. IgnoreQueryFilters is never used.
/// </summary>
public class TenderService : ITenderService
{
    /// <summary>A tender must close at least this far in the future when it is saved or published.</summary>
    public static readonly TimeSpan MinimumLeadTime = TimeSpan.FromHours(1);

    private readonly EProcureDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;

    public TenderService(EProcureDbContext db, ITenantContext tenant, IAuditService audit, INotificationService notifications)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _notifications = notifications;
    }

    public async Task<ServiceResult> CreateAsync(TenderFormViewModel form, string userId, CancellationToken ct)
    {
        if (!_tenant.IsOrganisationScoped || _tenant.OrganisationId is not int organisationId)
            return ServiceResult.Missing(); // fail closed: no organisation, no tender

        var result = new ServiceResult();
        var requirements = Validate(form, result);
        var criteria = ValidateFunctionality(form, result);
        if (await ReferenceTakenAsync(form.ReferenceNumber, excludeId: null, ct))
            result.With(nameof(form.ReferenceNumber), "Your organisation already has a tender with this reference.");
        if (!result.Succeeded) return result;

        var tender = new Tender
        {
            OrganisationId = organisationId,   // from the signed-in user's claim, never from the form
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            Status = TenderStatus.Draft
        };
        Apply(form, tender, requirements, criteria);
        _db.Tenders.Add(tender);

        if (!await TrySaveAsync(result, ct)) return result;
        await _audit.LogAsync("Tender.Created", "Tender", tender.Id.ToString(), organisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> UpdateDraftAsync(int id, TenderFormViewModel form, CancellationToken ct)
    {
        var tender = await _db.Tenders.Include(t => t.Requirements).Include(t => t.FunctionalityCriteria).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.Status != TenderStatus.Draft)
            return result.With(string.Empty, "Only draft tenders can be edited. A published tender is locked so every bidder sees the same terms.");
        if (tender.ApprovalRequestedAtUtc is not null)
            return result.With(string.Empty, "This draft is waiting for approval and is locked, so the approver sees exactly what will be published. Withdraw the request to edit it.");

        var requirements = Validate(form, result);
        var criteria = ValidateFunctionality(form, result);
        if (await ReferenceTakenAsync(form.ReferenceNumber, excludeId: id, ct))
            result.With(nameof(form.ReferenceNumber), "Your organisation already has a tender with this reference.");
        if (!result.Succeeded) return result;

        // The checklist is replaced as a whole. Safe because a draft cannot have submissions yet.
        _db.TenderRequirements.RemoveRange(tender.Requirements);
        tender.Requirements.Clear();
        _db.TenderFunctionalityCriteria.RemoveRange(tender.FunctionalityCriteria);
        tender.FunctionalityCriteria.Clear();
        Apply(form, tender, requirements, criteria);

        if (!await TrySaveAsync(result, ct)) return result;
        await _audit.LogAsync("Tender.Updated", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> PublishAsync(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.Include(t => t.Requirements).Include(t => t.FunctionalityCriteria).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        if (await RequiresApprovalAsync(ct))
            return new ServiceResult().With(string.Empty, "Your organisation requires a second SCM Officer to approve a tender before it is published. Submit it for approval.");
        return await PublishCoreAsync(tender, ct);
    }

    /// <summary>The publication itself, with the checks re-run on the server (the page could be hours old).</summary>
    private async Task<ServiceResult> PublishCoreAsync(Tender tender, CancellationToken ct)
    {
        var result = new ServiceResult();
        if (tender.Status != TenderStatus.Draft)
            return result.With(string.Empty, "Only a draft tender can be published.");
        if (PublishProblems(tender) is { Count: > 0 } problems)
        {
            foreach (var problem in problems) result.With(string.Empty, problem);
            return result;
        }

        tender.Status = TenderStatus.Published;
        tender.PublishedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Tender.Published", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    private List<string> PublishProblems(Tender tender) =>
        PublishChecks(tender, DateTime.UtcNow).Where(c => !c.Passed).Select(c => c.Meta).ToList();

    // ------------------------------------------------------------------ publication approval (four-eyes)

    public async Task<bool> RequiresApprovalAsync(CancellationToken ct) =>
        _tenant.OrganisationId is int organisationId
        && await _db.Organisations.Where(o => o.Id == organisationId).Select(o => o.RequireTenderApproval).SingleOrDefaultAsync(ct);

    public async Task<ServiceResult> SetApprovalRuleAsync(bool required, CancellationToken ct)
    {
        if (!_tenant.IsOrganisationScoped || _tenant.OrganisationId is not int organisationId) return ServiceResult.Missing();
        var organisation = await _db.Organisations.SingleAsync(o => o.Id == organisationId, ct);
        if (organisation.RequireTenderApproval == required) return ServiceResult.Ok(organisationId);

        organisation.RequireTenderApproval = required;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Organisation.ApprovalRuleChanged", "Organisation", organisationId.ToString(), organisationId,
            required ? "Publishing now needs a second SCM Officer's approval" : "SCM Officers may publish without a second approval");
        return ServiceResult.Ok(organisationId);
    }

    public async Task<ServiceResult> RequestApprovalAsync(int id, string userId, CancellationToken ct)
    {
        var tender = await _db.Tenders.Include(t => t.Requirements).Include(t => t.FunctionalityCriteria).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (!await RequiresApprovalAsync(ct)) return result.With(string.Empty, "Your organisation does not require approval. Publish the tender directly.");
        if (tender.Status != TenderStatus.Draft) return result.With(string.Empty, "Only a draft tender can be submitted for approval.");
        if (tender.ApprovalRequestedAtUtc is not null) return result.With(string.Empty, "This tender is already waiting for approval.");
        if (PublishProblems(tender) is { Count: > 0 } problems)
        {
            foreach (var problem in problems) result.With(string.Empty, problem);
            return result;
        }

        tender.ApprovalRequestedAtUtc = DateTime.UtcNow;
        tender.ApprovalRequestedByUserId = userId;
        tender.ApprovalReturnNote = null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Tender.ApprovalRequested", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        await _notifications.ApprovalRequestedAsync(tender, userId, ct);
        return ServiceResult.Ok(tender.Id);
    }

    /// <summary>A SECOND SCM Officer approves: the tender is published at once (the checks run again).</summary>
    public async Task<ServiceResult> ApproveAsync(int id, string userId, CancellationToken ct)
    {
        var tender = await _db.Tenders.Include(t => t.Requirements).Include(t => t.FunctionalityCriteria).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.ApprovalRequestedAtUtc is null || tender.Status != TenderStatus.Draft)
            return result.With(string.Empty, "This tender is not waiting for approval.");
        if (tender.ApprovalRequestedByUserId == userId)
            return result.With(string.Empty, "You asked for this approval, so another SCM Officer must give it.");

        var requester = tender.ApprovalRequestedByUserId!;
        tender.ApprovedAtUtc = DateTime.UtcNow;
        tender.ApprovedByUserId = userId;
        tender.ApprovalRequestedAtUtc = null;
        tender.ApprovalRequestedByUserId = null;
        var published = await PublishCoreAsync(tender, ct);
        if (!published.Succeeded) return published; // nothing was saved: still waiting for approval

        await _audit.LogAsync("Tender.Approved", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        await _notifications.ApprovalDecidedAsync(tender, requester, approved: true, note: null, ct);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> ReturnForChangesAsync(int id, string userId, string? note, CancellationToken ct)
    {
        var tender = await _db.Tenders.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.ApprovalRequestedAtUtc is null || tender.Status != TenderStatus.Draft)
            return result.With(string.Empty, "This tender is not waiting for approval.");
        if (tender.ApprovalRequestedByUserId == userId)
            return result.With(string.Empty, "Withdraw your own request instead of sending it back.");
        var trimmed = note?.Trim();
        if (trimmed is null || trimmed.Length < 10 || trimmed.Length > 1000)
            return result.With("ReturnNote", "Say what must change before the tender can be published (10 to 1000 characters).");

        var requester = tender.ApprovalRequestedByUserId!;
        tender.ApprovalRequestedAtUtc = null;
        tender.ApprovalRequestedByUserId = null;
        tender.ApprovalReturnNote = trimmed;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Tender.ApprovalReturned", "Tender", tender.Id.ToString(), tender.OrganisationId, trimmed);
        await _notifications.ApprovalDecidedAsync(tender, requester, approved: false, trimmed, ct);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> WithdrawApprovalRequestAsync(int id, string userId, CancellationToken ct)
    {
        var tender = await _db.Tenders.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.ApprovalRequestedAtUtc is null) return result.With(string.Empty, "This tender is not waiting for approval.");
        if (tender.ApprovalRequestedByUserId != userId) return result.With(string.Empty, "Only the SCM Officer who asked for approval can withdraw the request.");

        tender.ApprovalRequestedAtUtc = null;
        tender.ApprovalRequestedByUserId = null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Tender.ApprovalWithdrawn", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<IReadOnlyList<ApprovalQueueRow>> ApprovalQueueAsync(CancellationToken ct) =>
        await _db.Tenders.AsNoTracking()
            .Where(t => t.Status == TenderStatus.Draft && t.ApprovalRequestedAtUtc != null)
            .OrderBy(t => t.ApprovalRequestedAtUtc)
            .Select(t => new ApprovalQueueRow(t.Id, t.ReferenceNumber, t.Title, t.ApprovalRequestedByUser!.FullName, t.ApprovalRequestedByUserId!,
                t.ApprovalRequestedAtUtc!.Value, t.ClosingDateUtc))
            .ToListAsync(ct);

    public async Task<ServiceResult> CancelAsync(int id, string reason, CancellationToken ct)
    {
        var tender = await _db.Tenders.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.Status is not (TenderStatus.Draft or TenderStatus.Published))
            return result.With(string.Empty, "Only a draft or published tender can be cancelled.");

        // Nothing is deleted: the tender stays on record with its reason (records retention, DECISIONS D10).
        tender.Status = TenderStatus.Cancelled;
        tender.ApprovalRequestedAtUtc = null; // a pending approval request ends with the draft
        tender.ApprovalRequestedByUserId = null;
        tender.CancelledAtUtc = DateTime.UtcNow;
        tender.CancellationReason = reason.Trim();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Tender.Cancelled", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.CancellationReason);
        return ServiceResult.Ok(tender.Id);
    }

    public IReadOnlyList<TenderDetailsViewModel.PublishCheck> PublishChecks(Tender tender, DateTime nowUtc)
    {
        var closesInTime = tender.ClosingDateUtc >= nowUtc.Add(MinimumLeadTime);
        var count = tender.Requirements.Count;
        return new List<TenderDetailsViewModel.PublishCheck>
        {
            new("Closing date is in the future",
                closesInTime ? $"Closes {DisplayFormat.DateTime(tender.ClosingDateUtc)} SAST"
                             : "The closing date must be at least 1 hour from now. Edit the draft to change it.",
                closesInTime),
            new("Required documents are listed",
                count > 0 ? $"{count} document{(count == 1 ? "" : "s")} on the bidder checklist"
                          : "Add at least one required document. Edit the draft to add them.",
                count > 0),
            new("Pre-qualification is set",
                tender.MinimumBbbeeLevel is BbbeeLevel level
                    ? $"Bidders need {EligibilityRules.Describe(level)} or better; weaker levels are blocked"
                    : "No minimum B-BBEE level: any level may apply",
                true),
            new("Evaluation method is set",
                tender.FunctionalityThreshold is int threshold
                    ? $"Functionality first ({tender.FunctionalityCriteria.Count} criteria, minimum {threshold}%), then {EvaluationRules.Describe(tender.PointSystem)} price and preference"
                    : $"{EvaluationRules.Describe(tender.PointSystem)} price and preference, no functionality stage",
                true)
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The functionality stage as entered: NULL when it is not used, otherwise the cleaned criteria (empty rows dropped).
    /// Rules: 1 to 6 criteria, each named and weighted 1-100, weights adding up to exactly 100, threshold 1-100.
    /// </summary>
    public static List<(string Name, int Weight)>? ValidateFunctionality(TenderFormViewModel form, ServiceResult result)
    {
        if (!form.UseFunctionality) return null;

        var rows = form.FunctionalityCriteria
            .Select(c => (Name: c.Name?.Trim() ?? string.Empty, c.Weight))
            .Where(c => c.Name.Length > 0 || c.Weight is not null)
            .ToList();
        const string field = nameof(form.FunctionalityCriteria);
        if (form.FunctionalityThreshold is not (>= 1 and <= 100))
            result.With(nameof(form.FunctionalityThreshold), "Enter the minimum functionality score as a percentage from 1 to 100 (70 is common).");
        if (rows.Count == 0)
            result.With(field, "Add at least one functionality criterion with its weight, or untick functionality.");
        else if (rows.Count > TenderFormViewModel.MaxCriteria)
            result.With(field, $"A tender can have at most {TenderFormViewModel.MaxCriteria} functionality criteria.");
        else if (rows.Any(r => r.Name.Length == 0))
            result.With(field, "Every weighted row needs a criterion name.");
        else if (rows.Any(r => r.Name.Length > 200))
            result.With(field, "Each criterion name must be 200 characters or fewer.");
        else if (rows.Any(r => r.Weight is not (>= 1 and <= 100)))
            result.With(field, "Give every criterion a weight from 1 to 100.");
        else if (rows.Sum(r => r.Weight!.Value) != 100)
            result.With(field, $"The weights must add up to 100. They now add up to {rows.Sum(r => r.Weight!.Value)}.");
        else if (rows.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            result.With(field, "Each criterion must have a different name.");

        return rows.Where(r => r.Weight is not null).Select(r => (r.Name, r.Weight!.Value)).ToList();
    }

    /// <summary>Server-side checks that DataAnnotations cannot express. Returns the cleaned checklist.</summary>
    private static List<string> Validate(TenderFormViewModel form, ServiceResult result)
    {
        if (!TenderCatalog.Categories.Contains(form.Category))
            result.With(nameof(form.Category), "Choose a category from the list.");

        if (form.ClosingDateLocal is DateTime local && SaTime.FromSast(local) < DateTime.UtcNow.Add(MinimumLeadTime))
            result.With(nameof(form.ClosingDateLocal), "The closing date must be at least 1 hour from now.");

        var requirements = BuildRequirements(form);
        if (requirements.Count == 0)
            result.With(nameof(form.SelectedDocuments), "Choose at least one required document.");
        if (requirements.Count > TenderCatalog.MaxRequirements)
            result.With(nameof(form.OtherDocuments), $"A tender can list at most {TenderCatalog.MaxRequirements} required documents.");
        if (requirements.Any(r => r.Length > TenderCatalog.MaxRequirementLength))
            result.With(nameof(form.OtherDocuments), $"Each document name must be {TenderCatalog.MaxRequirementLength} characters or fewer.");

        return requirements;
    }

    /// <summary>
    /// Ticked standard documents (in catalogue order) followed by typed extras, without duplicates. A tender that evaluates
    /// functionality always asks for the technical proposal, because that is what the BEC scores.
    /// </summary>
    public static List<string> BuildRequirements(TenderFormViewModel form)
    {
        var ticked = TenderCatalog.StandardDocuments.Where(d => form.SelectedDocuments.Contains(d));
        var typed = (form.OtherDocuments ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
        var list = ticked.Concat(typed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (form.UseFunctionality && !list.Any(TenderCatalog.IsProposal)) list.Add(TenderCatalog.ProposalDocument);
        return list;
    }

    private static void Apply(TenderFormViewModel form, Tender tender, List<string> requirements, List<(string Name, int Weight)>? criteria)
    {
        tender.Title = form.Title.Trim();
        tender.ReferenceNumber = form.ReferenceNumber.Trim();
        tender.Category = form.Category;
        tender.Description = form.Description.Trim();
        tender.ClosingDateUtc = SaTime.FromSast(form.ClosingDateLocal!.Value);
        tender.TenderFee = form.TenderFee;
        tender.EstimatedValue = form.EstimatedValue;
        tender.MinimumBbbeeLevel = form.MinimumBbbeeLevel;
        tender.PointSystem = form.PointSystem;
        for (var i = 0; i < requirements.Count; i++)
        {
            tender.Requirements.Add(new TenderRequirement { Name = requirements[i], IsMandatory = true, SortOrder = i + 1 });
        }

        // No criteria means no functionality stage: the threshold is only kept together with its criteria.
        tender.FunctionalityThreshold = criteria is null ? null : form.FunctionalityThreshold;
        for (var i = 0; i < (criteria?.Count ?? 0); i++)
        {
            tender.FunctionalityCriteria.Add(new TenderFunctionalityCriterion { Name = criteria![i].Name, Weight = criteria[i].Weight, SortOrder = i + 1 });
        }
    }

    /// <summary>The tenant filter limits this to the admin's own organisation: "unique per organisation".</summary>
    private Task<bool> ReferenceTakenAsync(string reference, int? excludeId, CancellationToken ct)
    {
        var trimmed = reference.Trim();
        return _db.Tenders.AnyAsync(t => t.ReferenceNumber == trimmed && (excludeId == null || t.Id != excludeId), ct);
    }

    /// <summary>
    /// The unique index (OrganisationId, ReferenceNumber) is the real guarantee: if two people save the
    /// same reference at the same moment, the database rejects the second and we show a friendly message.
    /// </summary>
    private async Task<bool> TrySaveAsync(ServiceResult result, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Tenders_OrganisationId_ReferenceNumber") == true)
        {
            result.With(nameof(TenderFormViewModel.ReferenceNumber), "Your organisation already has a tender with this reference.");
            return false;
        }
    }
}
