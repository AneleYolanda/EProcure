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

---

## Design phase, part 1: design assets, phone OTP, AddDesignFields (2026-09-28)

### What happened (honest record)
- `f38842f` added the prototype sources (`docs/design/`), `DESIGN_SPEC.md`, local fonts, the RBIDZ logo and splash
  photo, and a starter `eprocure.css`. **Its commit message overstates it**: no screens were restyled in that commit.
- `c3b93c6`…`374593f` added a splash view, starter partials and a VerifyPhone *stub*.
- `af18460` completed phone OTP registration (see below).
- The screens are **not yet** a pixel match with the prototypes. That restyle is the next piece of work.

### Added in this part
- **Phone OTP on registration**: POPIA consent enforced server-side; SA cellphone validated and stored as +27…;
  code generated with Identity's phone token provider and sent through `IOtpSender` (`MockOtpSender` shows it on
  screen in Development); `VerifyPhone` + `ResendCode`; `EnsurePhoneVerifiedFilter` keeps unverified suppliers out
  of the Supplier area.
- **Migration `AddDesignFields`** (`docs/schema/AddDesignFields.sql`): `Tenders.EstimatedValue`,
  `Companies.EnterpriseSize` (existing rows default to `Generic`; the generated default `""` was hand-edited because it
  is not a valid enum name), `Submissions.ReferenceNumber` (unique where not NULL), RBIDZ branding re-seeded to
  `#0F1B33` / `#1CA3EC` / `rbidz-logo.png`.
- **DemoDataSeeder**: demo users get fictitious verified phone numbers (also back-filled for existing users),
  companies get an enterprise size, tenders get estimated values (only fills empty values).
- **Fix**: anonymous visitors to `/` crashed with a NullReferenceException (the controller returned the home view
  without a model); they now get the splash view.

