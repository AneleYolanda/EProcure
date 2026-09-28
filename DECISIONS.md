# eProcure: Architecture Decision Log

Each entry: **What** was decided, **Why**, **Alternatives considered**, and **why not**.
Newest decisions are appended at the bottom. Numbering is permanent (never renumber).

---

## D1. ASP.NET Core MVC (.NET 8), not ASP.NET Framework MVC or Node.js

**What:** The web application is ASP.NET Core MVC targeting .NET 8 (LTS), written in C#.

**Why:**
- .NET 8 is a Long-Term Support release, cross-platform, and the version fixed by the programme brief.
  **Risk flagged:** Microsoft support for .NET 8 ends on **10 November 2026**, about six weeks after this
  project started. Upgrading to .NET 10 (LTS) is a one-line `<TargetFramework>` change plus package
  bumps, and should be planned before any production use.
- ASP.NET Core Identity gives vetted password hashing, lockout and role management out of the box.
  We do not write our own security code.
- Strong typing in C# catches whole classes of errors (e.g. a misspelt role name) at compile time.
- Visual Studio gives an integrated debugger, EF Core migrations tooling and a familiar experience for
  a public-sector reviewer.

**Alternatives considered:**
- *ASP.NET (Framework) MVC 5*: Windows/IIS only and in maintenance mode (no new features). Starting a new
  government system on a legacy framework creates avoidable technical debt.
- *Node.js (Express/NestJS)*: viable, but authentication, ORM and validation must be assembled from many
  third-party packages. That means more supply-chain risk and more code to justify line by line.

**Note (environment):** Visual Studio 2026 ships the .NET 10 SDK, whose templates only offer `net10.0`.
The project was generated with the template and then retargeted to `net8.0` by editing `<TargetFramework>`.
The .NET 10 SDK compiles `net8.0` fine; *running* the app requires the **.NET 8 ASP.NET Core Runtime**
to be installed (free, VS Installer → Individual components → ".NET 8.0 Runtime").

---

## D2. Entity Framework Core, not Dapper or raw SQL

**What:** Data access uses EF Core 8 with code-first migrations.

**Why:**
- **Global query filters** let us enforce tenant isolation in *one* place (the DbContext), so every
  query is automatically restricted to the user's organisation. With Dapper/raw SQL every single query
  would need a hand-written `WHERE OrganisationId = @org`; one forgotten clause leaks another
  organisation's data. This is the deciding reason.
- Migrations give a versioned, reviewable history of every schema change (also exportable as SQL,
  see `docs/schema/InitialCreate.sql`).
- Parameterised SQL by default, which protects against SQL injection.
- ASP.NET Core Identity's stores are built on EF Core.

**Alternatives considered:**
- *Dapper*: faster for heavy reporting, and we may add it later for read-only reports. As the primary
  data layer it loses global filters and migrations.
- *Raw ADO.NET SQL*: maximum control, but the most code to write, review and keep injection-safe.

---

## D3. Server-rendered Razor views, not a JavaScript SPA (React/Angular)

**What:** Pages are rendered on the server with Razor, with minimal JavaScript for enhancements.

**Why:**
- **One codebase, one language, one deployment.** A SPA needs a separate front-end project, an API
  layer, token authentication (JWT) and CORS, which is a lot of extra surface for a solo developer.
- Works on low-end phones and poor connections (common for SMME suppliers): the browser receives
  finished HTML, not a large JavaScript bundle.
- Security is simpler: cookie authentication + built-in anti-forgery tokens on every form.
- Every permission check happens on the server, where users cannot tamper with it.

**Alternatives considered:**
- *React/Angular SPA + Web API*: richer interactivity, but doubles the codebase and the attack
  surface. Can be added later for specific screens if needed.
- *Blazor*: modern, but Blazor Server needs a persistent connection (poor on mobile data) and
  Blazor WebAssembly has a large initial download.

---

## D4. SQL Server (LocalDB / Express), not PostgreSQL

