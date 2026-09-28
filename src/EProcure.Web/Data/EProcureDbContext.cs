using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Data;

/// <summary>
/// The single database for ALL organisations (shared-schema multi-tenancy).
/// Inherits IdentityDbContext so the Identity tables (Users, Roles, ...) live in the same database.
/// </summary>
public class EProcureDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    private readonly ITenantContext _tenant;

    public EProcureDbContext(DbContextOptions<EProcureDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<SupplierProfile> SupplierProfiles => Set<SupplierProfile>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderRequirement> TenderRequirements => Set<TenderRequirement>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<UploadedDocument> UploadedDocuments => Set<UploadedDocument>();
    public DbSet<SubmissionStatusHistory> SubmissionStatusHistory => Set<SubmissionStatusHistory>();
    public DbSet<AwardRecord> AwardRecords => Set<AwardRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    // These two properties are read by the query filters below. Because they are members of
    // the DbContext, EF Core re-reads them on EVERY query (they become SQL parameters), so each
    // request gets its own user's tenant, not the value from when the model was first built.
    private bool IsOrgScoped => _tenant.IsOrganisationScoped;
    private int? CurrentOrgId => _tenant.OrganisationId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store every enum as readable text ("Published") instead of a number.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        // Money: 18 digits, 2 decimals (Rand and cents).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity's own table definitions first

        // Apply every IEntityTypeConfiguration<T> class in this project (Data/Configurations).
        builder.ApplyConfigurationsFromAssembly(typeof(EProcureDbContext).Assembly);

        // ------------------------------------------------------------------
        // TENANT ISOLATION: global query filters.
        // For an OrgAdmin/Evaluator every query on these tables silently gets
        //   WHERE OrganisationId = <their org>
        // added by EF Core. A developer cannot forget it in a controller.
        // Suppliers / anonymous users are not org-scoped and see the marketplace;
        // their own restrictions (e.g. only their own submissions) are applied in the
        // supplier queries, added in steps 5-6.
        // ------------------------------------------------------------------
        builder.Entity<Tender>().HasQueryFilter(t =>
            !IsOrgScoped || t.OrganisationId == CurrentOrgId);

        builder.Entity<TenderRequirement>().HasQueryFilter(r =>
            !IsOrgScoped || r.Tender.OrganisationId == CurrentOrgId);

        builder.Entity<AwardRecord>().HasQueryFilter(a =>
            !IsOrgScoped || a.Tender.OrganisationId == CurrentOrgId);

        // Submissions: own organisation only, AND only once paid (Draft / AwaitingPayment are
        // invisible to the organisation). Enums are stored as text, so we list the hidden states
        // rather than using "<" (text comparison would be alphabetical, not by stage).
        builder.Entity<Submission>().HasQueryFilter(s =>
            !IsOrgScoped ||
            (s.Tender.OrganisationId == CurrentOrgId
             && s.Status != SubmissionStatus.Draft
             && s.Status != SubmissionStatus.AwaitingPayment));

        builder.Entity<UploadedDocument>().HasQueryFilter(d =>
            !IsOrgScoped ||
            (d.Submission.Tender.OrganisationId == CurrentOrgId
             && d.Submission.Status != SubmissionStatus.Draft
             && d.Submission.Status != SubmissionStatus.AwaitingPayment));

        builder.Entity<SubmissionStatusHistory>().HasQueryFilter(h =>
            !IsOrgScoped ||
            (h.Submission.Tender.OrganisationId == CurrentOrgId
             && h.Submission.Status != SubmissionStatus.Draft
             && h.Submission.Status != SubmissionStatus.AwaitingPayment));

        // AuditEntry.OrganisationId is nullable (platform events such as supplier sign-ins have none).
        // EF compares nullables with C# rules, where NULL == NULL is true, so without the explicit
        // "!= null" a staff user with no organisation would see every platform event (found by a Step 8 test).
        builder.Entity<AuditEntry>().HasQueryFilter(a =>
            !IsOrgScoped || (a.OrganisationId != null && a.OrganisationId == CurrentOrgId));

        SeedData.Apply(builder);
    }
}