### Environment note
Windows **Smart App Control** blocked the freshly built `EProcure.Web.dll` ("An Application Control policy has blocked
this file"), which stopped `dotnet ef` and the agent. It was turned off by the developer on 2026-09-28.

### Manual tests
1. Open `/` while signed out → splash page, "Continue" goes to login (no error page).
2. Register a new supplier without ticking POPIA consent → field error, no account created.
3. Register with an invalid cellphone (e.g. `12345`) → field error.
4. Register correctly → redirected to Verify phone, yellow DEMO MODE box shows a 6-digit code.
5. Enter a wrong code → "That code is incorrect or has expired." Enter the right code → supplier dashboard.
6. Register another supplier, skip verification, open `/Supplier/Dashboard` → redirected to Verify phone.
7. Sign in as `supplier1@demo.co.za` → goes straight to the dashboard (demo users are pre-verified).
8. SQL: `SELECT Code, PrimaryColour, AccentColour, LogoPath FROM Organisations` → RBIDZ `#0F1B33` / `#1CA3EC` / `/img/orgs/rbidz-logo.png`.
9. SQL: `SELECT Name, EnterpriseSize FROM Companies` → QSE and EME; `SELECT ReferenceNumber, EstimatedValue FROM Tenders` → all filled.

### Commit message
```
feat: AddDesignFields migration, demo data for design fields, splash crash fix
```

---

## Design phase, part 2: screens restyled to match the prototypes (2026-09-28)

### Added / changed
- `wwwroot/css/eprocure.css`: the design system (tokens and components copied from the prototypes);
  `fonts.css`: IBM Plex Sans variable font, latin + latin-ext. Bootstrap CSS, `site.css` and the unused Archivo
  fonts removed (DECISIONS D19).
- **Bidder screens** (phone-app column, D20): splash (continues to sign-in by itself), sign in, create your profile,
  verify your number (6 boxes, works without JavaScript), tender feed (search, category chips, cards with
  "You qualify" / "Not eligible", closing date, fee, "Closes in N days"), profile and settings with sign out,
  bottom tab bar, access denied, error page, POPIA notice.
- **Admin console**: `/admin/{orgCode}` branded splash and split-screen sign-in (D22, Development-only demo
  account cards); sidebar with "Soon" items (D21); top bar with role badge ("SCM Officer" / "BEC member");
  dashboard with 4 KPI cards, Needs your action, Pipeline by stage and Compliance watch, all from real data.
  Below 992px the sidebar becomes a slide-out menu.
- New helpers: `DisplayFormat` (R8 600 000, R500.00, dates in SAST), `BrandColours` (validated #RRGGBB),
  `RoleLabels`, `EligibilityRules` (D24). New `Supplier/ProfileController` and `OrgPortalController`.
- MVLM placeholder logo redrawn as a round emblem (it sits in the design's circular logo badge).

### Bugs found and fixed while testing in the browser
1. **Demo OTP never appeared and registration would throw**: `MockOtpSender` wrote to Session, which is not
   enabled. It now uses TempData (D23).
2. **Area views had no `_ViewImports.cshtml`**: the admin menu links and the admin sign-out form were not turned
   into real links/forms (no anti-forgery token). Added to both areas.
3. **Unverified suppliers were sent to `/Supplier/Account/VerifyPhone` (a 404)**: the filter kept the current
   area. It now redirects with `area = ""`.
4. The OTP boxes overflowed the phone screen (a `<fieldset>` does not shrink by default).
5. Cellphone numbers typed with spaces ("082 123 4567", the design's own format) were rejected.
6. The profile header showed name and email on one line.

### Verified in the browser (402x874 phone and 1200x760 desktop)
- Splash continues to sign-in. Supplier One sees tenders from **both** organisations, all "You qualify" (Level 1).
- Supplier Two (Level 6) sees "Not eligible" on the two tenders that require Level 4.
- Registration without POPIA consent is refused. With consent, the Verify page shows the demo code; a wrong code
  gives "That code is incorrect or has expired."; an unverified user opening `/Supplier/Dashboard` is sent to
  Verify; the right code opens the tender feed.
- `/admin/rbidz/login` is navy `#0F1B33` / blue `#1CA3EC` with the RBIDZ logo. `admin@mvlm.demo` signing in there
  gets "Invalid email or password." and is **not** left signed in.
- `admin@rbidz.demo` reaches the RBIDZ dashboard (3 open, 1 draft, 1 closed, 3 action items).
- `/admin/mvlm/login` is teal / coral; the MVLM admin sees only MVLM data (2 open, 0 drafts, nothing to evaluate).
- Admin console at phone width: cards reflow, the menu button opens the sidebar drawer.
- A throwaway test supplier `test.supplier1@example.test` was created in the local database during testing.

### Known differences from the prototype (intentional, see DECISIONS D20 to D25)
- No iOS status-bar space and no device frame. 6 OTP boxes instead of 5. "Confirm password" added to registration.
- "Soon" items instead of Approval queue, BEC/BAC, Roles and users, Applications; TenderBuddy, "For you",
  notifications and the company switcher hidden. Tender cards are not clickable until the detail page (Step 5).
- The admin KPIs use our real measures (open, applications, awaiting evaluation, drafts) instead of the prototype's
  award statistics, and the sign-in page copy only claims features we have.
- The design's white-on-blue buttons do not meet WCAG AA contrast; flagged in D25.

### Manual tests
1. Signed out, open `/`: splash, then the sign-in page after about 2.5 s (or tap Skip).
2. Sign in as `supplier1@demo.co.za`: feed with 5 tenders from RBIDZ and MVLM, badges "You qualify".
3. Type "road" in the search box: only the MVLM road tender. Tap a category chip: only that category.
4. Sign in as `supplier2@demo.co.za`: RBIDZ/2026/014 and MVLM/2026/007 show "Not eligible".
5. Profile tab: name, email, company, verified cellphone, POPIA consent date; Sign out works.
6. Register a new supplier: without consent it is refused; with consent the code is shown, verify, then the feed.
7. Open `/admin/rbidz`: RBIDZ splash, then sign-in. Try `admin@mvlm.demo`: generic error. `admin@rbidz.demo`: dashboard.
8. Open `/admin/mvlm`: teal/coral. `admin@mvlm.demo`: MVLM-only dashboard.
9. Open `/admin/unknown`: 404.
10. Make the browser narrower than 992px on the dashboard: menu button appears and the sidebar slides out.

### Commit message
```
feat: restyle bidder app and admin console to the Claude Design prototypes
```