**What:** SQL Server: LocalDB for development, SQL Server Express (free) for a first deployment.

**Why:**
- South African government IT (and the TISP environment) predominantly runs Microsoft SQL Server;
  handover to an organisation's IT team is easier.
- LocalDB is installed with Visual Studio, needs no configuration and runs fully offline.
- First-class EF Core provider and tooling (SSMS, VS SQL Server Object Explorer).
- Express is free up to 10 GB per database, far beyond MVP needs (PDFs are stored outside the DB, see D8).

**Alternatives considered:**
- *PostgreSQL*: excellent and free, arguably more capable, but adds a separate install and is less
  common in the target organisations' environments. EF Core makes a future switch possible.
- *SQLite*: great for prototypes, but weak concurrency and a different SQL dialect from production.

---

## D5. Multi-tenancy: single shared database with an `OrganisationId` column

**What:** All organisations share one database and one set of tables. Tenant-owned rows carry an
`OrganisationId` (directly on `Tenders` and admin `Users`; indirectly, through the tender, on
submissions, documents, history and awards). EF Core **global query filters** add
`WHERE OrganisationId = <current user's org>` to every query made by an OrgAdmin or Evaluator.

**Why:**
- The product *requires* cross-tenant reads: a supplier browses tenders from **all** organisations and
  applies to any of them with one company profile. In a shared database that is a normal query. With a
  database per tenant it would mean querying N databases and merging the results.
- One schema, one migration, one backup: realistic for a solo developer.
- Onboarding a new organisation is inserting one `Organisations` row, not provisioning a database.
- The organisation is taken from the user's **server-signed authentication cookie**, never from the URL
  or a form field, so a user cannot "ask for" another organisation's data.
- **Fail closed:** if an OrgAdmin somehow has no organisation claim, the filter compares against NULL
  and returns *nothing*, not everything.

**Alternatives considered:**
- *Database per tenant*: strongest isolation and per-tenant backup/restore, but makes the supplier
  marketplace (cross-tenant search) hard, multiplies migrations and costs. Worth revisiting only if
  an organisation contractually requires physical separation.
- *Schema per tenant* (one DB, `rbidz.Tenders`, `mvlm.Tenders`): same cross-tenant problems as
  database-per-tenant, and EF Core support is awkward.

**Risks and mitigations:**
- *A bug bypasses the filter* (e.g. someone calls `IgnoreQueryFilters()`). Mitigation: rule that
  `IgnoreQueryFilters` is never used in admin code paths; tenant-isolation manual tests in BUILD_LOG;
  automated tests to be added.
- *Noisy neighbour*: one busy organisation slows others. This is negligible at MVP scale.

---

## D6. Local development on LocalDB now; no cloud hosting yet

**What:** Everything runs on the developer's laptop, offline, at zero cost. No Azure/AWS resources.

**Why:**
- Zero cost and no dependency on internet connectivity or load-shedding at a data centre link.
- No real personal information leaves the laptop during development (POPIA risk minimised).
- External services are behind interfaces (`IFileStorage`, `IPaymentGateway`, `IOtpSender`) with mock
  implementations, so moving to hosted services later is a configuration and implementation change,
  not a rewrite.

**Alternatives considered:**
- *Azure App Service + Azure SQL now*: realistic production target, but costs money, needs a
  subscription and a POPIA assessment of cross-border data (region choice) before any real data.
  Deferred until the MVP is approved.

---

## D7. Separate `SupplierProfile` (person) and `Company` (legal entity)

**What:** A supplier login has one `SupplierProfile`, which points to one `Company`. Submissions belong to
the **Company**, and also record which user submitted them.

**Why:** The bidder in law is the company (CIPC registration, CSD number, tax PIN, B-BBEE certificate),
not the person clicking the button. Keeping them apart lets a company have several staff users later
without duplicating company data, and keeps personal information (POPIA) separate from company
information.

**Alternative:** One combined table. It is simpler, but mixes personal and juristic data and blocks multi-user companies.

---

## D8. Uploaded PDFs stored outside the database

