using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Areas.Admin;

/// <summary>
/// Applications received by the signed-in user's organisation (journey step 7).
///
/// What staff can see is decided by the tenant filters in EProcureDbContext, not by this controller:
/// only submissions to THIS organisation's tenders, and only once Submitted (fee paid or no fee).
/// An id belonging to another organisation, or an unpaid draft, is simply "not found" (404).
/// Opening an application and downloading a document are both written to the audit trail.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "OrgStaff")]
public class SubmissionsController : Controller
{
    private readonly EProcureDbContext _db;
    private readonly IAuditService _audit;
    private readonly IFileStorage _files;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubmissionsController(EProcureDbContext db, IAuditService audit, IFileStorage files, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _audit = audit;
        _files = files;
        _userManager = userManager;
    }

    // GET /Admin/Submissions   or   /Admin/Submissions?tenderId=5
    public async Task<IActionResult> Index(int? tenderId, CancellationToken ct)
    {
        var query = _db.Submissions.AsNoTracking();
        if (tenderId is not null) query = query.Where(s => s.TenderId == tenderId);

        var raw = await query
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Select(s => new
            {
                s.Id, s.ReferenceNumber, CompanyName = s.Company.Name, s.DeclaredBbbeeLevel,
                TenderReference = s.Tender.ReferenceNumber, TenderTitle = s.Tender.Title, s.Status, s.SubmittedAtUtc,
                s.IsCsdRegistered, s.IsTaxCompliant, s.HasDeclaredInterest, s.ConfirmsNotRestricted, s.ConfirmsIndependentBid,
                Documents = s.Documents.Count(),
                s.Tender.ClosingDateUtc
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var rows = raw.Select(s =>
        {
            var sealedBid = EvaluationService.IsSealed(s.ClosingDateUtc, now);
            return new ReceivedApplicationRow(s.Id, s.ReferenceNumber, sealedBid ? "Sealed bid" : s.CompanyName, s.DeclaredBbbeeLevel,
                s.TenderReference, s.TenderTitle, s.Status, s.SubmittedAtUtc,
                sealedBid ? 0 : SubmissionStatuses.RedFlags(new Submission
                {
                    IsCsdRegistered = s.IsCsdRegistered, IsTaxCompliant = s.IsTaxCompliant, HasDeclaredInterest = s.HasDeclaredInterest,
                    ConfirmsNotRestricted = s.ConfirmsNotRestricted, ConfirmsIndependentBid = s.ConfirmsIndependentBid
                }).Count,
                s.Documents, sealedBid ? s.ClosingDateUtc : null);
        }).ToList();

        string? tenderRef = tenderId is null ? null
            : await _db.Tenders.Where(t => t.Id == tenderId).Select(t => t.ReferenceNumber).SingleOrDefaultAsync(ct);
        if (tenderId is not null && tenderRef is null) return NotFound(); // not this organisation's tender

        return View(new ReceivedApplicationsViewModel { TenderId = tenderId, TenderReference = tenderRef, Rows = rows });
    }

    // GET /Admin/Submissions/Details/12
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await BuildAsync(id, ct);
        if (model is null) return NotFound();

        var organisationId = await _db.Submissions.Where(s => s.Id == id).Select(s => s.Tender.OrganisationId).SingleAsync(ct);
        await _audit.LogAsync("Submission.Viewed", "Submission", id.ToString(), organisationId, model.ReferenceNumber);
        return View(model);
    }

    // GET /Admin/Submissions/Document/7   Streams one PDF. Only reachable through the tenant filter.
    public async Task<IActionResult> Document(int id, CancellationToken ct)
    {
        var document = await _db.UploadedDocuments.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { d.StorageKey, d.OriginalFileName, d.SubmissionId, d.Submission.Tender.OrganisationId, d.Submission.ReferenceNumber, d.Submission.Tender.ClosingDateUtc })
            .SingleOrDefaultAsync(ct);
        if (document is null) return NotFound();

        // Sealed until the closing date: refused on the server, not just hidden on the page.
        if (EvaluationService.IsSealed(document.ClosingDateUtc, DateTime.UtcNow))
        {
            await _audit.LogAsync("Document.SealedRefused", "Submission", document.SubmissionId.ToString(), document.OrganisationId, document.ReferenceNumber);
            TempData["FlashError"] = $"Bids are sealed until the closing date ({DisplayFormat.DateTime(document.ClosingDateUtc)}).";
            return RedirectToAction(nameof(Details), new { id = document.SubmissionId });
        }

        var stream = await _files.OpenReadAsync(document.StorageKey, ct);
        if (stream is null) return NotFound();

        await _audit.LogAsync("Document.Downloaded", "Submission", document.SubmissionId.ToString(), document.OrganisationId,
            $"{document.ReferenceNumber}: {document.OriginalFileName}");
        return File(stream, "application/pdf", document.OriginalFileName);
    }

