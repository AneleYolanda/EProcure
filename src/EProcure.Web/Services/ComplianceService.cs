using System.Security.Cryptography;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Services;

/// <summary>What the supplier states about a compliance document (the PDF comes separately).</summary>
public record ComplianceInput(ComplianceDocumentType? Type, DateTime? IssuedOn, DateTime? ExpiresOn);

public interface IComplianceService
{
    /// <summary>The current document of each type on the signed-in supplier's company (none if there is no company).</summary>
    Task<IReadOnlyList<ComplianceDocument>> ListCurrentAsync(string userId, CancellationToken ct);

    /// <summary>Adds a document; a current one of the same type is archived (replaced).</summary>
    Task<ServiceResult> AddAsync(string userId, ComplianceInput input, IFormFile? file, CancellationToken ct);

    /// <summary>Takes a document off the profile (archived, not deleted).</summary>
    Task<ServiceResult> RemoveAsync(string userId, int documentId, CancellationToken ct);

    /// <summary>One of the supplier's own documents, current or archived, or NULL.</summary>
    Task<ComplianceDocument?> FindOwnAsync(string userId, int documentId, CancellationToken ct);
}

/// <summary>
/// The supplier's compliance documents: uploaded once (at registration or later), reminded before they expire, and attached
/// to bids from the profile (ApplicationService.UseProfileDocumentAsync). Files are checked like every other PDF.
/// Ownership: always the signed-in supplier's own company, found through their SupplierProfile, never an id from the browser.
/// </summary>
public class ComplianceService : IComplianceService
{
    private readonly EProcureDbContext _db;
    private readonly IAuditService _audit;
    private readonly IFileStorage _files;
    private readonly UploadOptions _uploads;

    public ComplianceService(EProcureDbContext db, IAuditService audit, IFileStorage files, IOptions<UploadOptions> uploads)
    {
        _db = db;
        _audit = audit;
        _files = files;
        _uploads = uploads.Value;
    }

    public async Task<IReadOnlyList<ComplianceDocument>> ListCurrentAsync(string userId, CancellationToken ct)
    {
        var companyId = await CompanyIdAsync(userId, ct);
        if (companyId is null) return Array.Empty<ComplianceDocument>();
        return await _db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.ArchivedAtUtc == null)
            .OrderBy(d => d.Type)
            .ToListAsync(ct);
    }

    public async Task<ServiceResult> AddAsync(string userId, ComplianceInput input, IFormFile? file, CancellationToken ct)
    {
        var result = new ServiceResult();
        var companyId = await CompanyIdAsync(userId, ct);
        if (companyId is null) return result.With(string.Empty, "Add your company first. Compliance documents belong to the company.");

        var today = SaTime.ToSast(DateTime.UtcNow).Date;
        if (input.Type is not ComplianceDocumentType type || !Enum.IsDefined(type))
            return result.With(nameof(input.Type), "Choose which document this is.");

        var info = ComplianceRules.Info(type);
        var issued = input.IssuedOn?.Date;
        if (issued is null)
            result.With(nameof(input.IssuedOn), $"Enter the {info.DateLabel.ToLowerInvariant()} shown on the document.");
        else if (issued > today)
            result.With(nameof(input.IssuedOn), "The date on the document cannot be in the future.");
        else if (issued < new DateTime(1990, 1, 1))
            result.With(nameof(input.IssuedOn), "Check the date on the document.");

        if (info.ExpiryOnDocument)
        {
            if (input.ExpiresOn is null)
                result.With(nameof(input.ExpiresOn), "Enter the expiry date printed on the document.");
            else if (issued is DateTime from && (input.ExpiresOn.Value.Date < from || input.ExpiresOn.Value.Date > from.AddYears(5)))
                result.With(nameof(input.ExpiresOn), "The expiry date must be after the date issued, and within 5 years of it.");
        }
        if (file is null || file.Length == 0) result.With("File", "Choose the PDF to upload.");
        if (!result.Succeeded) return result;

        var expires = ComplianceRules.ExpiresOn(type, issued!.Value, input.ExpiresOn);
        if (expires is DateTime last && last < today)
            return result.With(nameof(input.IssuedOn), $"This document expired on {ComplianceRules.Day(last)}. Validity: {info.Validity} Upload a current one.");

        // Read into memory (max 5 MB) so we can check the real content, hash it, then store it.
        await using var buffer = new MemoryStream();
        await using (var stream = file!.OpenReadStream())
        {
            await stream.CopyToAsync(buffer, ct);
        }
        var bytes = buffer.ToArray();
        if (PdfValidator.Check(file.FileName, file.ContentType, bytes.AsSpan(0, Math.Min(bytes.Length, 8)), bytes.LongLength, _uploads.MaxFileSizeBytes) is string invalid)
            return result.With("File", invalid);

        var now = DateTime.UtcNow;
        foreach (var previous in await _db.ComplianceDocuments.Where(d => d.CompanyId == companyId && d.Type == type && d.ArchivedAtUtc == null).ToListAsync(ct))
            previous.ArchivedAtUtc = now;

        buffer.Position = 0;
        var document = new ComplianceDocument
        {
            CompanyId = companyId.Value,
            Type = type,
            IssuedOn = issued.Value,
            ExpiresOn = expires,
            OriginalFileName = SafeDisplayName(file.FileName),
            StorageKey = await _files.SaveAsync(buffer, ".pdf", ct),
            ContentType = "application/pdf",
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedByUserId = userId,
            UploadedAtUtc = now
        };
        _db.ComplianceDocuments.Add(document);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Compliance.Added", "Company", companyId.Value.ToString(), null,
            $"{info.Label}, {ComplianceRules.Describe(expires, today)}");
        return ServiceResult.Ok(document.Id);
    }

    public async Task<ServiceResult> RemoveAsync(string userId, int documentId, CancellationToken ct)
    {
        var companyId = await CompanyIdAsync(userId, ct);
        var document = companyId is null ? null
            : await _db.ComplianceDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.CompanyId == companyId && d.ArchivedAtUtc == null, ct);
        if (document is null) return ServiceResult.Missing();

        document.ArchivedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Compliance.Removed", "Company", document.CompanyId.ToString(), null, ComplianceRules.Info(document.Type).Label);
        return ServiceResult.Ok(document.Id);
    }

    public async Task<ComplianceDocument?> FindOwnAsync(string userId, int documentId, CancellationToken ct)
    {
        var companyId = await CompanyIdAsync(userId, ct);
        return companyId is null ? null
            : await _db.ComplianceDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.CompanyId == companyId, ct);
    }

    private Task<int?> CompanyIdAsync(string userId, CancellationToken ct) =>
        _db.SupplierProfiles.Where(p => p.UserId == userId).Select(p => p.CompanyId).SingleOrDefaultAsync(ct);

    private static string SafeDisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var clean = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : (clean.Length == 0 ? "document.pdf" : clean);
    }
}