**What:** `UploadedDocument` stores metadata plus a server-generated `StorageKey` and SHA-256 hash; the
bytes go to `IFileStorage` (local folder now).

**Why:** Keeps the database small (within SQL Server Express's 10 GB limit), makes backups fast, and lets
us move files to blob storage later. The original file name is never used as a disk path, which
prevents path-traversal attacks. The hash proves a document was not altered after submission.

---

## D9. Enums stored as text; money as `decimal(18,2)`

**What:** Every enum column is `nvarchar(32)` holding the name (`"Published"`); money is `decimal(18,2)`.

**Why:** A reviewer or auditor reading the database directly sees meaningful values. `decimal` avoids
floating-point rounding errors on Rand amounts.

**Consequence:** "Order" comparisons on enums (e.g. B-BBEE level ≤ required level) must be done in C#,
not in SQL, because text sorts alphabetically. The query filters therefore list excluded statuses
explicitly instead of using `<`.

---

## D10. Restrict deletes almost everywhere (records retention)

**What:** Foreign keys use `ON DELETE NO ACTION` (Restrict), except Tender → TenderRequirement (Cascade).

**Why:** Procurement records must be retained for audit (PFMA/MFMA, Auditor-General). The database
refuses to delete a tender that has submissions, a company that has bids, and so on. Records are
closed or cancelled via status, not deleted. Also avoids SQL Server's "multiple cascade paths" error.

---

## D11. Identity entity names: `ApplicationUser` / `ApplicationRole` in C#, `Users` / `Roles` in SQL

**What:** The brief's "User/Role" entities are C# classes `ApplicationUser : IdentityUser` and
`ApplicationRole : IdentityRole`, mapped to tables named exactly **`Users`** and **`Roles`**
(Identity's default `AspNet*` prefixes removed).

**Why:** Every MVC controller already has a property named `User` (the signed-in principal). A class also
named `User` makes controller code ambiguous and error-prone. The *database* uses the brief's exact names.

---

## D12. Audit log as its own table with no foreign keys

**What:** `AuditLog` (entity `AuditEntry`) records who/what/when/which-organisation for key actions.
It was added beyond the brief's entity list because the compliance section requires an audit trail.

**Why no foreign keys:** An audit row must outlive the record it describes. The user's email is copied
into the row for the same reason.

---

## D13. Demo users are not seeded in migrations

**What:** Organisations and roles are seeded with `HasData` (in the migration). Demo OrgAdmin and
supplier *users* will be created at application start-up in step 2, with passwords read from
**user-secrets**.

**Why:** Passwords (even demo ones) must not be committed to source control or baked into migration
files.

---

## D14. B-BBEE "hard stop" modelled as a prequalification criterion

**What:** `Tender.MinimumBbbeeLevel` is the worst level still allowed to apply (NULL = open to all).

**Why:** The Preferential Procurement Regulations, 2022 allow an organ of state to advertise a tender
with specific prequalification criteria (such as a stipulated minimum B-BBEE status level), provided
this is stated in the tender documents. Blocking ineligible bidders up front, with a clear reason,
treats all bidders equally and transparently (Constitution s217). The rule is applied identically to
everyone, with no override.

---

## D15. Custom AccountController instead of scaffolded Identity UI

**What:** Account pages use a small MVC `AccountController` and project Razor views rather than the
scaffolded Identity UI.

**Why:** The application needs a supplier-only registration flow, POPIA consent capture, generic login
errors, role-based redirects and the Modernist visual language. Keeping these actions in the application
makes every behaviour visible and explainable to the technical reviewer.

**Alternatives considered:** Scaffolded Identity UI would provide standard pages quickly.

**Why not:** It adds a separate UI area and makes the required supplier-only workflow and branding less
direct to review.

---

## D16. Areas separate supplier and organisation staff workflows

**What:** Supplier and organisation staff dashboards live in `Areas/Supplier` and `Areas/Admin`, with
explicit policies on their controllers.

**Why:** The two roles have different data boundaries, navigation and visual priorities. Area routing makes
the boundary visible in URLs and in the code review.

**Alternatives considered:** One controller with role checks in every action.

**Why not:** Repeated checks are easier to omit and would mix tenant administration with supplier activity.

---

## D17. Validate organisation colours before injecting CSS

**What:** The admin layout accepts database branding colours only when they match `^#[0-9A-Fa-f]{6}$`,
otherwise using the RBIDZ navy/gold fallback.

**Why:** Branding is tenant-configurable, but values must not become arbitrary CSS. Validation keeps the
layout safe and guarantees a usable fallback.

**Alternatives considered:** Trusting the database value or adding a colour-picker package.

**Why not:** Trusting values is unsafe, and a package is unnecessary for the fixed six-digit hex format.

---

## D18. Development demo seeding uses user-secrets

**What:** Demo users and sample tenders are created idempotently only in Development. The password is read
from `DemoSeed:Password` in user-secrets.

**Why:** Reviewers need repeatable data, but credentials must never be in code, appsettings or git. The
seeder skips account creation and logs a setup warning when the secret is missing.

**Alternatives considered:** HasData passwords, appsettings credentials, or manual SQL scripts.

**Why not:** Migrations and configuration files are committed and would expose a credential; manual scripts
are not repeatable on application start.

---

## D19. Own design-system stylesheet; Bootstrap CSS removed

**What:** All styling lives in `wwwroot/css/eprocure.css` (tokens and components copied from the Claude Design
prototypes). Bootstrap's CSS and JS are no longer loaded; jQuery stays only for form validation.

**Why:** "Must look exactly like the prototype." Bootstrap's own defaults (typography, form controls, spacing)
kept overriding the design and made pixel matching a fight. One stylesheet, one source of truth, easier to explain.

**Alternatives:** Theme Bootstrap with Sass variables (needs a Sass build step and still leaves Bootstrap's look
underneath); Tailwind (a build step and a new tool to explain). Not worth it for ~15 screens.

---

## D20. Bidder side is a phone app in a centred column; no fake device frame

**What:** On phones the bidder screens fill the screen; on larger screens the same screens sit in a 480px column.
The iOS status-bar space in the prototype (~44px at the top of each screen) is removed.

**Why:** The prototype is an iPhone app and suppliers mostly use phones. Drawing an iPhone bezel on a website would be fake.

---

## D21. Out-of-scope design features shown as "Soon", never as dead buttons

**What:** Menu items from the prototype that the MVP does not have (Approval queue, BEC scoring, BAC adjudication,
Roles and users; Tender register, Applications and Audit trail until their steps) are shown greyed with a "Soon"
pill and are not links. TenderBuddy, "For you", notifications and the company switcher are hidden.

**Why:** Users and the reviewer must never see a button that does nothing, and the app must not suggest features
(such as automated scoring) that it does not have. Showing the full menu keeps the demo recognisable against the design.

---

## D22. Organisation-branded admin entrance: /admin/{orgCode}

**What:** `/admin/rbidz` shows RBIDZ's splash and sign-in in its colours; `/admin/mvlm` shows MVLM's.

**Why / security:** The URL only changes the LOOK of these two pages. After sign-in the tenant still comes from the
signed cookie claim. A user can only sign in at their own organisation's address: if the password is right but the
organisation is wrong, they are signed straight back out and get the same "Invalid email or password." message
(no hint which organisation an email belongs to), and the attempt is audited.
The routes use `Order = 100`, so they rank below `/Admin/{controller}`: `/Admin/Dashboard` can never be mistaken
for an organisation code.

---

## D23. Six-digit OTP via Identity; demo code passed in TempData

**What:** Phone codes come from ASP.NET Identity's phone token provider (6 digits, time-limited, nothing stored in
the database). The prototype shows 5 boxes; we show 6. The mock sender passes the code to the page in TempData.

**Why:** Re-using Identity's provider avoids writing our own OTP security. TempData (an encrypted cookie) replaced
Session, which is not enabled in this app; the earlier code threw an error on every real registration.

---

## D24. B-BBEE eligibility rule as one pure function

**What:** `Services/EligibilityRules.CheckBbbee(companyLevel, tenderMinimum)`, with no database access.
The feed uses it now to label cards "You qualify" / "Not eligible"; Step 6 will call the same function as the
hard stop on every application POST.

**Why:** One rule in one place, applied identically to everyone, and easy to unit-test (Step 8). It runs in C#
because enums are stored as text (D9), so SQL cannot compare levels.

---

## D25. Accessibility risk in the design's colours (flagged, not changed)

**What:** White text on the design's blue `#1CA3EC` has a contrast ratio of about 2.9:1; white on MVLM's coral
`#E4572E` is about 3.4:1. WCAG AA needs 4.5:1 for normal text (3:1 for large text).

**Decision for now:** Keep the exact design colours, as requested. **Recommendation:** before production, darken
the button colour (e.g. `#0E6FA8` instead of `#1CA3EC`) or make button text larger and bolder. Changing one token
in `eprocure.css` fixes it everywhere.

---

## D26. Tender form: all four steps on one page

**What:** The prototype's "New tender" shows one step at a time (Details, Pre-qualification, Evaluation method,
Documents, Review). We keep its look (numbered step list on the left, one card per step) but show all steps on one
page, with the step list as jump links, and save in one POST. The tender page itself is the "Review" step.

