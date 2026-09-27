using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Web.Data;

/// <summary>
/// Reference data that every environment needs, baked into the migration with HasData.
/// Fixed ids and timestamps are required: HasData compares values between migrations,
/// so anything random (Guid.NewGuid, DateTime.Now) would create a new migration every time.
///
/// Demo USERS are NOT seeded here: they need passwords, and passwords do not belong in
/// source code or migrations. They are created at start-up in step 2 from user-secrets.
/// </summary>
public static class SeedData
{
    public const int RbidzId = 1;
    public const int MzansiValleyId = 2;

    private static readonly DateTime SeededAt = new(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc);

    public static void Apply(ModelBuilder builder)
    {
        builder.Entity<Organisation>().HasData(
            new Organisation
            {
                Id = RbidzId,
                Name = "Richards Bay Industrial Development Zone",
                Code = "RBIDZ",
                LogoPath = "/img/orgs/rbidz.svg",
                PrimaryColour = "#0B2545", // navy
                AccentColour = "#C9A227",  // gold
                DefaultPointSystem = PreferencePointSystem.NinetyTen,
                IsActive = true,
                CreatedAtUtc = SeededAt
            },
            new Organisation
            {
                // FICTIONAL demo municipality, so the multi-tenant branding is visible.
                Id = MzansiValleyId,
                Name = "Mzansi Valley Local Municipality",
                Code = "MVLM",
                LogoPath = "/img/orgs/mvlm.svg",
                PrimaryColour = "#00695C", // teal
                AccentColour = "#E4572E",  // coral
                DefaultPointSystem = PreferencePointSystem.EightyTwenty,
                IsActive = true,
                CreatedAtUtc = SeededAt
            });

        builder.Entity<ApplicationRole>().HasData(
            Role("2c5e174e-3b0e-446f-86af-483d56fd7210", AppRoles.Supplier),
            Role("8e445865-a24d-4543-a6c6-9443d048cdb9", AppRoles.OrgAdmin),
            Role("b5c1a2d3-7f4e-4c1a-9d2b-3e6f7a8b9c0d", AppRoles.Evaluator));
    }

    private static ApplicationRole Role(string id, string name) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id // fixed value, see class comment
    };
}
