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

        // Fictitious demo numbers. They are marked as already verified so demo users skip the OTP step.
        var rbidzAdmin = await EnsureUserAsync(userManager, "admin@rbidz.demo", "RBIDZ Administrator", AppRoles.OrgAdmin, rbidz.Id, password, "+27820000001");
        await EnsureUserAsync(userManager, "evaluator@rbidz.demo", "RBIDZ Evaluator", AppRoles.Evaluator, rbidz.Id, password, "+27820000002");
        // A second SCM Officer, so RBIDZ's "second approval before publishing" rule can be demonstrated.
        await EnsureUserAsync(userManager, "approver@rbidz.demo", "RBIDZ Senior SCM Officer", AppRoles.OrgAdmin, rbidz.Id, password, "+27820000007");
        var mvlmAdmin = await EnsureUserAsync(userManager, "admin@mvlm.demo", "MVLM Administrator", AppRoles.OrgAdmin, mvlm.Id, password, "+27820000003");

        var supplier1 = await EnsureUserAsync(userManager, "supplier1@demo.co.za", "Supplier One", AppRoles.Supplier, null, password, "+27820000004");
        var supplier2 = await EnsureUserAsync(userManager, "supplier2@demo.co.za", "Supplier Two", AppRoles.Supplier, null, password, "+27820000005");

        await EnsureSupplierProfileAsync(db, supplier1, "Umhlathi Civils (Pty) Ltd", "2018/123456/07", "MAAA1234567", BbbeeLevel.Level1, EnterpriseSize.QSE, "Construction", cancellationToken);
        await EnsureSupplierProfileAsync(db, supplier2, "Khanya Office Supplies CC", "2015/654321/23", "MAAA7654321", BbbeeLevel.Level6, EnterpriseSize.EME, "Goods & Supplies", cancellationToken);

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

        await EnsureEstimatedValuesAsync(db, cancellationToken);

        var supplier3 = await EnsureUserAsync(userManager, "supplier3@demo.co.za", "Supplier Three", AppRoles.Supplier, null, password, "+27820000006");
        await EnsureSupplierProfileAsync(db, supplier3, "Siyakha Business Solutions (Pty) Ltd", "2019/246810/07", "MAAA2468101", BbbeeLevel.Level2, EnterpriseSize.QSE, "Professional Services", cancellationToken);
        await EnsureEvaluationDemoAsync(db, rbidz, rbidzAdmin, new[] { supplier1, supplier2, supplier3 }, cancellationToken);

        _logger.LogInformation("Development demo data is ready.");
    }

    /// <summary>
    /// A tender that has already CLOSED with three submitted bids, so bid opening, BEC evaluation and the BAC
    /// award can be demonstrated at once. Each bid's pricing schedule PDF states its price, which the BEC member
    /// reads and captures (80/20): Umhlathi R1 380 000 (Level 1), Khanya R1 150 000 (Level 6, lowest price),
    /// Siyakha R1 240 000 (Level 2). Siyakha ranks first once preference points are added.
    /// </summary>
    private async Task EnsureEvaluationDemoAsync(EProcureDbContext db, Organisation organisation, ApplicationUser createdBy,
        ApplicationUser[] bidders, CancellationToken cancellationToken)
    {
        const string reference = "RBIDZ/2026/011";
        if (await db.Tenders.AnyAsync(t => t.OrganisationId == organisation.Id && t.ReferenceNumber == reference, cancellationToken)) return;

        var files = _services.GetRequiredService<Services.External.IFileStorage>();
        var closing = DateTime.UtcNow.Date.AddDays(-3).AddHours(9); // 11:00 SAST three days ago
        var requirementNames = new[] { "CSD registration summary", "B-BBEE certificate or sworn affidavit", "Pricing schedule" };
        var tender = new Tender
        {
            OrganisationId = organisation.Id,
            Title = "Printing and document management services (36 months)",
            ReferenceNumber = reference,
            Category = "Professional Services",
            Description = "Demo tender that has already closed, for demonstrating bid opening, evaluation by the BEC and the BAC award. Managed printing, scanning and records digitisation for the RBIDZ offices over 36 months.",
            ClosingDateUtc = closing,
            TenderFee = 0m,
            EstimatedValue = 1_400_000m,
            PointSystem = PreferencePointSystem.EightyTwenty,
            Status = TenderStatus.Published,
            CreatedByUserId = createdBy.Id,
            CreatedAtUtc = closing.AddDays(-30),
            PublishedAtUtc = closing.AddDays(-30),
            Requirements = requirementNames.Select((name, index) => new TenderRequirement { Name = name, IsMandatory = true, SortOrder = index + 1 }).ToList()
        };
        db.Tenders.Add(tender);
        await db.SaveChangesAsync(cancellationToken);

        var prices = new[] { "R1 380 000.00", "R1 150 000.00", "R1 240 000.00" };
        for (var i = 0; i < bidders.Length; i++)
        {
            var bidder = bidders[i];
            var company = await db.SupplierProfiles.Where(p => p.UserId == bidder.Id).Select(p => p.Company!).SingleAsync(cancellationToken);
            var submittedAt = closing.AddDays(-2).AddHours(i * 3);
            var submission = new Submission
            {
                TenderId = tender.Id,
                CompanyId = company.Id,
                SubmittedByUserId = bidder.Id,
                Status = SubmissionStatus.Submitted,
                IsCsdRegistered = true,
                IsTaxCompliant = true,
                DeclaredBbbeeLevel = company.BbbeeLevel,
                HasDeclaredInterest = i == 1,
                InterestDetails = i == 1 ? "A director's sister works in the RBIDZ finance department (not in supply chain management)." : null,
                ConfirmsNotRestricted = true,
                ConfirmsIndependentBid = true,
                DeclaredAtUtc = submittedAt,
                PaymentStatus = PaymentStatus.NotRequired,
                CreatedAtUtc = submittedAt.AddHours(-1),
                SubmittedAtUtc = submittedAt
            };
            submission.StatusHistory.Add(new SubmissionStatusHistory { ToStatus = SubmissionStatus.Draft, ChangedByUserId = bidder.Id, ChangedAtUtc = submittedAt.AddHours(-1), Note = "Application started" });
            submission.StatusHistory.Add(new SubmissionStatusHistory { FromStatus = SubmissionStatus.Draft, ToStatus = SubmissionStatus.Submitted, ChangedByUserId = bidder.Id, ChangedAtUtc = submittedAt, Note = "Submitted (no tender fee)" });

            foreach (var requirement in tender.Requirements)
            {
                var text = requirement.Name == "Pricing schedule"
                    ? $"Pricing schedule - {company.Name} - Total bid price for 36 months, including VAT: {prices[i]}"
                    : $"{requirement.Name} - {company.Name} - demo document";
                var bytes = DemoPdf(text);
                var key = await files.SaveAsync(new MemoryStream(bytes), ".pdf", cancellationToken);
                submission.Documents.Add(new UploadedDocument
                {
                    TenderRequirementId = requirement.Id,
                    OriginalFileName = $"{requirement.Name.Replace(' ', '-')}.pdf",
                    StorageKey = key,
                    ContentType = "application/pdf",
                    SizeBytes = bytes.Length,
                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                    UploadedByUserId = bidder.Id,
                    UploadedAtUtc = submittedAt.AddMinutes(-30)
                });
            }

            db.Submissions.Add(submission);
            await db.SaveChangesAsync(cancellationToken);
            submission.ReferenceNumber = $"EP-{submittedAt:yyyy}-{submission.Id:D6}";
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>A minimal one-page PDF showing one line of text (for demo bid documents).</summary>
    private static byte[] DemoPdf(string text)
    {
        var safe = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $"BT /F1 12 Tf 60 740 Td ({safe}) Tj 0 -24 Td (eProcure demonstration document. Not a real record.) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string role,
        int? organisationId,
        string password,
        string phoneNumber)
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
                PhoneNumber = phoneNumber,
                PhoneNumberConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create demo user {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
        else if (!user.PhoneNumberConfirmed)
        {
            // Demo users created before phone verification existed: give them a verified number
            // so the EnsurePhoneVerifiedFilter does not lock them out.
            user.PhoneNumber ??= phoneNumber;
            user.PhoneNumberConfirmed = true;
            await userManager.UpdateAsync(user);
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
        EnterpriseSize enterpriseSize,
        string sector,
        CancellationToken cancellationToken)
    {
        var company = await db.Companies.SingleOrDefaultAsync(c => c.RegistrationNumber == registrationNumber, cancellationToken);

        // Companies seeded before AddDesignFields got the column default "Generic"; set the intended size.
        if (company is not null && company.EnterpriseSize != enterpriseSize)
        {
            company.EnterpriseSize = enterpriseSize;
            await db.SaveChangesAsync(cancellationToken);
        }

        var profile = await db.SupplierProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        if (profile is not null) return;

        if (company is null)
        {
            company = new Company
            {
                Name = companyName,
                RegistrationNumber = registrationNumber,
                TaxPin = $"DEMO-{registrationNumber.Replace("/", string.Empty)}",
                CsdNumber = csdNumber,
                BbbeeLevel = bbbeeLevel,
                EnterpriseSize = enterpriseSize,
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

    /// <summary>
    /// Demo estimated contract values, keyed by tender reference. Only fills values that are still empty,
    /// so it is safe to run on every start and never overwrites a value an admin has entered.
    /// </summary>
    private static async Task EnsureEstimatedValuesAsync(EProcureDbContext db, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, decimal>
        {
            ["RBIDZ/2026/014"] = 14_900_000m,
            ["RBIDZ/2026/015"] = 1_250_000m,
            ["RBIDZ/2026/016"] = 8_600_000m,
            ["RBIDZ/2026/017"] = 2_400_000m,
            ["RBIDZ/2026/018"] = 950_000m,
            ["MVLM/2026/007"] = 24_000_000m,
            ["MVLM/2026/008"] = 3_100_000m
        };

        var references = values.Keys.ToList();
        var tenders = await db.Tenders
            .Where(t => t.EstimatedValue == null && references.Contains(t.ReferenceNumber))
            .ToListAsync(cancellationToken);
        if (tenders.Count == 0) return;

        foreach (var tender in tenders)
        {
            tender.EstimatedValue = values[tender.ReferenceNumber];
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
