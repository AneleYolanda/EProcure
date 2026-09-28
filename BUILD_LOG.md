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

---

## Step 3: Organisation admins create, edit, publish and cancel tenders (2026-09-28)

### Added
- **Migration `AddTenderPublishingFields`** (`docs/schema/AddTenderPublishingFields.sql`): `Tenders.PointSystem`
  (existing rows set to EightyTwenty; generated default "" hand-edited, as in AddDesignFields),
  `CancelledAtUtc`, `CancellationReason`.
- `Services/TenderService` (+ `ITenderService`): every tender rule in one place. Organisation and creator always come
  from the signed-in user; reference unique per organisation (checked first, and the database's unique index caught
  with a friendly message); closing date at least 1 hour ahead; 1 to 20 required documents; only drafts can be
  edited; publishing re-checks everything on the server; cancelling needs a reason and deletes nothing.
- `Services/TenderStages`, `TenderCatalog` (categories and the standard SA bid documents).
- `Areas/Admin/TendersController` and views, in the prototype's design:
  - **Tender register** (`/Admin/Tenders`): stage filter pills with counts, search (top bar), table, "Create tender".
  - **Create / Edit draft**: step list + cards for details, pre-qualification (minimum B-BBEE level),
    evaluation method (80/20 or 90/10, pre-selected from the organisation's default) and documents (tick-boxes plus
    "other, one per line") (DECISIONS D26).
  - **Tender page**: facts, scope, pre-qualification, documents; publish card with checks and an "I confirm" tick box;
    cancel page with a required reason; history timeline from the audit trail.
- Sidebar "Tender register" is now a real link; the top-bar search searches tenders; dashboard "Needs your action"
  items link to the tender.
- `Program.cs`: fixed request culture (DECISIONS D27). Admin layout now loads jQuery for form validation.
- Audit: `Tender.Created`, `Tender.Updated`, `Tender.Published`, `Tender.Cancelled` with the organisation id.

### Verified in the browser
- RBIDZ register lists 5 tenders with correct stages (1 Draft, 3 Advertised, 1 Under evaluation).
- Create with an existing reference and a past closing date: both errors shown, typed values kept ("500.00" read
  correctly). Create correctly: draft `RBIDZ/2026/021` with 8 documents, 90/10 pre-selected (RBIDZ default).
- Publish without the tick box: refused. With it: stage Advertised, history "Draft created" then "Published".
- Opening Edit on a published tender: back to the tender page with "Only draft tenders can be edited."
- Cancel with a 2-character reason: refused. A test draft `TEST/CANCEL/001` cancelled with a reason: stage
  Cancelled, reason on the page and in the history, Cancel button gone.
- As `admin@mvlm.demo`: RBIDZ tender ids 1 and 4 (details, edit, cancel) return **404**; MVLM's own tender opens;
  the register lists only the 2 MVLM tenders; a publish POST without the anti-forgery token returns 400.
- As `evaluator@rbidz.demo` (BEC member): no Create / Edit / Publish / Cancel controls; direct GET Create and POST
  Publish (with a valid token) both end on Access denied; the draft stays a draft (DECISIONS D28).
- As `supplier2@demo.co.za`: `RBIDZ/2026/021` appears in the marketplace ("Not eligible": needs Level 4);
  the draft `RBIDZ/2026/017` and the cancelled test tender do not.
- Test data left in the local database: tender `RBIDZ/2026/021` (published) and `TEST/CANCEL/001` (cancelled).

### Manual tests
1. Sign in at `/admin/rbidz` as `admin@rbidz.demo`; open Tender register: stage pills and table.
2. Create tender with reference `RBIDZ/2026/014`: "Your organisation already has a tender with this reference."
3. Same reference in MVLM (`admin@mvlm.demo`): allowed (unique per organisation, not globally).
4. Closing date in the past: "The closing date must be at least 1 hour from now."
5. Save a valid draft: it is NOT in the supplier marketplace.
6. Publish without ticking the box: refused. Tick and publish: it appears in the marketplace with the RBIDZ name.
7. Try to edit the published tender: not possible.
8. Cancel a draft with a short reason: refused. With a proper reason: Cancelled, reason kept, visible in History.
9. Sign in as `evaluator@rbidz.demo`: tenders visible, no create/edit/publish/cancel; `/Admin/Tenders/Create` goes to
   Access denied.
