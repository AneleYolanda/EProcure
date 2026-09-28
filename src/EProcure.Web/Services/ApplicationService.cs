using System.Security.Cryptography;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Services;

public enum StartOutcome { Started, Existing, NoCompany, TenderNotFound, Closed, NotEligible }

public record StartResult(StartOutcome Outcome, int SubmissionId = 0, string? Reason = null);

/// <summary>
/// Applying for a tender (journey step 5): start, answer the SBD questions, upload the checklist PDFs,
/// declare, pay, submit. Every rule lives here; the controller only moves between pages.
///
/// The hard rules, re-checked on EVERY change, not only when the page is shown:
///   - only the company that owns the application can see or change it (otherwise "not found");
///   - the tender must be published and still open (nothing is accepted after the closing date);
///   - the company must meet the tender's minimum B-BBEE level (hard stop, no override);
///   - one application per company per tender (unique index as the final guarantee);
///   - the organisation only sees the application once it is Submitted (fee paid, or no fee).
/// </summary>
public interface IApplicationService
{
    Task<StartResult> StartAsync(int tenderId, string userId, CancellationToken ct);
    Task<Submission?> LoadAsync(int submissionId, string userId, CancellationToken ct);
    string? CheckCanChange(Submission submission, DateTime nowUtc);
    Task<ServiceResult> SaveComplianceAsync(Submission submission, bool csdRegistered, bool taxCompliant, CancellationToken ct);
    Task<ServiceResult> SaveDeclarationsAsync(Submission submission, bool hasInterest, string? interestDetails, bool onRestrictedList, string? restrictionDetails, bool independentBid, CancellationToken ct);
    Task<ServiceResult> UploadAsync(Submission submission, int requirementId, IFormFile file, string userId, CancellationToken ct);
    Task<ServiceResult> RemoveDocumentAsync(Submission submission, int documentId, CancellationToken ct);
    IReadOnlyList<string> MissingItems(Submission submission);
    Task<ServiceResult> SubmitAsync(Submission submission, bool declared, string userId, CancellationToken ct);
    Task<(ServiceResult Result, string? RedirectUrl)> StartPaymentAsync(Submission submission, string method, string returnUrl, CancellationToken ct);
    Task<ServiceResult> CompletePaymentAsync(Submission submission, string reference, string userId, CancellationToken ct);
}

public class ApplicationService : IApplicationService
{
    private readonly EProcureDbContext _db;
    private readonly IAuditService _audit;
    private readonly IFileStorage _files;
    private readonly IPaymentGateway _payments;
    private readonly UploadOptions _uploads;

    public ApplicationService(EProcureDbContext db, IAuditService audit, IFileStorage files, IPaymentGateway payments, IOptions<UploadOptions> uploads)
    {
        _db = db;
        _audit = audit;
        _files = files;
        _payments = payments;
        _uploads = uploads.Value;
    }

    public async Task<StartResult> StartAsync(int tenderId, string userId, CancellationToken ct)
    {
        var company = await CompanyOfAsync(userId, ct);
        if (company is null) return new(StartOutcome.NoCompany);

        // Suppliers are not organisation-scoped, so the Published check must be explicit here.
        var tender = await _db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenderId && t.Status == TenderStatus.Published, ct);
        if (tender is null) return new(StartOutcome.TenderNotFound);
        if (tender.ClosingDateUtc <= DateTime.UtcNow) return new(StartOutcome.Closed);

        var eligibility = EligibilityRules.CheckBbbee(company.BbbeeLevel, tender.MinimumBbbeeLevel);
        if (eligibility.Outcome == EligibilityOutcome.NotEligible)
        {
            await _audit.LogAsync("Eligibility.Blocked", "Tender", tender.Id.ToString(), null, $"{company.Name}: {eligibility.Reason}");
            return new(StartOutcome.NotEligible, Reason: eligibility.Reason);
        }

        var existing = await _db.Submissions.Where(s => s.TenderId == tenderId && s.CompanyId == company.Id).Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (existing != 0) return new(StartOutcome.Existing, existing);

