using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Tests.Support;

/// <summary>Who the database thinks is asking: an organisation's staff member, or the marketplace (suppliers).</summary>
public sealed record TestTenant(bool IsOrganisationScoped, int? OrganisationId) : ITenantContext
{
    public static readonly TestTenant Marketplace = new(false, null);
    public static TestTenant Org(int? organisationId) => new(true, organisationId);
}

/// <summary>
/// A real EF Core database for each test: SQLite in memory, built from the same model as production
/// (same query filters, unique indexes and seeded organisations). It lives as long as the connection is
/// open, so several DbContexts with DIFFERENT tenants can look at the same data, like different users.
/// </summary>
public sealed class TestDb : IDisposable
{
    public const int Rbidz = SeedData.RbidzId;
    public const int Mvlm = SeedData.MzansiValleyId;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EProcureDbContext> _options;
    private int _counter;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EProcureDbContext>().UseSqlite(_connection).Options;
        using var db = Context(TestTenant.Marketplace);
        db.Database.EnsureCreated();
    }

    public EProcureDbContext Context(ITenantContext tenant) => new(_options, tenant);

    /// <summary>A context with no organisation filter, for arranging test data.</summary>
    public EProcureDbContext Marketplace() => Context(TestTenant.Marketplace);

    public void Dispose() => _connection.Dispose();

    // ------------------------------------------------------------------ arranging data

    public string AddUser(int? organisationId = null)
    {
        var id = $"user-{++_counter}";
        using var db = Marketplace();
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@EXAMPLE.TEST",
            NormalizedEmail = $"{id}@EXAMPLE.TEST",
            Email = $"{id}@example.test",
            FullName = $"Test {id}",
            OrganisationId = organisationId,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    /// <summary>A supplier user with a company at the given B-BBEE level. Returns (userId, companyId).</summary>
    public (string UserId, int CompanyId) AddSupplier(BbbeeLevel level)
    {
        var userId = AddUser();
        var n = ++_counter;
        using var db = Marketplace();
        var company = new Company
        {
            Name = $"Company {n} (Pty) Ltd",
            RegistrationNumber = $"2020/{n:D6}/07",
            TaxPin = $"{n:D10}",
            CsdNumber = $"MAAA{n:D7}",
            BbbeeLevel = level,
            Sector = "Security",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.SupplierProfiles.Add(new SupplierProfile { UserId = userId, Company = company, CreatedAtUtc = DateTime.UtcNow });
        db.SaveChanges();
        return (userId, company.Id);
    }

    public int AddTender(int organisationId, DateTime closingUtc, BbbeeLevel? minimum = null, decimal fee = 0m,
        TenderStatus status = TenderStatus.Published, int requirements = 2)
    {
        var n = ++_counter;
        var creator = AddUser(organisationId);
        using var db = Marketplace();
        var tender = new Tender
        {
            OrganisationId = organisationId,
            Title = $"Tender {n}",
            ReferenceNumber = $"TEST/{n:D4}",
            Category = "Security services",
            Description = "Test tender",
            ClosingDateUtc = closingUtc,
            TenderFee = fee,
            MinimumBbbeeLevel = minimum,
            Status = status,
            CreatedByUserId = creator,
            CreatedAtUtc = DateTime.UtcNow,
            PublishedAtUtc = status == TenderStatus.Published ? DateTime.UtcNow : null
        };
        for (var i = 1; i <= requirements; i++)
            tender.Requirements.Add(new TenderRequirement { Name = $"Document {i}", IsMandatory = true, SortOrder = i });
        db.Tenders.Add(tender);
        db.SaveChanges();
        return tender.Id;
    }

    /// <summary>An application in a given state, with one document and one history row (for isolation tests).</summary>
    public int AddSubmission(int tenderId, int companyId, string userId, SubmissionStatus status, BbbeeLevel level = BbbeeLevel.Level1)
    {
        using var db = Marketplace();
        var submission = new Submission
        {
            TenderId = tenderId,
            CompanyId = companyId,
            SubmittedByUserId = userId,
            Status = status,
            DeclaredBbbeeLevel = level,
            CreatedAtUtc = DateTime.UtcNow
        };
        submission.Documents.Add(new UploadedDocument
        {
            OriginalFileName = "doc.pdf",
            StorageKey = Guid.NewGuid().ToString("N"),
            ContentType = "application/pdf",
            SizeBytes = 10,
            Sha256 = new string('a', 64),
            UploadedByUserId = userId,
            UploadedAtUtc = DateTime.UtcNow
        });
        submission.StatusHistory.Add(new SubmissionStatusHistory
        {
            ToStatus = status,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow
        });
        db.Submissions.Add(submission);
        db.SaveChanges();
        return submission.Id;
    }

    public static DateTime InDays(double days) => DateTime.UtcNow.AddDays(days);
}