10. As `admin@mvlm.demo`, open `/Admin/Tenders/Details/1` (an RBIDZ tender): 404.
11. SQL: `SELECT Action, EntityId, OrganisationId, OccurredAtUtc FROM AuditLog WHERE EntityType='Tender'` shows
    Tender.Created / Published / Cancelled with OrganisationId 1.
12. SQL: `DELETE FROM Organisations WHERE Code='RBIDZ'` fails with a foreign-key error (restrict delete).

### Commit message
```
feat: org admins create, edit, publish and cancel tenders
```

---

## Supplier side as a responsive web app (2026-09-28)

### Changed
- `Views/Shared/_Layout.cshtml`: three layouts chosen per page with `ViewData["Shell"]`: "app" (top navigation bar on
  computers, bottom tab bar on phones), "auth" (split screen on computers), "splash" (full screen). DECISIONS D30.
- `eprocure.css` section 15: web layout from 768px (top bar, 1200px content, tender card grid, page headings, profile
  and privacy pages at 760px) and split-screen sign-in from 992px. Phones are unchanged.
- Tender feed: a proper page heading on computers ("Open tenders", count, organisations, company checked against).
- Splash photo cropped to the buildings on wide screens (the photo has text printed on its left side).

### Verified in the browser (1200x760)
- Sign in and Register: split screen, brand panel left, form right; no printed text from the photo showing.
- Splash: full screen, branding left, buildings right.
- After a laptop restart (LocalDB had stopped starting), verified against the Visual Studio run at 1200x760:
  tender feed with top navigation, "Open tenders" heading and a 3-column card grid; profile page centred;
  no horizontal scrolling; bottom tab bar hidden. At 402px the phone layout and tab bar are unchanged.

### Manual tests
1. On a computer, open `/Account/Login`: split screen. Make the window narrow (phone width): the phone layout returns.
2. Sign in as `supplier1@demo.co.za`: top navigation bar, "Open tenders" heading, tender cards in 2 to 3 columns.
3. Profile: centred page, Sign out in the top bar and on the page.
4. On a phone (or narrow window): bottom tab bar as before.

### Commit message
```
feat: supplier side as a responsive web app on tablets and computers
```

---

## Step 4: Supplier company profile (2026-09-28)

### Added
- `Services/CompanyService` (+ `ICompanyService`): company found through the user's own SupplierProfile; format
  normalisation; unique registration and CSD numbers; identity locked once the company has applications;
  audit `Company.Created` / `Company.Updated` (OrganisationId NULL: suppliers belong to no organisation).
- `Areas/Supplier/CompanyController` + views (design screens "sProfile" and "sAddCo"): company card with EME/QSE
  badge, B-BBEE, sector and tax chips; Add / Edit form; empty state. DECISIONS D31.
- After phone verification a new supplier goes to "Add your company" ("Step 2 of 2", with "Skip for now").
- Links: Profile row "Company", the feed's company tile and the "Add your company" banner.

### Verified in the browser (against the Visual Studio run, 1200x760)
- Test supplier without a company: empty state, then the form.
- Bad tax PIN ("12345") and CSD ("ABC123"): server-side errors shown (browser checks switched off for the test).
- Registration number already used by Supplier One's company: "This company is already registered on eProcure...".
- Valid save: company card shows the details; CSD typed in lower case stored as `MAAA0384471`.
- Feed: "checked against Thandeka Supply & Projects (Pty) Ltd"; Level 2 qualifies for the Level 4 tenders and is
  "Not eligible" for a tender requiring Level 1.

### Manual tests
1. Register a new supplier and verify the phone: you land on "Add your company", Step 2 of 2.
2. Enter `2019/12345/07` as CIPC: format error. Enter Supplier One's `2018/123456/07`: "already registered".
3. Save a valid company: the card appears; the feed shows eligibility badges for your level.
4. Edit the company and change the B-BBEE level to Level 8: tenders with a Level 4 minimum become "Not eligible".
5. SQL: `SELECT Action, EntityId, OrganisationId FROM AuditLog WHERE EntityType='Company'` shows Company.Created.

