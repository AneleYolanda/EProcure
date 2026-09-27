# eProcure: Build Log

One entry per working step: what was added, how to verify it, and the commit message.

---

## Step 1: Solution structure, data model, first migration (2026-09-27)

### Added
- `EProcure.sln` + `src/EProcure.Web` (ASP.NET Core MVC, `net8.0`), created with the dotnet CLI.
  - Template was `net10.0`-only (SDK 10); retargeted to `net8.0`, removed .NET 9+ APIs
    (`MapStaticAssets`, `WithStaticAssets`, `<script type="importmap">`) and used `UseStaticFiles()`.
- Packages (pinned): `Microsoft.EntityFrameworkCore.SqlServer`, `.Design`,
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, all **8.0.31**.
- Local tool manifest `dotnet-tools.json` pinning `dotnet-ef` **8.0.31** (`dotnet tool restore`).
- `Domain/`: Organisation, ApplicationUser/ApplicationRole (+ `AppRoles`), SupplierProfile, Company,
  Tender, TenderRequirement, Submission, UploadedDocument, SubmissionStatusHistory, AwardRecord,
  AuditEntry, enums.
- `Tenancy/`: `ITenantContext`, `HttpTenantContext` (reads org from the auth cookie claim).
- `Data/`: `EProcureDbContext` (tenant query filters), `Configurations/` (keys, lengths, indexes,
  delete rules, Identity table names), `SeedData` (2 organisations, 3 roles),
  `Migrations/…_InitialCreate`.
- `Program.cs`: DbContext, tenant context, Identity (password ≥ 10 chars, lockout after 5 failures),
  authentication middleware.
- `appsettings.Development.json`: LocalDB connection string (Windows auth, **no password**).
- `wwwroot/img/orgs/`: placeholder logos (rbidz.svg, mvlm.svg).
- Docs: `DECISIONS.md`, `BUILD_LOG.md`, `docs/DATA_MODEL.md`, `docs/schema/InitialCreate.sql`.

### Prerequisites on the laptop
- Visual Studio 2026 with "ASP.NET and web development" workload (includes LocalDB).
- **.NET 8 ASP.NET Core Runtime** (VS Installer → Individual components → ".NET 8.0 Runtime", or
  the free download from dot.net). Without it the `net8.0` app will not start.

### Manual tests
1. **Build:** open `EProcure.sln` in Visual Studio → Build → Build Solution. Expect 0 errors.
2. **Create the database:** in a terminal at the repository root:
   ```
   dotnet tool restore
   dotnet ef database update --project src/EProcure.Web
   ```
   Expect `Done.`
3. **Inspect schema:** View → SQL Server Object Explorer → `(localdb)\MSSQLLocalDB` → Databases →
   `EProcure` → Tables. Expect 17 tables incl. `Users`, `Roles`, `Organisations`, `Tenders`,
   `Submissions`, `AuditLog`, `__EFMigrationsHistory`.
4. **Seed data:** right-click `Organisations` → View Data. Expect RBIDZ (#0B2545 / #C9A227, NinetyTen)
   and MVLM (#00695C / #E4572E, EightyTwenty). `Roles` shows Supplier, OrgAdmin, Evaluator.
5. **Enums as text:** `Organisations.DefaultPointSystem` shows `NinetyTen`, not `2`.
6. **Duplicate protection:** run in a new query window against `EProcure`:
   ```sql
   INSERT INTO Organisations (Name, Code, PrimaryColour, AccentColour, DefaultPointSystem, IsActive, CreatedAtUtc)
   VALUES ('Dup', 'RBIDZ', '#000000', '#000000', 'EightyTwenty', 1, SYSUTCDATETIME());
   ```
   Expect error: *Cannot insert duplicate key row … IX_Organisations_Code*.
   (The "cannot delete an organisation that has tenders" test is added in the next step, once tenders exist.)

### Verified during generation
- Build: 0 warnings, 0 errors.
- Migration SQL applied to LocalDB (SQL Server 17): 17 tables created, 2 organisations and 3 roles seeded,
  `__EFMigrationsHistory` contains `…_InitialCreate`.
- Tenant query filters compile into the model; their runtime SQL will be demonstrated in step 7
  (OrgAdmin sees own organisation only).

### Commit message
```
feat: solution skeleton, multi-tenant data model and initial migration

- ASP.NET Core MVC (net8.0) solution created via dotnet CLI
- EF Core 8.0.31 + Identity; dotnet-ef pinned in local tool manifest
- Entities: Organisation (tenant), ApplicationUser/Role (Users/Roles tables),
  SupplierProfile, Company, Tender, TenderRequirement, Submission,
  UploadedDocument, SubmissionStatusHistory, AwardRecord, AuditEntry
- Tenant isolation via EF Core global query filters driven by ITenantContext
  (org id from auth cookie claim; fails closed)
- Unique indexes: one submission per company per tender, tender ref per org
- Restrict deletes for records retention; enums stored as text
- Seed: RBIDZ and Mzansi Valley LM organisations, Supplier/OrgAdmin/Evaluator roles
 Docs: DECISIONS.md, BUILD_LOG.md, docs/DATA_MODEL.md, InitialCreate.sql
```

---

## Step 2: Authentication, roles, tenant claim, demo data and branded layouts (2026-09-28)

### Added
- Custom supplier-only account flow with Login, Register, Logout and AccessDenied views.
- Generic login errors, lockout-on-failure, local return-url validation and POPIA consent timestamp.
- `OrganisationClaimsPrincipalFactory` adding `eprocure:org_id` from `TenantClaimTypes.OrganisationId`.
- Global antiforgery protection, SupplierOnly/OrgStaff/OrgAdminOnly policies and authenticated fallback policy.
- Audit service events for `Account.Registered` and `Account.LoginFailed`.
- Admin and Supplier areas with protected dashboards and area-first routing.
- Request-scoped organisation branding service and validated CSS colour variables.
- Mobile-first public/supplier layout, supplier bottom navigation and responsive branded admin layout.
- Public home page with six latest open tenders across organisations, SAST dates and Rand formatting.
- Plain-language POPIA privacy notice.
- Development-only, idempotent demo seeder for five users, two supplier companies and seven tenders.
- `Infrastructure/SaTime.cs` with Windows time-zone lookup and fixed UTC+2 fallback.

### Manual tests
1. Build the solution: expect 0 errors and 0 warnings.
2. Configure `DemoSeed:Password` with user-secrets and start in Development: expect five users and seven tenders in LocalDB.
3. Open `/`: expect HTTP 200 and tender cards; draft and expired demo tenders are excluded.
4. Open `/Account/Login`: expect HTTP 200; anonymous `/Admin/Dashboard` redirects to login.
5. Log in over HTTPS as `admin@rbidz.demo`: expect redirect to `/Admin/Dashboard`, navy/gold RBIDZ branding and RBIDZ-only metrics.
6. Log in over HTTPS as `admin@mvlm.demo`: expect teal/coral MVLM branding and no RBIDZ branding.
7. Open `/Home/Privacy`: expect HTTP 200 and the POPIA notice.
8. Resize the public and admin layouts to 375px: expect responsive navigation and no intentional horizontal overflow.
9. Run the app a second time: expect no duplicate demo users or tenders.

### Environment note
- The existing `C:\Users\techn\EProcure.mdf` and log were attached to LocalDB because the database name existed while the migration tool initially reported a missing database. The migration was already current afterward.

### Commit message
```
feat: authentication, roles, tenant claim, branded layouts, and demo data
```
```
