using EProcure.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EProcure.Web.Data.Configurations;

// DELETE BEHAVIOUR POLICY
// Procurement records must be kept (PFMA/MFMA record-keeping, audit by the Auditor-General),
// so almost every relationship is Restrict: the database refuses to delete a parent that
// still has children. The one Cascade is Tender -> TenderRequirement (checklist lines have
// no meaning without their tender and are edited while the tender is a draft).
// Restrict everywhere also avoids SQL Server's "multiple cascade paths" error.

public class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> b)
    {
        b.Property(o => o.Name).HasMaxLength(200).IsRequired();
        b.Property(o => o.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(o => o.Code).IsUnique();
        b.Property(o => o.LogoPath).HasMaxLength(260);
        b.Property(o => o.PrimaryColour).HasMaxLength(7).IsRequired();  // "#RRGGBB"
        b.Property(o => o.AccentColour).HasMaxLength(7).IsRequired();
    }
}

public class SupplierProfileConfiguration : IEntityTypeConfiguration<SupplierProfile>
{
    public void Configure(EntityTypeBuilder<SupplierProfile> b)
    {
        // One user -> zero or one SupplierProfile (unique FK makes it 1:0..1).
        b.HasOne(p => p.User)
            .WithOne(u => u.SupplierProfile)
            .HasForeignKey<SupplierProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.UserId).IsUnique();

        // Many profiles -> one Company (optional until the company profile is created).
        b.HasOne(p => p.Company)
            .WithMany(c => c.Profiles)
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(p => p.JobTitle).HasMaxLength(100);
        b.Property(p => p.ContactNumber).HasMaxLength(20);
    }
}

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Property(c => c.RegistrationNumber).HasMaxLength(20).IsRequired();
        b.Property(c => c.TaxPin).HasMaxLength(20).IsRequired();
        b.Property(c => c.CsdNumber).HasMaxLength(20).IsRequired();
        b.Property(c => c.Sector).HasMaxLength(100).IsRequired();

        // The same legal entity must not be registered twice.
        b.HasIndex(c => c.RegistrationNumber).IsUnique();
        b.HasIndex(c => c.CsdNumber).IsUnique();
    }
}

public class TenderConfiguration : IEntityTypeConfiguration<Tender>
{
    public void Configure(EntityTypeBuilder<Tender> b)
    {
        b.Property(t => t.Title).HasMaxLength(250).IsRequired();
        b.Property(t => t.ReferenceNumber).HasMaxLength(50).IsRequired();
        b.Property(t => t.Category).HasMaxLength(100).IsRequired();
        b.Property(t => t.Description).HasMaxLength(8000).IsRequired();
        b.Property(t => t.CancellationReason).HasMaxLength(1000);

        // Many tenders -> one Organisation (the tenant). Required.
        b.HasOne(t => t.Organisation)
            .WithMany(o => o.Tenders)
            .HasForeignKey(t => t.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Many tenders -> one creating user.
        b.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reference numbers are unique PER organisation: two organisations may both use "2026/001".
        b.HasIndex(t => new { t.OrganisationId, t.ReferenceNumber }).IsUnique();
        // Speeds up the supplier marketplace (open tenders ordered by closing date).
        b.HasIndex(t => new { t.Status, t.ClosingDateUtc });
    }
}

public class TenderRequirementConfiguration : IEntityTypeConfiguration<TenderRequirement>
{
    public void Configure(EntityTypeBuilder<TenderRequirement> b)
    {
        b.Property(r => r.Name).HasMaxLength(200).IsRequired();
        b.Property(r => r.Description).HasMaxLength(1000);

        // Many requirements -> one Tender. The only Cascade in the schema (see policy above).
        b.HasOne(r => r.Tender)
            .WithMany(t => t.Requirements)
            .HasForeignKey(r => r.TenderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> b)
    {
        b.HasOne(s => s.Tender)
            .WithMany(t => t.Submissions)
            .HasForeignKey(s => s.TenderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(s => s.Company)
            .WithMany(c => c.Submissions)
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(s => s.SubmittedByUser)
            .WithMany()
            .HasForeignKey(s => s.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // DUPLICATE PREVENTION at database level: one application per company per tender.
        // Even if two browser tabs submit at once, SQL Server rejects the second row.
        b.HasIndex(s => new { s.TenderId, s.CompanyId }).IsUnique();

        b.Property(s => s.PaymentReference).HasMaxLength(100);
        b.Property(s => s.InterestDetails).HasMaxLength(1000);
        b.Property(s => s.RestrictionDetails).HasMaxLength(1000);

        // Application reference shown to the supplier (EP-2026-000123). Assigned when payment succeeds,
        // so it is NULL on drafts; the unique index ignores NULLs so many drafts can exist.
        b.Property(s => s.ReferenceNumber).HasMaxLength(20);
        b.HasIndex(s => s.ReferenceNumber).IsUnique().HasFilter("[ReferenceNumber] IS NOT NULL");
    }
}

public class UploadedDocumentConfiguration : IEntityTypeConfiguration<UploadedDocument>
{
    public void Configure(EntityTypeBuilder<UploadedDocument> b)
    {
        b.Property(d => d.OriginalFileName).HasMaxLength(255).IsRequired();
        b.Property(d => d.StorageKey).HasMaxLength(200).IsRequired();
        b.HasIndex(d => d.StorageKey).IsUnique();
        b.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        b.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();

        b.HasOne(d => d.Submission)
            .WithMany(s => s.Documents)
            .HasForeignKey(d => d.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(d => d.TenderRequirement)
            .WithMany()
            .HasForeignKey(d => d.TenderRequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(d => d.UploadedByUser)
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SubmissionStatusHistoryConfiguration : IEntityTypeConfiguration<SubmissionStatusHistory>
{
    public void Configure(EntityTypeBuilder<SubmissionStatusHistory> b)
    {
        b.ToTable("SubmissionStatusHistory");
        b.Property(h => h.Note).HasMaxLength(1000);

        b.HasOne(h => h.Submission)
            .WithMany(s => s.StatusHistory)
            .HasForeignKey(h => h.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(h => h.ChangedByUser)
            .WithMany()
            .HasForeignKey(h => h.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AwardRecordConfiguration : IEntityTypeConfiguration<AwardRecord>
{
    public void Configure(EntityTypeBuilder<AwardRecord> b)
    {
        b.Property(a => a.Rationale).HasMaxLength(4000).IsRequired();
        b.Property(a => a.CommitteeReference).HasMaxLength(100);

        // One tender -> zero or one award (unique FK).
        b.HasOne(a => a.Tender)
            .WithOne(t => t.Award)
            .HasForeignKey<AwardRecord>(a => a.TenderId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => a.TenderId).IsUnique();

        b.HasOne(a => a.Submission)
            .WithMany()
            .HasForeignKey(a => a.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(a => a.RecordedByUser)
            .WithMany()
            .HasForeignKey(a => a.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditLog");
        b.Property(a => a.UserId).HasMaxLength(450);
        b.Property(a => a.UserEmail).HasMaxLength(256);
        b.Property(a => a.Action).HasMaxLength(64).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(64).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(64).IsRequired();
        b.Property(a => a.IpAddress).HasMaxLength(45); // fits IPv6
        b.HasIndex(a => new { a.OrganisationId, a.OccurredAtUtc });
        b.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