### Commit message
```
feat: supplier company profile with eligibility checks
```

---

## Steps 5 and 6: tender page for suppliers, and applying with document uploads (2026-09-28)

### Added
- **Migration `AddSubmissionDeclarations`** (`docs/schema/AddSubmissionDeclarations.sql`). In Development the app now
  applies pending migrations at start-up (`Database.MigrateAsync`); production uses the SQL scripts.
- **External services behind interfaces, chosen in `appsettings.json` "ExternalServices"**: `IFileStorage` →
  `LocalFileStorage`; `IPaymentGateway` → `MockPaymentGateway` (+ `MockGatewayController`, the demo provider page);
  `IOtpSender` → `MockOtpSender`. `Uploads:MaxFileSizeBytes` = 5 MB.
- `Infrastructure/PdfValidator` (DECISIONS D33), `Services/ApplicationService` (all rules; D32 to D35).
- **Step 5** `Supplier/TendersController.Details` (design "sDetail"): organisation, eligibility banner, key facts, scope,
  pre-qualification against the supplier's company, required documents, and the right action (Apply / Continue /
  Add your company / Not eligible / Closed). Feed cards now link here.
- **Step 6** `Supplier/ApplicationsController` + views (design "sApply", "sGate", "sPay", "sConfirm"):
  1 Bidding as, 2 Compliance, 3 SBD 4/8/9 declarations, 4 one PDF upload per checklist item (replace / remove),
  5 review + declaration; then payment (fee > 0) or straight submission (no fee); confirmation with reference
  `EP-{year}-{000000}`. Status history and audit on every change.
- `.gitignore` repaired: an earlier edit had written literal "\n" characters, so `App_Data/uploads/` was NOT ignored.

### Verified in the browser (Visual Studio run, 1200x760), as the test supplier (Level 2) on RBIDZ/2026/014 (R500)
- Tender page: qualifies banner, facts, ✓ on B-BBEE Level 4, 5 required documents, "Apply for this tender".
- Step 2 with no answers: "Answer both questions." Step 3 with SBD 4 = Yes and no details: "Give the name of the person...".
- Uploads: a text file renamed .pdf rejected ("not a valid PDF"); a .docx rejected ("Only PDF files"); a 5.5 MB file
  rejected ("larger than 5 MB"); 4 real PDFs accepted and listed with name, size and time; files stored under
  `App_Data/uploads/2026/09/` with random names.
- Review with 1 document missing: listed, button disabled; forcing the submit request anyway is refused by the server;
  submitting without the declaration tick is refused.
- Payment: "Simulate failed payment" → back to the payment page, "did not go through... nothing was charged";
  a forged return link with a made-up reference → "That payment does not belong to this application";
  "Simulate successful payment" → "Payment verified. Application submitted.", reference EP-2026-000002, 5 documents.
- After submission: the wizard redirects to the confirmation (locked); pressing Apply again reopens the same
  application (no duplicate).
- Hard stop: the Level 1 tender → "This tender requires B-BBEE Level 1 or better. Your company is B-BBEE Level 2",
  with 7 qualifying tenders offered.