        var submission = new Submission
        {
            TenderId = tender.Id,
            CompanyId = company.Id,
            SubmittedByUserId = userId,
            Status = SubmissionStatus.Draft,
            DeclaredBbbeeLevel = company.BbbeeLevel,
            PaymentStatus = tender.TenderFee == 0 ? PaymentStatus.NotRequired : PaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
        AddHistory(submission, null, SubmissionStatus.Draft, userId, "Application started");
        _db.Submissions.Add(submission);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Submissions_TenderId_CompanyId") == true)
        {
            // Two clicks at the same moment: the unique index stopped the second copy. Use the first.
            _db.ChangeTracker.Clear();
            var id = await _db.Submissions.Where(s => s.TenderId == tenderId && s.CompanyId == company.Id).Select(s => s.Id).FirstAsync(ct);
            return new(StartOutcome.Existing, id);
        }

        // Organisation id deliberately NULL: an unpaid draft is private to the supplier (the organisation's
        // audit view must not reveal who has started applying). "Submission.Submitted" carries the org id.
        await _audit.LogAsync("Submission.Created", "Submission", submission.Id.ToString(), null, tender.ReferenceNumber);
        return new(StartOutcome.Started, submission.Id);
    }

    public async Task<Submission?> LoadAsync(int submissionId, string userId, CancellationToken ct)
    {
        var company = await CompanyOfAsync(userId, ct);
        if (company is null) return null;

        // Ownership is part of the query itself: another company's application simply is not found.
        return await _db.Submissions
            .Include(s => s.Tender).ThenInclude(t => t.Organisation)
            .Include(s => s.Tender).ThenInclude(t => t.Requirements)
            .Include(s => s.Company)
            .Include(s => s.Documents)
            .SingleOrDefaultAsync(s => s.Id == submissionId && s.CompanyId == company.Id, ct);
    }

    public string? CheckCanChange(Submission submission, DateTime nowUtc)
    {
        if (submission.Status != SubmissionStatus.Draft)
            return "This application has already been completed and can no longer be changed.";
        if (submission.Tender.Status != TenderStatus.Published || submission.Tender.ClosingDateUtc <= nowUtc)
            return $"This tender closed on {DisplayFormat.DateTime(submission.Tender.ClosingDateUtc)}. Applications can no longer be changed or submitted.";
        var eligibility = EligibilityRules.CheckBbbee(submission.Company.BbbeeLevel, submission.Tender.MinimumBbbeeLevel);
        return eligibility.Outcome == EligibilityOutcome.NotEligible ? eligibility.Reason : null;
    }

    public async Task<ServiceResult> SaveComplianceAsync(Submission submission, bool csdRegistered, bool taxCompliant, CancellationToken ct)
    {
        if (CheckCanChange(submission, DateTime.UtcNow) is string blocked) return new ServiceResult().With(string.Empty, blocked);
        submission.IsCsdRegistered = csdRegistered;
        submission.IsTaxCompliant = taxCompliant;
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok(submission.Id);
    }

    public async Task<ServiceResult> SaveDeclarationsAsync(Submission submission, bool hasInterest, string? interestDetails,
        bool onRestrictedList, string? restrictionDetails, bool independentBid, CancellationToken ct)
    {
        if (CheckCanChange(submission, DateTime.UtcNow) is string blocked) return new ServiceResult().With(string.Empty, blocked);

        var result = new ServiceResult();
        if (hasInterest && string.IsNullOrWhiteSpace(interestDetails))
            result.With("InterestDetails", "Give the name of the person and the nature of the relationship.");
        if (onRestrictedList && string.IsNullOrWhiteSpace(restrictionDetails))
            result.With("RestrictionDetails", "Explain the listing (when, by whom and why).");
        if (!result.Succeeded) return result;

        submission.HasDeclaredInterest = hasInterest;
        submission.InterestDetails = hasInterest ? interestDetails!.Trim() : null;
        submission.ConfirmsNotRestricted = !onRestrictedList;
        submission.RestrictionDetails = onRestrictedList ? restrictionDetails!.Trim() : null;
        submission.ConfirmsIndependentBid = independentBid;
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok(submission.Id);
    }

