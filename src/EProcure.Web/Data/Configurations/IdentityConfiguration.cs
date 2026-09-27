using EProcure.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EProcure.Web.Data.Configurations;

// Rename Identity's default "AspNetUsers"/"AspNetRoles"... tables to the names in the brief.

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("Users");
        b.Property(u => u.FullName).HasMaxLength(150).IsRequired();

        // Many admin users -> one Organisation. Optional because suppliers have none.
        // Restrict: an organisation that still has users cannot be deleted by accident.
        b.HasOne(u => u.Organisation)
            .WithMany(o => o.Users)
            .HasForeignKey(u => u.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(u => u.OrganisationId);
    }
}

public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> b) => b.ToTable("Roles");
}

public class IdentityJoinTablesConfiguration :
    IEntityTypeConfiguration<IdentityUserRole<string>>,
    IEntityTypeConfiguration<IdentityUserClaim<string>>,
    IEntityTypeConfiguration<IdentityUserLogin<string>>,
    IEntityTypeConfiguration<IdentityUserToken<string>>,
    IEntityTypeConfiguration<IdentityRoleClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<string>> b) => b.ToTable("UserRoles");
    public void Configure(EntityTypeBuilder<IdentityUserClaim<string>> b) => b.ToTable("UserClaims");
    public void Configure(EntityTypeBuilder<IdentityUserLogin<string>> b) => b.ToTable("UserLogins");
    public void Configure(EntityTypeBuilder<IdentityUserToken<string>> b) => b.ToTable("UserTokens");
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<string>> b) => b.ToTable("RoleClaims");
}