- RBIDZ admin dashboard: "Applications received 2" (paid only); register shows "2 bids" on RBIDZ/2026/014.
- Two small fixes made after this run and not yet re-checked in the browser: answers are kept when a step shows an
  error, and the question text sits inside its card (was drawn on the card's border).

### Manual tests
1. As a supplier, open a tender from the feed: organisation name, eligibility banner, checklist.
2. Apply. In step 4 upload a Word file: refused. Upload a PDF over 5 MB: refused. Upload PDFs for every item.
3. Review: remove one document first and see it listed as missing; the button stays disabled.
4. Tick the declaration and continue. On the demo gateway choose "failed", then pay again and choose "successful".
5. Confirmation shows the EP- reference. As the organisation's admin, the dashboard counts the application.
6. Try to apply for a tender above your B-BBEE level: the hard-stop page, nothing created.
7. Apply for a free tender (e.g. RBIDZ/2026/015): after Review it is submitted straight away, no payment.

### Commit message
```
feat: tender page for suppliers and application wizard with PDF uploads and payment
```

---

## Step 7: Application tracking (suppliers) and application review (organisations)

### What was built
- **Supplier** `Applications/Index` ("My applications", design "sApps"): every application with its status, tender and
  organisation. `Applications/Details`: status banner, the full timeline, their SBD answers, documents (downloadable
  by the owner only) and payment reference. Tab bar and web nav link "Applications"; the confirmation page links here.
- **Admin** `Submissions/Index` (register of submitted applications, filter by tender) and `Submissions/Details`
  (company, SBD answers with red flags, documents with SHA-256 fingerprint and download, history, status form).
  Tender details has a "View applications" button. Sidebar: Applications and Audit trail are now live.
- **Admin** `Audit/Index`: the organisation's audit trail, newest first, 50 per page, read-only.
- `SubmissionStatuses`: labels, badge colours, allowed next statuses and red flags in one place.

### Verified in the browser (Visual Studio run)
- RBIDZ admin: 2 applications listed (EP-2026-000001, EP-2026-000002). Details shows 5 documents, answers and history.
  Download returns the PDF (`application/pdf`, attachment). Status set to Under evaluation with a note: flash shown,
  history updated, form now offers only "Not awarded". Forged request for "Awarded" → "That status change is not allowed."
- Audit trail: Submission.Viewed, Document.Downloaded and Submission.StatusChanged recorded with name and IP.
- MVLM admin: Applications empty; RBIDZ application and document ids → 404; MVLM audit has no RBIDZ entries.
- RBIDZ evaluator: no status form; a forced status POST → Access denied; can read and download.
- Test supplier: list shows "Under evaluation"; the timeline shows the note from "Richards Bay Industrial Development
  Zone"; own PDF downloads; another supplier's application and document → 404.
- Step 6 fixes re-checked: SBD question text sits inside its card; after the "give details" error the three answers
  are still selected.
- Small fix after testing: long audit details show in full on hover.

### Manual tests
1. As a supplier, open Applications: every application with its status. Open one and download a document.
2. As the organisation's SCM Officer, open Applications, open a bid, download a PDF, set "Under evaluation" with a note.
3. As the supplier again: the note appears on the timeline, from the organisation (not a person).
4. Open Audit trail: the view, download and status change are listed.
5. As the other organisation's admin, paste the first organisation's application URL: not found.
6. As the evaluator: no status form.

### Commit message
```
feat: application tracking for suppliers and application review for organisations
```

---

## Step 8: Automated tests

### What was built
- `tests/EProcure.Tests` (xUnit, .NET 8), added to the solution. 72 tests, about 3 seconds, no database server needed.
- `Support/TestDb`: the real `EProcureDbContext` on in-memory SQLite, with helpers to add users, suppliers, tenders
  and applications. `TestTenant` plays "staff of organisation X" or "marketplace (supplier)".
- **Rules** (pure functions): B-BBEE eligibility (at/above/below minimum, no minimum, no company, non-compliant),
  PDF validator (extension, content type, signature, empty, exactly 5 MB vs 5 MB + 1 byte), allowed status changes
  (never "Awarded"), red flags.
- **Tenant isolation**: own tenders only; another organisation's tender is not found even by id; drafts and unpaid
  applications invisible; documents and history follow their application; audit trail per organisation; a staff user
  with no organisation sees nothing; the marketplace sees every organisation.
- **Application journey** (real `ApplicationService`): hard stop creates nothing and is audited; closed, draft and
  cancelled tenders refused; applying again reopens the same application; the database rejects a duplicate;
  another supplier cannot load the application; nothing changes after the closing date or after a B-BBEE downgrade;
  renamed text file and Word file rejected and not stored; valid PDF stored with SHA-256; re-upload replaces;
  a checklist item from another tender refused; declarations need details; missing document or unticked declaration
  blocks submitting; free tender submits at once; paid tender hidden until the provider confirms payment; forged
  reference, unconfirmed and failed payments leave it unsubmitted; submitted application locked.
- **Tender service**: new tender gets the signed-in user's organisation; no organisation, no tender; another
  organisation's tender cannot be published, edited or cancelled; reference unique per organisation (the same
  reference is allowed in another organisation); closing date at least 1 hour away; at least one document;
  expired draft cannot be published; published tender locked; cancelling keeps the record and the reason.