    // GET /Admin/Submissions/TrackRecordFile/7?submissionId=12   One document from the bidder's track record.
    // Reached through a bid the organisation can see (tenant filter), after closing, and only if the company held the
    // document at that tender's closing date, which is exactly the list the BEC page shows.
    public async Task<IActionResult> TrackRecordFile(int id, int submissionId, CancellationToken ct)
    {
        var bid = await _db.Submissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => new { s.CompanyId, s.ReferenceNumber, s.Tender.OrganisationId, s.Tender.ClosingDateUtc })
            .SingleOrDefaultAsync(ct);
        if (bid is null) return NotFound();
        if (EvaluationService.IsSealed(bid.ClosingDateUtc, DateTime.UtcNow))
        {
            await _audit.LogAsync("Document.SealedRefused", "Submission", submissionId.ToString(), bid.OrganisationId, bid.ReferenceNumber);
            TempData["FlashError"] = $"Bids are sealed until the closing date ({DisplayFormat.DateTime(bid.ClosingDateUtc)}).";
            return RedirectToAction(nameof(Details), new { id = submissionId });
        }

        var document = await _db.CompanyDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.CompanyId == bid.CompanyId, ct);
        if (document is null || !document.HeldAt(bid.ClosingDateUtc)) return NotFound();

        var stream = await _files.OpenReadAsync(document.StorageKey, ct);
        if (stream is null) return NotFound();
        await _audit.LogAsync("Document.Downloaded", "Submission", submissionId.ToString(), bid.OrganisationId,
            $"{bid.ReferenceNumber}: track record, {document.OriginalFileName}");
        return File(stream, "application/pdf", document.OriginalFileName);
    }

    // Status changes are no longer made by hand here: they follow the evaluation and the BAC's award decision
    // (EvaluationController), so a bid's status always matches the recorded evaluation (DECISIONS D43).

    private async Task<ReceivedApplicationViewModel?> BuildAsync(int id, CancellationToken ct)
    {
        var s = await _db.Submissions.AsNoTracking()
            .Include(x => x.Tender).ThenInclude(t => t.Requirements)
            .Include(x => x.Company)
            .Include(x => x.SubmittedByUser)
            .Include(x => x.Documents)
            .Include(x => x.Evaluation)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return null;

        var stage = EvaluationService.StageOf(s.Tender, DateTime.UtcNow);
        if (stage == EvaluationStage.NotClosed)
        {
            // Sealed bid: staff may know that a bid arrived and when, nothing more, until the closing date.
            return new ReceivedApplicationViewModel
            {
                Id = s.Id,
                ReferenceNumber = s.ReferenceNumber,
                TenderId = s.TenderId,
                TenderReference = s.Tender.ReferenceNumber,
                TenderTitle = s.Tender.Title,
                Status = s.Status,
                SubmittedAtUtc = s.SubmittedAtUtc,
                IsSealed = true,
                ClosingDateUtc = s.Tender.ClosingDateUtc,
                Stage = stage,
                CompanyName = "Sealed bid"
            };
        }

        var history = await _db.SubmissionStatusHistory.AsNoTracking()
            .Where(h => h.SubmissionId == id)
            .OrderByDescending(h => h.ChangedAtUtc).ThenByDescending(h => h.Id)
            .Select(h => new { h.Note, h.ToStatus, h.ChangedAtUtc, h.ChangedByUser.FullName })
            .ToListAsync(ct);
        var requirementNames = s.Tender.Requirements.ToDictionary(r => r.Id, r => r.Name);
        string YesNo(bool? v) => v is null ? "Not answered" : v.Value ? "Yes" : "No";

        return new ReceivedApplicationViewModel
        {
            Id = s.Id,
            ReferenceNumber = s.ReferenceNumber,
            TenderId = s.TenderId,
            TenderReference = s.Tender.ReferenceNumber,
            TenderTitle = s.Tender.Title,
            Status = s.Status,
            SubmittedAtUtc = s.SubmittedAtUtc,
            PaymentText = s.PaymentStatus == PaymentStatus.Paid ? $"{DisplayFormat.Fee(s.AmountPaid)} verified · {s.PaymentReference}" : "No tender fee",
            CompanyName = s.Company.Name,
            CompanyFields = new List<(string, string)>
            {
                ("CIPC registration", s.Company.RegistrationNumber),
                ("CSD number", s.Company.CsdNumber),
                ("Tax number / TCS PIN", s.Company.TaxPin),
                ("Enterprise size", s.Company.EnterpriseSize.ToString()),
                ("Sector", s.Company.Sector)
            },
            ContactName = s.SubmittedByUser.FullName,
            ContactEmail = s.SubmittedByUser.Email ?? string.Empty,
            Answers = new List<(string, string, bool)>
            {
                ("Registered on the CSD", YesNo(s.IsCsdRegistered), s.IsCsdRegistered == false),
                ("Tax compliant", YesNo(s.IsTaxCompliant), s.IsTaxCompliant == false),
                ("B-BBEE level declared", EligibilityRules.Describe(s.DeclaredBbbeeLevel), false),
                ("SBD 4: interest declared", YesNo(s.HasDeclaredInterest) + (s.InterestDetails is null ? "" : $": {s.InterestDetails}"), s.HasDeclaredInterest == true),
                ("SBD 8: on a restricted list", s.ConfirmsNotRestricted is null ? "Not answered" : s.ConfirmsNotRestricted.Value ? "No" : $"Yes: {s.RestrictionDetails}", s.ConfirmsNotRestricted == false),
                ("SBD 9: independent bid", s.ConfirmsIndependentBid is null ? "Not answered" : s.ConfirmsIndependentBid.Value ? "Confirmed" : "Cannot confirm", s.ConfirmsIndependentBid == false),
                ("Declaration signed", s.DeclaredAtUtc is DateTime at ? DisplayFormat.DateTime(at) : "No", s.DeclaredAtUtc is null)
            },
            RedFlags = SubmissionStatuses.RedFlags(s),
            Documents = s.Documents.OrderBy(d => d.UploadedAtUtc).Select(d => new ReceivedApplicationViewModel.DocumentRow(
                d.Id, d.TenderRequirementId is int r && requirementNames.TryGetValue(r, out var n) ? n : "Supporting document",
                d.OriginalFileName, d.SizeBytes, d.Sha256[..12], d.UploadedAtUtc)).ToList(),
            Timeline = history.Select(h => (h.Note ?? SubmissionStatuses.Label(h.ToStatus), h.FullName, h.ChangedAtUtc)).ToList(),
            ClosingDateUtc = s.Tender.ClosingDateUtc,
            Stage = stage,
            EvaluationText = s.Evaluation is null ? "Not evaluated yet"
                : s.Evaluation.IsResponsive ? $"Responsive · {DisplayFormat.Money(s.Evaluation.BidPrice!.Value)}"
                : $"Not responsive: {s.Evaluation.NonResponsiveReason}"
        };
    }
}