    public async Task<ServiceResult> UploadAsync(Submission submission, int requirementId, IFormFile file, string userId, CancellationToken ct)
    {
        var result = new ServiceResult();
        if (CheckCanChange(submission, DateTime.UtcNow) is string blocked) return result.With(string.Empty, blocked);

        // The requirement must belong to THIS tender (a crafted form could send any id).
        var requirement = submission.Tender.Requirements.SingleOrDefault(r => r.Id == requirementId);
        if (requirement is null) return result.With(string.Empty, "That document is not on this tender's checklist.");

        // Read into memory (max 5 MB) so we can check the real content, hash it, then store it.
        await using var buffer = new MemoryStream();
        await using (var input = file.OpenReadStream())
        {
            await input.CopyToAsync(buffer, ct);
        }
        var bytes = buffer.ToArray();
        if (PdfValidator.Check(file.FileName, file.ContentType, bytes.AsSpan(0, Math.Min(bytes.Length, 8)), bytes.LongLength, _uploads.MaxFileSizeBytes) is string invalid)
            return result.With(string.Empty, $"{requirement.Name}: {invalid}");

        // Uploading again for the same checklist item replaces the earlier file.
        var previous = submission.Documents.Where(d => d.TenderRequirementId == requirementId).ToList();
        foreach (var old in previous)
        {
            await _files.DeleteAsync(old.StorageKey, ct);
            _db.UploadedDocuments.Remove(old);
        }

        buffer.Position = 0;
        var key = await _files.SaveAsync(buffer, ".pdf", ct);
        submission.Documents.Add(new UploadedDocument
        {
            TenderRequirementId = requirementId,
            OriginalFileName = SafeDisplayName(file.FileName),
            StorageKey = key,
            ContentType = "application/pdf",
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedByUserId = userId,
            UploadedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Document.Uploaded", "Submission", submission.Id.ToString(), null, requirement.Name);
        return ServiceResult.Ok(submission.Id);
    }

    public async Task<ServiceResult> RemoveDocumentAsync(Submission submission, int documentId, CancellationToken ct)
    {
        if (CheckCanChange(submission, DateTime.UtcNow) is string blocked) return new ServiceResult().With(string.Empty, blocked);
        var document = submission.Documents.SingleOrDefault(d => d.Id == documentId);
        if (document is null) return ServiceResult.Missing();

        await _files.DeleteAsync(document.StorageKey, ct);
        _db.UploadedDocuments.Remove(document);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Document.Removed", "Submission", submission.Id.ToString(), null, document.OriginalFileName);
        return ServiceResult.Ok(submission.Id);
    }

    /// <summary>Everything still missing before the application can be submitted (shown on the Review step).</summary>
    public IReadOnlyList<string> MissingItems(Submission s)
    {
        var missing = new List<string>();
        if (s.IsCsdRegistered is null || s.IsTaxCompliant is null) missing.Add("Answer the compliance questions (step 2).");
        if (s.HasDeclaredInterest is null || s.ConfirmsNotRestricted is null || s.ConfirmsIndependentBid is null)
            missing.Add("Answer the SBD declarations (step 3).");
        var uploaded = s.Documents.Where(d => d.TenderRequirementId != null).Select(d => d.TenderRequirementId!.Value).ToHashSet();
        missing.AddRange(s.Tender.Requirements
            .Where(r => r.IsMandatory && !uploaded.Contains(r.Id))
            .OrderBy(r => r.SortOrder)
            .Select(r => $"Upload: {r.Name} (step 4)."));
        return missing;
    }

    public async Task<ServiceResult> SubmitAsync(Submission submission, bool declared, string userId, CancellationToken ct)
    {
        var result = new ServiceResult();
        if (CheckCanChange(submission, DateTime.UtcNow) is string blocked) return result.With(string.Empty, blocked);
        foreach (var item in MissingItems(submission)) result.With(string.Empty, item);
        if (!declared) result.With("Declared", "Tick the declaration to confirm the information is true and correct.");
        if (!result.Succeeded) return result;

        submission.DeclaredAtUtc = DateTime.UtcNow;
        submission.DeclaredBbbeeLevel = submission.Company.BbbeeLevel; // the level at the moment of declaring

        if (submission.Tender.TenderFee == 0)
        {
            Finalise(submission, userId, "Submitted (no tender fee)");
        }
        else
        {
            AddHistory(submission, submission.Status, SubmissionStatus.AwaitingPayment, userId, "Declaration signed; waiting for the tender fee");
            submission.Status = SubmissionStatus.AwaitingPayment;
        }
        await _db.SaveChangesAsync(ct);
        await AuditSubmittedIfDoneAsync(submission);
        return ServiceResult.Ok(submission.Id);
    }

    public async Task<(ServiceResult Result, string? RedirectUrl)> StartPaymentAsync(Submission submission, string method, string returnUrl, CancellationToken ct)
    {
        var result = new ServiceResult();
        if (submission.Status != SubmissionStatus.AwaitingPayment) return (result.With(string.Empty, "This application is not waiting for payment."), null);
        if (submission.Tender.ClosingDateUtc <= DateTime.UtcNow)
            return (result.With(string.Empty, $"The tender closed on {DisplayFormat.DateTime(submission.Tender.ClosingDateUtc)}; the fee can no longer be paid."), null);

        var start = await _payments.StartAsync(new PaymentRequest(submission.Id, submission.Tender.TenderFee,
            $"Tender fee {submission.Tender.ReferenceNumber}", method, returnUrl), ct);

        // Only the provider's reference is stored: never card or bank details.
        submission.PaymentReference = start.Reference;
        submission.PaymentStatus = PaymentStatus.Pending;
        await _db.SaveChangesAsync(ct);
        return (ServiceResult.Ok(submission.Id), start.RedirectUrl);
    }

    public async Task<ServiceResult> CompletePaymentAsync(Submission submission, string reference, string userId, CancellationToken ct)
    {
        var result = new ServiceResult();
        if (submission.Status == SubmissionStatus.Submitted && submission.PaymentReference == reference)
            return ServiceResult.Ok(submission.Id); // already done (e.g. the page was refreshed)
        if (submission.Status != SubmissionStatus.AwaitingPayment || submission.PaymentReference != reference)
            return result.With(string.Empty, "That payment does not belong to this application.");

        // Never trust the browser: ask the payment provider what really happened.
        var verification = await _payments.VerifyAsync(reference, ct);
        if (verification.Outcome == PaymentOutcome.Paid && verification.AmountPaid == submission.Tender.TenderFee)
        {
            submission.PaymentStatus = PaymentStatus.Paid;
            submission.AmountPaid = verification.AmountPaid;
            submission.PaidAtUtc = DateTime.UtcNow;
            Finalise(submission, userId, $"Tender fee {DisplayFormat.Fee(verification.AmountPaid)} verified ({reference})");
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("Submission.Paid", "Submission", submission.Id.ToString(), submission.Tender.OrganisationId, reference);
            await AuditSubmittedIfDoneAsync(submission);
            return ServiceResult.Ok(submission.Id);
        }

        if (verification.Outcome == PaymentOutcome.Pending)
            return result.With(string.Empty, "The payment has not been confirmed yet. Please wait a moment and try again.");

        submission.PaymentStatus = PaymentStatus.Failed;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Submission.PaymentFailed", "Submission", submission.Id.ToString(), null, reference);
        return result.With(string.Empty, "The payment did not go through. Nothing was charged and your application was not submitted. You can try again.");
    }

    // ------------------------------------------------------------------ helpers

    private Task<Company?> CompanyOfAsync(string userId, CancellationToken ct) =>
        _db.SupplierProfiles.Where(p => p.UserId == userId).Select(p => p.Company).SingleOrDefaultAsync(ct);

    /// <summary>Marks the application Submitted: from this moment the organisation can see it.</summary>
    private static void Finalise(Submission submission, string userId, string note)
    {
        AddHistory(submission, submission.Status, SubmissionStatus.Submitted, userId, note);
        submission.Status = SubmissionStatus.Submitted;
        submission.SubmittedAtUtc = DateTime.UtcNow;
        submission.ReferenceNumber = $"EP-{DateTime.UtcNow:yyyy}-{submission.Id:D6}";
    }

    private async Task AuditSubmittedIfDoneAsync(Submission submission)
    {
        if (submission.Status == SubmissionStatus.Submitted)
            await _audit.LogAsync("Submission.Submitted", "Submission", submission.Id.ToString(), submission.Tender.OrganisationId, submission.ReferenceNumber);
    }

    private static void AddHistory(Submission submission, SubmissionStatus? from, SubmissionStatus to, string userId, string note) =>
        submission.StatusHistory.Add(new SubmissionStatusHistory
        {
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow,
            Note = note
        });

    /// <summary>The original file name is only ever DISPLAYED: strip any folder part and odd characters.</summary>
    private static string SafeDisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var clean = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : (clean.Length == 0 ? "document.pdf" : clean);
    }
}