### Bug found and fixed
- First run: 71 passed, 1 failed. A staff user with no organisation could see audit entries that have no
  organisation (EF treats NULL == NULL as true). Filter fixed (DECISIONS D40); 72 of 72 pass.

### How to run
- Command line: `dotnet test` in the repository folder. Visual Studio: Test > Run All Tests (Test Explorer).

### Commit message
```
test: automated tests for tenant isolation, eligibility, applications, uploads and payments
```

---

## Step 9: Demo script and final polish

### What was built
- **README** rewritten: first-time setup (user-secrets for the demo password, F5 creates the database), demo accounts,
  a 10-minute demo script across two browser windows (organisation publishes, supplier registers and applies,
  organisation reviews, supplier sees the outcome, safety rules), how each requirement is met, mocks and known limits.
- `docs/demo-files/`: `sample-document.pdf` (valid) and `not-really-a-pdf.pdf` (text renamed to .pdf, rejected).
  `.gitattributes` marks PDFs as binary so Git never changes their line endings.
- Security headers on every response (DECISIONS D41).
- Friendly status pages: `/Home/Status/{code}` for empty 404/400/other responses, keeping the status code.

### Verified
- 72 of 72 automated tests pass after the changes; build has 0 warnings.
- Separate Production build on https://localhost:5250: every response has the CSP, X-Frame-Options, nosniff,
  Referrer-Policy and Permissions-Policy headers. Splash (with its photo), sign-in, register and privacy pages
  render normally with the IBM Plex font and all scripts; the browser reports no CSP violations.