**Why:** A multi-request wizard needs somewhere to keep half-finished data between steps (session or partial rows)
and more code to explain. One form is simpler, works without JavaScript, and loses nothing if the admin jumps around.

**Alternative:** A true server-side wizard. Worth it only if the form grows much larger.

---

## D27. Fixed culture: South African English with "." as the decimal separator

**What:** `Program.cs` sets every request to en-ZA, but with "." for decimals, and ignores the browser's language.

**Why:** en-ZA uses a comma for decimals, so on a laptop set to the South Africa region a fee typed as "500.00"
was read incorrectly. HTML number fields always send ".", so the server must read ".". Fixing the culture also
means the app behaves the same on every machine and every browser.

---

## D28. Evaluators (BEC members) are read-only on tenders

**What:** Viewing tenders needs the OrgStaff policy; creating, editing, publishing and cancelling need OrgAdminOnly.
The buttons are hidden for evaluators and the server refuses the requests anyway (checked: direct requests with a
valid anti-forgery token still end on Access denied).

**Why:** Separation of duties: the people who evaluate bids should not also shape the tender they evaluate.

---

## D29. Tender stage is derived, and cancelling never deletes

**What:** The register's stage (Draft, Advertised, Under evaluation, Awarded, Cancelled) is worked out from the stored
status plus the closing date, so a published tender shows "Under evaluation" the moment it closes, with no background
job. Cancelling sets status Cancelled with a required reason (10 to 1000 characters) and time; the row is kept.

**Why:** No scheduled job to maintain, and the register can never be out of date. Keeping cancelled tenders
(with reasons) is required for records retention and audit (D10).

---

## D30. Supplier side is a responsive web app (replaces D20)

**What:** Phones (< 768px) keep the design's phone app with the bottom tab bar. From 768px the same screens become a
normal website: a top navigation bar (wordmark, Tenders / Applications / Profile, the signed-in person, Sign out),
full-width content up to 1200px, and the tender feed as a grid of cards. From 992px, sign in / register / verify are
a split screen: the brand photo panel on the left, the form on the right. Pages pick their layout with
`ViewData["Shell"]` ("app", "auth" or "splash").

**Why:** Feedback on the first version: on a laptop it looked like a phone in the middle of the page. Suppliers use
both phones and office computers. Colours, type, cards and wording are unchanged; only the page structure adapts.

**Detail:** The design's splash photo has "eProcure / Procurement, simplified." printed on its left side. On wide
screens the photo is kept to the right-hand part of the panel and the left fades into navy, so the printed text
never shows under our own wordmark.
