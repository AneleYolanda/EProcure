using System.Security.Cryptography;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Services;

/// <summary>What the supplier types about one piece of past work (the PDF comes separately).</summary>
public record TrackRecordInput(CompanyDocumentKind? Kind, string? Title, string? ClientName, int? YearCompleted, decimal? ContractValue);

public interface ITrackRecordService
{
    /// <summary>The documents currently on the signed-in supplier's company profile (newest first). Empty if no company.</summary>
    Task<IReadOnlyList<CompanyDocument>> ListOwnAsync(string userId, CancellationToken ct);

    Task<ServiceResult> AddAsync(string userId, TrackRecordInput input, IFormFile? file, CancellationToken ct);

    /// <summary>Takes a document off the profile. The file is kept: bids that closed earlier may rely on it.</summary>
    Task<ServiceResult> RemoveAsync(string userId, int documentId, CancellationToken ct);

    /// <summary>One of the supplier's OWN documents (including removed ones), or NULL.</summary>
    Task<CompanyDocument?> FindOwnAsync(string userId, int documentId, CancellationToken ct);

    /// <summary>The documents a company held at a tender's closing date: what the committee evaluates.</summary>
    Task<IReadOnlyList<CompanyDocument>> HeldAtAsync(int companyId, DateTime closingUtc, CancellationToken ct);
}

/// <summary>
/// The company's track record (past work, reference letters, company profile): uploaded once, at registration or later,
/// and part of every bid. Files are checked exactly like bid documents (PdfValidator, 5 MB, SHA-256).
///
/// Ownership: the company is always the signed-in supplier's own (found through their SupplierProfile), never an id
/// from the browser. The committee sees what the company held AT THE CLOSING DATE, so a document added or removed
/// after a tender closed does not change that bid (CompanyDocument.HeldAt).
/// </summary>
public class TrackRecordService : ITrackRecordService
{
    /// <summary>A profile holds at most this many documents at a time (it is a summary, not an archive).</summary>
    public const int MaxDocuments = 20;

    private readonly EProcureDbContext _db;
    private readonly IAuditService _audit;
    private readonly IFileStorage _files;
    private readonly UploadOptions _uploads;

    public TrackRecordService(EProcureDbContext db, IAuditService audit, IFileStorage files, IOptions<UploadOptions> uploads)
    {
        _db = db;
        _audit = audit;
        _files = files;
        _uploads = uploads.Value;
    }