- Signed-out visitors who open an unknown URL are sent to Sign in (every page requires sign-in by default, so the
  site's structure is not revealed). The "Page not found" page is for signed-in users; it is checked in the
  Visual Studio run because the sandbox cannot reach LocalDB.
- Visual Studio run, signed in: `/Supplier/Applications/Details/99999`, another supplier's application and an
  unknown URL all return 404 with "Page not found"; a form posted without its security token returns 400 with
  "This form has expired". Every supplier page (feed, tender, applications, upload step, profile, company) and
  every admin page (portal login, dashboard, register, create, tender, application, audit) loads with no CSP
  violations; RBIDZ brand colours (inline style) and the phone menu drawer (site.js) still work.
- Found while checking: the audit trail squeezed its Details column to "E..." between 992 and 1279 px, and hid it
  completely on phones (the generic card layout hides the 4th column). Fixed with audit-specific rules: the IP column
  is dropped at mid widths, and on phones each entry is a card with the details in full.

### Manual tests
1. Signed in, open a URL that does not exist, e.g. `/Supplier/Applications/Details/99999`: "Page not found" in the app's style.
2. Browser developer tools > Network > any page > Response headers: Content-Security-Policy, X-Frame-Options, etc.
3. Click through the demo script in the README; the browser console shows no "Content Security Policy" errors.

### Commit message
```
docs: demo script and setup guide; feat: security headers and friendly error pages
```

---

## Step 10: Button contrast (WCAG AA)

### What was built
- `BrandColours.ReadableWithWhiteText` / `ContrastWithWhite` (WCAG 2 formula); the admin and portal layouts write
  `--org-accent-strong` next to `--org-accent`.
- `--ep-accent-strong` (#157CB3) in the stylesheet, used only where white text sits on the brand colour (DECISIONS D42).
- 10 new tests (`Rules/BrandColoursTests`): exact shades for the two real brand colours, passing colours unchanged,
  any colour (even yellow or white) ends up readable, invalid values cannot inject CSS. 82 of 82 pass.

### Verified in the browser (Visual Studio run)
- RBIDZ application page: "Save status" and other filled buttons are rgb(21, 124, 179) = #157CB3; the rest of the
  design is unchanged. MVLM's darker coral appears after the app is restarted (layout change).

### Manual tests
1. Sign in at `/admin/mvlm`: the sign-in button and primary buttons are a slightly deeper coral, still clearly MVLM.
2. As a supplier, the Apply / Continue buttons and selected chips are a slightly deeper blue; lines and dots stay bright.

### Commit message
```
fix: readable white text on buttons (WCAG AA) with a darker shade of each brand colour
```

---

## Step 11: Link contrast (WCAG AA)

### What was built
- Links, text buttons ("Register"), the outline button, the "closes in" label, the active phone tab and the SBD form
  labels use `--ep-accent-strong` as their text colour (the "e" of the logo and decorative icons stay bright).
- A page-wide contrast scan found one link still short: "privacy notice" on the Register page, which sits on the
  light grey consent box (4.39:1). The shade is now calculated against the darkest light surface text sits on
  (`#E3F1FB`) instead of white: `BrandColours.ReadableShade`. New values: blue `#1472A5`, MVLM coral `#BB4726`.
- Tests updated: the shade is checked against white, both greys and the pale-blue surface. 84 of 84 pass.

### Verified in the browser (Visual Studio run)
- Contrast scan of every text element on: admin dashboard, tender register, create tender, tender details,
  application details, audit trail; supplier feed, tender page, applications, application details, declarations,
  profile; sign-in, register; MVLM portal sign-in. No link, button or accent-coloured text is below 4.5:1.
- MVLM portal: `--org-accent-strong` written by the layout, sign-in button in MVLM's own darker coral.
- Remaining below 4.5:1: the design's light grey secondary text `#8C9CAB` (column headings, field hints, fact
  labels, 2.7-2.8:1) and the logo's "e" (logos are exempt). Not changed; see DECISIONS D42.

### Commit message
```
fix: accent shade readable on light grey surfaces too; contrast verified on every page
```

---

## Step 12: Sealed bids, BEC evaluation and BAC award

### What was built
- **Sealed bids** (D43): before the closing date staff see only "Sealed bid · opens (date)"; company, answers and
  documents are hidden and downloads are refused on the server (audited as `Document.SealedRefused`).
- **`EvaluationRules`**: PPPFA 2022 price points (80/20, 90/10), B-BBEE preference points table, ranking with the
  preference-points tie-break and shared ranks for true ties. Pure functions.
- **`EvaluationService`** + **`EvaluationController`** (D44): the BEC captures responsiveness and price per bid; the
  scoresheet calculates and ranks; the BEC submits a recommendation (reasons needed if not ranked first or tied), which
  freezes the points; the SCM Officer records the BAC decision (minute reference, date, reasons; full reasons when
  deviating) or returns the evaluation to the BEC. On award: `AwardRecord`, tender Awarded, every bidder's status and
  a note with their outcome (reason, or points and rank).
- New table `BidEvaluations`; four nullable columns on `Tenders` (migration `AddEvaluationAndAward`, script in
  `docs/schema`).
- Screens: sidebar **BEC scoring** and **BAC adjudication** (now live), scoresheet, bid evaluation page; application
  page shows the evaluation instead of the old status form (D45); admin tender page links to the scoresheet; the
  dashboard's action items point the BEC to closed tenders and the SCM Officer to decisions awaiting the BAC.
- Supplier side: public **award notice** on the tender page; "Awarded to your company" / "Not awarded" cards on the
  application page.
- Demo data: `supplier3@demo.co.za` (Siyakha, Level 2) and the closed tender **RBIDZ/2026/011** with three bids whose
  pricing-schedule PDFs state the prices.
- Tests: 17 rules tests and 16 service tests (113 in total, all passing).

### Verified in the browser (Visual Studio run)
- RBIDZ admin: RBIDZ/2026/014's two bids listed as "Sealed bid · opens 04 Oct 2026"; the application page shows only
  "Sealed until 04 Oct 2026, 20:13"; a document download is refused and audited.
- SCM Officer on the bid page: no evaluation form; forcing the request → Access denied. SCM forcing a recommendation →
  Access denied.
- BEC member: pricing schedule PDF downloads and states the price; "Responsive" without a price → "Enter the total bid
  price…"; three bids captured; scoresheet: Siyakha 73.74 + 18.00 = **91.74** (rank 1), Khanya 80.00 + 6.00 = 86.00,
  Umhlathi 64.00 + 20.00 = 84.00. Recommending Khanya without reasons → refused. Recommendation form pre-selects rank 1;
  submitted; scoresheet locked (a later change → "submitted to the BAC and is locked"); evaluator forcing an award →
  Access denied.
- SCM Officer: return with a 2-character note → refused; returned with a note, the BEC sees "Returned by the BAC: …",
  resubmits. Award with no reference, a future date and a short reason for a non-recommended bid → three errors.
  Award through the form (recommended bid and today's date pre-filled) → "The BAC decision has been recorded".
- Audit trail: Evaluation.Captured ×3, Evaluation.Submitted, Evaluation.Returned, Evaluation.Submitted, Tender.Awarded,
  Document.Downloaded, Document.SealedRefused.
- MVLM: scoresheet, bid page and document of RBIDZ → 404; its evaluation list is empty.
- Suppliers: Siyakha "Awarded to your company … R1 240 000.00"; Khanya "Not awarded … scored 86.00 points and ranked 2
  of 3"; Umhlathi "… 84.00 … ranked 3 of 3"; the tender page shows the award notice (R1 240 000.00, 91.74 points,
  Level 2).
- Fixed during testing: the scoresheet was squeezed beside the side column (it now spans the full width, with the
  formula and actions below); the winner's note said "BAC BAC 2026/41" (now "decision reference …").

### Commit message
```
feat: sealed bids, BEC evaluation with PPPFA points, and BAC award
```

### Layout check after restart (Step 12)
- Scoresheet at 1280 px: full-width table, formula and committee cards below. At 1100 px the split-points columns are
  dropped (rank, bidder, price, total, evaluation remain).
- Phone (375 px): the scoresheet overlapped price and total; now each bid is a card (rank; bidder and total; price and
  evaluation badge). The evaluation list overlapped its badge and count; now reference and stage, title, progress.
  Long console titles now end with "…" instead of pushing the role badge off screen.
- The award notice was at the very bottom of the supplier tender page on phones; it now sits under the title.
- The formula list is stacked (label above text) instead of squeezed into a right-aligned column.

---

## Step 13: Password reset, email notifications, staff management, bid withdrawal

### What was built
- `IEmailSender` + `MockEmailSender` + Development-only demo mailbox (`/dev/mailbox`); `NotificationService`;
  `LinkBuilder` (D46).
- Forgot password / reset password / accept invitation pages; "Forgot your password?" on both sign-in pages (D47).
- **Roles and users** (sidebar, SCM Officers only): invite, change role, deactivate/reactivate, resend invitation (D48).
- Supplier **withdraw** (collapsed panel with confirmation) and **reopen and resubmit** (D49); withdrawn bids excluded
  from evaluation and staff counts.
- Tests: 24 new (137 in total), including real ASP.NET Core Identity on SQLite for staff and tokens.

### Verified in the browser (Visual Studio run)
- Forgot password for an existing and a made-up address: identical "Check your email" page; one email in the mailbox.
  Reset link: mismatched passwords refused; new password saved; reusing the link → "not valid or has expired";
  sign-in works; "Your eProcure password was changed" email.
- Withdrawal (test supplier, RBIDZ/2026/015, EP-2026-000009): without the tick → refused; with tick and reason →
  Withdrawn, timeline "Withdrawn by the bidder: Correcting the pricing schedule", email; "Reopen and resubmit" → step 1;
  resubmitted → Submitted with the same reference EP-2026-000009; emails for each step.
- Roles and users (RBIDZ SCM Officer): bad input → three messages; an existing supplier address → refused; invite →
  email; invitation page "Welcome, Sipho Test"; password set; link reuse refused. Role changed and back; deactivated →
  sign-in "Invalid email or password"; reactivated → sign-in works; as a BEC member the page is Access denied and the
  sidebar has no link. MVLM: list shows only its own admin; deactivating or changing RBIDZ's evaluator → 404.
- Fixed while testing: the staff list was squeezed next to the invite panel (names invisible); the list now uses the
  full width with the invite form below it (shows after a restart).

### Commit message
```
feat: password reset, email notifications, staff management and bid withdrawal
```
