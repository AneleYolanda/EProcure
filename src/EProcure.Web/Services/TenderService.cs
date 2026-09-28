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
}

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

    public TenderService(EProcureDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<ServiceResult> CreateAsync(TenderFormViewModel form, string userId, CancellationToken ct)
    {
        if (!_tenant.IsOrganisationScoped || _tenant.OrganisationId is not int organisationId)
            return ServiceResult.Missing(); // fail closed: no organisation, no tender

        var result = new ServiceResult();
        var requirements = Validate(form, result);
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
        Apply(form, tender, requirements);
        _db.Tenders.Add(tender);

        if (!await TrySaveAsync(result, ct)) return result;
        await _audit.LogAsync("Tender.Created", "Tender", tender.Id.ToString(), organisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> UpdateDraftAsync(int id, TenderFormViewModel form, CancellationToken ct)
    {
        var tender = await _db.Tenders.Include(t => t.Requirements).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.Status != TenderStatus.Draft)
            return result.With(string.Empty, "Only draft tenders can be edited. A published tender is locked so every bidder sees the same terms.");

        var requirements = Validate(form, result);
        if (await ReferenceTakenAsync(form.ReferenceNumber, excludeId: id, ct))
            result.With(nameof(form.ReferenceNumber), "Your organisation already has a tender with this reference.");
        if (!result.Succeeded) return result;

        // The checklist is replaced as a whole. Safe because a draft cannot have submissions yet.
        _db.TenderRequirements.RemoveRange(tender.Requirements);
        tender.Requirements.Clear();
        Apply(form, tender, requirements);

        if (!await TrySaveAsync(result, ct)) return result;
        await _audit.LogAsync("Tender.Updated", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> PublishAsync(int id, CancellationToken ct)
    {
        var tender = await _db.Tenders.Include(t => t.Requirements).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.Status != TenderStatus.Draft)
            return result.With(string.Empty, "Only a draft tender can be published.");

        // Re-check on the server at the moment of publishing (the page could be hours old).
        foreach (var failed in PublishChecks(tender, DateTime.UtcNow).Where(c => !c.Passed))
            result.With(string.Empty, failed.Meta);
        if (!result.Succeeded) return result;

        tender.Status = TenderStatus.Published;
        tender.PublishedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Tender.Published", "Tender", tender.Id.ToString(), tender.OrganisationId, tender.ReferenceNumber);
        return ServiceResult.Ok(tender.Id);
    }

    public async Task<ServiceResult> CancelAsync(int id, string reason, CancellationToken ct)
    {
        var tender = await _db.Tenders.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tender is null) return ServiceResult.Missing();

        var result = new ServiceResult();
        if (tender.Status is not (TenderStatus.Draft or TenderStatus.Published))
            return result.With(string.Empty, "Only a draft or published tender can be cancelled.");

        // Nothing is deleted: the tender stays on record with its reason (records retention, DECISIONS D10).
        tender.Status = TenderStatus.Cancelled;
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
                true)
        };
    }

    // ------------------------------------------------------------------ helpers

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

    /// <summary>Ticked standard documents (in catalogue order) followed by typed extras, without duplicates.</summary>
    public static List<string> BuildRequirements(TenderFormViewModel form)
    {
        var ticked = TenderCatalog.StandardDocuments.Where(d => form.SelectedDocuments.Contains(d));
        var typed = (form.OtherDocuments ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
        return ticked.Concat(typed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Apply(TenderFormViewModel form, Tender tender, List<string> requirements)
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
