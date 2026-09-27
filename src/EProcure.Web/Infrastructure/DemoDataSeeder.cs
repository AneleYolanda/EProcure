using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Infrastructure;

/// <summary>
/// Creates safe, repeatable demonstration data only in Development.
/// The password is intentionally read from user-secrets and never stored in source control.
/// </summary>
public class DemoDataSeeder
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(IServiceProvider services, ILogger<DemoDataSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var configuration = _services.GetRequiredService<IConfiguration>();
        var password = configuration["DemoSeed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Demo users were not seeded because DemoSeed:Password is missing. Run dotnet user-secrets init --project src/EProcure.Web and set DemoSeed:Password.");
            return;
        }

        var db = _services.GetRequiredService<EProcureDbContext>();
        var userManager = _services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = _services.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var role in new[] { AppRoles.Supplier, AppRoles.OrgAdmin, AppRoles.Evaluator })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new ApplicationRole(role));
                if (!roleResult.Succeeded)
                {
                    _logger.LogError("Could not create demo role {Role}: {Errors}", role, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                    return;
                }
            }
        }

        var rbidz = await db.Organisations.SingleAsync(o => o.Id == SeedData.RbidzId, cancellationToken);
        var mvlm = await db.Organisations.SingleAsync(o => o.Id == SeedData.MzansiValleyId, cancellationToken);

        var rbidzAdmin = await EnsureUserAsync(userManager, "admin@rbidz.demo", "RBIDZ Administrator", AppRoles.OrgAdmin, rbidz.Id, password);
        await EnsureUserAsync(userManager, "evaluator@rbidz.demo", "RBIDZ Evaluator", AppRoles.Evaluator, rbidz.Id, password);
        var mvlmAdmin = await EnsureUserAsync(userManager, "admin@mvlm.demo", "MVLM Administrator", AppRoles.OrgAdmin, mvlm.Id, password);

        var supplier1 = await EnsureUserAsync(userManager, "supplier1@demo.co.za", "Supplier One", AppRoles.Supplier, null, password);
        var supplier2 = await EnsureUserAsync(userManager, "supplier2@demo.co.za", "Supplier Two", AppRoles.Supplier, null, password);

        await EnsureSupplierProfileAsync(db, supplier1, "Umhlathi Civils (Pty) Ltd", "2018/123456/07", "MAAA1234567", BbbeeLevel.Level1, "Construction", cancellationToken);
        await EnsureSupplierProfileAsync(db, supplier2, "Khanya Office Supplies CC", "2015/654321/23", "MAAA7654321", BbbeeLevel.Level6, "Goods & Supplies", cancellationToken);

        await EnsureTendersAsync(db, rbidz, rbidzAdmin, new[]
        {
            TenderSeed.Published("Provision of security services for IDZ Phase 1A", "RBIDZ/2026/014", "Security Services", 500m, BbbeeLevel.Level4, 7, new[] { "CIPC registration certificate", "CSD summary report", "SARS tax compliance status PIN", "B-BBEE certificate or sworn affidavit", "Pricing schedule" }),
            TenderSeed.Published("Supply and delivery of office furniture", "RBIDZ/2026/015", "Goods & Supplies", 0m, null, 14, new[] { "CIPC registration certificate", "CSD summary report", "SBD 4 Declaration of Interest", "SBD 9 Independent Bid Determination" }),
            TenderSeed.Published("Maintenance of electrical substations", "RBIDZ/2026/016", "Maintenance", 350m, null, 21, new[] { "CIPC registration certificate", "SARS tax compliance status PIN", "B-BBEE certificate or sworn affidavit", "SBD 6.1", "Pricing schedule" }),
            TenderSeed.Draft("Provision of landscaping services", "RBIDZ/2026/017", "Cleaning & Hygiene", 0m, null, 30, new[] { "CIPC registration certificate", "CSD summary report", "Pricing schedule" }),
            TenderSeed.Published("Supply of industrial safety equipment", "RBIDZ/2026/018", "Goods & Supplies", 200m, null, -2, new[] { "CIPC registration certificate", "CSD summary report", "SBD 4 Declaration of Interest" })
        }, cancellationToken);

        await EnsureTendersAsync(db, mvlm, mvlmAdmin, new[]
        {
            TenderSeed.Published("Road maintenance: Ward 7 gravel roads", "MVLM/2026/007", "Construction", 250m, BbbeeLevel.Level4, 10, new[] { "CIPC registration certificate", "CSD summary report", "SARS tax compliance status PIN", "B-BBEE certificate or sworn affidavit", "SBD 9 Independent Bid Determination" }),
            TenderSeed.Published("Cleaning and hygiene services for municipal offices", "MVLM/2026/008", "Cleaning & Hygiene", 0m, null, 28, new[] { "CIPC registration certificate", "CSD summary report", "SBD 4 Declaration of Interest", "SBD 6.1" })
        }, cancellationToken);

        _logger.LogInformation("Development demo data is ready.");
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string role,
        int? organisationId,
        string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                OrganisationId = organisationId,
                CreatedAtUtc = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create demo user {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var roleResult = await userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException($"Could not add {email} to {role}: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
            }
        }

        return user;
    }

    private static async Task EnsureSupplierProfileAsync(
        EProcureDbContext db,
        ApplicationUser user,
        string companyName,
        string registrationNumber,
        string csdNumber,
        BbbeeLevel bbbeeLevel,
        string sector,
        CancellationToken cancellationToken)
    {
        var profile = await db.SupplierProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        if (profile is not null) return;

        var company = await db.Companies.SingleOrDefaultAsync(c => c.RegistrationNumber == registrationNumber, cancellationToken);
        if (company is null)
        {
            company = new Company
            {
                Name = companyName,
                RegistrationNumber = registrationNumber,
                TaxPin = $"DEMO-{registrationNumber.Replace("/", string.Empty)}",
                CsdNumber = csdNumber,
                BbbeeLevel = bbbeeLevel,
                Sector = sector,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync(cancellationToken);
        }

        db.SupplierProfiles.Add(new SupplierProfile
        {
            UserId = user.Id,
            CompanyId = company.Id,
            PopiaConsentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureTendersAsync(
        EProcureDbContext db,
        Organisation organisation,
        ApplicationUser createdBy,
        IEnumerable<TenderSeed> seeds,
        CancellationToken cancellationToken)
    {
        if (await db.Tenders.AnyAsync(t => t.OrganisationId == organisation.Id, cancellationToken)) return;

        foreach (var seed in seeds)
        {
            var tender = new Tender
            {
                OrganisationId = organisation.Id,
                Title = seed.Title,
                ReferenceNumber = seed.ReferenceNumber,
                Category = seed.Category,
                Description = $"Demo tender for {seed.Title}. Suppliers should review the complete tender requirements before applying.",
                ClosingDateUtc = DateTime.UtcNow.AddDays(seed.DaysFromNow),
                TenderFee = seed.Fee,
                MinimumBbbeeLevel = seed.MinimumBbbeeLevel,
                Status = seed.Status,
                CreatedByUserId = createdBy.Id,
                CreatedAtUtc = DateTime.UtcNow,
                PublishedAtUtc = seed.Status == TenderStatus.Published ? DateTime.UtcNow : null,
                Requirements = seed.Requirements.Select((name, index) => new TenderRequirement
                {
                    Name = name,
                    IsMandatory = true,
                    SortOrder = index + 1
                }).ToList()
            };
            db.Tenders.Add(tender);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record TenderSeed(
        string Title,
        string ReferenceNumber,
        string Category,
        decimal Fee,
        BbbeeLevel? MinimumBbbeeLevel,
        int DaysFromNow,
        TenderStatus Status,
        string[] Requirements)
    {
        public static TenderSeed Published(string title, string reference, string category, decimal fee, BbbeeLevel? minimum, int days, string[] requirements)
            => new(title, reference, category, fee, minimum, days, TenderStatus.Published, requirements);

        public static TenderSeed Draft(string title, string reference, string category, decimal fee, BbbeeLevel? minimum, int days, string[] requirements)
            => new(title, reference, category, fee, minimum, days, TenderStatus.Draft, requirements);
    }
}