    public async Task<IReadOnlyList<CompanyDocument>> ListOwnAsync(string userId, CancellationToken ct)
    {
        var companyId = await CompanyIdAsync(userId, ct);
        if (companyId is null) return Array.Empty<CompanyDocument>();
        return await _db.CompanyDocuments.AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.RemovedAtUtc == null)
            .OrderByDescending(d => d.YearCompleted).ThenByDescending(d => d.UploadedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<ServiceResult> AddAsync(string userId, TrackRecordInput input, IFormFile? file, CancellationToken ct)
    {
        var result = new ServiceResult();
        var companyId = await CompanyIdAsync(userId, ct);
        if (companyId is null) return result.With(string.Empty, "Add your company first. The track record belongs to the company.");

        var title = input.Title?.Trim() ?? string.Empty;
        var client = string.IsNullOrWhiteSpace(input.ClientName) ? null : input.ClientName.Trim();
        var thisYear = SaTime.ToSast(DateTime.UtcNow).Year;

        if (input.Kind is null || !Enum.IsDefined(input.Kind.Value))
            result.With(nameof(input.Kind), "Choose what the document is.");
        if (title.Length < 3 || title.Length > 200)
            result.With(nameof(input.Title), "Describe the work in 3 to 200 characters, e.g. \"Resurfacing of 12 km of municipal roads\".");
        if (client is null && input.Kind is CompanyDocumentKind.ReferenceLetter or CompanyDocumentKind.CompletionCertificate)
            result.With(nameof(input.ClientName), "Enter the client the work was done for.");
        if (client?.Length > 200)
            result.With(nameof(input.ClientName), "The client name must be 200 characters or fewer.");
        if (input.YearCompleted is int year && (year < 1950 || year > thisYear))
            result.With(nameof(input.YearCompleted), $"Enter a year from 1950 to {thisYear}.");
        if (input.ContractValue is decimal value && (value < 0 || value >= 100_000_000_000m))
            result.With(nameof(input.ContractValue), "Enter a valid contract value in Rand.");
        if (file is null || file.Length == 0)
            result.With("File", "Choose the PDF to upload.");
        if (await _db.CompanyDocuments.CountAsync(d => d.CompanyId == companyId && d.RemovedAtUtc == null, ct) >= MaxDocuments)
            result.With(string.Empty, $"Your track record can hold {MaxDocuments} documents. Remove an older one first.");
        if (!result.Succeeded) return result;

        // Read into memory (max 5 MB) so we can check the real content, hash it, then store it.
        await using var buffer = new MemoryStream();
        await using (var stream = file!.OpenReadStream())
        {
            await stream.CopyToAsync(buffer, ct);
        }
        var bytes = buffer.ToArray();
        if (PdfValidator.Check(file.FileName, file.ContentType, bytes.AsSpan(0, Math.Min(bytes.Length, 8)), bytes.LongLength, _uploads.MaxFileSizeBytes) is string invalid)
            return result.With("File", invalid);

        buffer.Position = 0;
        var document = new CompanyDocument
        {
            CompanyId = companyId.Value,
            Kind = input.Kind!.Value,
            Title = title,
            ClientName = client,
            YearCompleted = input.YearCompleted,
            ContractValue = input.ContractValue is decimal v ? Math.Round(v, 2) : null,
            OriginalFileName = SafeDisplayName(file.FileName),
            StorageKey = await _files.SaveAsync(buffer, ".pdf", ct),
            ContentType = "application/pdf",
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            UploadedByUserId = userId,
            UploadedAtUtc = DateTime.UtcNow
        };
        _db.CompanyDocuments.Add(document);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TrackRecord.Added", "Company", companyId.Value.ToString(), null, $"{Label(document.Kind)}: {document.Title}");
        return ServiceResult.Ok(document.Id);
    }

    public async Task<ServiceResult> RemoveAsync(string userId, int documentId, CancellationToken ct)
    {
        var companyId = await CompanyIdAsync(userId, ct);
        var document = companyId is null ? null
            : await _db.CompanyDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.CompanyId == companyId && d.RemovedAtUtc == null, ct);
        if (document is null) return ServiceResult.Missing();

        document.RemovedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TrackRecord.Removed", "Company", document.CompanyId.ToString(), null, document.Title);
        return ServiceResult.Ok(document.Id);
    }

    public async Task<CompanyDocument?> FindOwnAsync(string userId, int documentId, CancellationToken ct)
    {
        var companyId = await CompanyIdAsync(userId, ct);
        return companyId is null ? null
            : await _db.CompanyDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.CompanyId == companyId, ct);
    }

    public async Task<IReadOnlyList<CompanyDocument>> HeldAtAsync(int companyId, DateTime closingUtc, CancellationToken ct) =>
        await _db.CompanyDocuments.AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.UploadedAtUtc <= closingUtc && (d.RemovedAtUtc == null || d.RemovedAtUtc > closingUtc))
            .OrderByDescending(d => d.YearCompleted).ThenByDescending(d => d.UploadedAtUtc)
            .ToListAsync(ct);

    public static string Label(CompanyDocumentKind kind) => kind switch
    {
        CompanyDocumentKind.ReferenceLetter => "Reference letter",
        CompanyDocumentKind.CompletionCertificate => "Completion certificate",
        CompanyDocumentKind.CompanyProfile => "Company profile",
        _ => "Other proof of experience"
    };

    /// <summary>"Client · 2024 · R1 200 000.00" (the parts that were given).</summary>
    public static string Describe(CompanyDocument d) => string.Join(" · ", new[]
    {
        d.ClientName, d.YearCompleted?.ToString(), d.ContractValue is decimal v ? DisplayFormat.Money(v) : null
    }.Where(p => !string.IsNullOrEmpty(p)));

    private Task<int?> CompanyIdAsync(string userId, CancellationToken ct) =>
        _db.SupplierProfiles.Where(p => p.UserId == userId).Select(p => p.CompanyId).SingleOrDefaultAsync(ct);

    private static string SafeDisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var clean = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : (clean.Length == 0 ? "document.pdf" : clean);
    }
}
