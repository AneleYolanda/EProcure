# eProcure

Multi-tenant digital procurement platform for South African public-sector tenders
(TISP 2.0 / RECADI programme). One application serves many buying organisations, each with its own
branded admin workspace; suppliers use one shared marketplace.

**Stack:** ASP.NET Core MVC (.NET 8) · EF Core 8 · SQL Server LocalDB · ASP.NET Core Identity · Razor.
Runs fully offline on a laptop. No payment, SMS or cloud account needed: those services are mocked behind interfaces.

---

## 1. First-time setup (about 5 minutes)

Requires Visual Studio 2026 with the **ASP.NET and web development** workload (includes SQL Server LocalDB)
and the .NET 8 runtime.

1. Clone the repository and open `EProcure.sln` in Visual Studio.
2. Choose the password for the demo accounts. It is kept in your user-secrets, never in the code.
   In a terminal in the repository folder:
   ```
   dotnet user-secrets set "DemoSeed:Password" "<a password of 10+ characters>" --project src/EProcure.Web
   ```
   (Or in Visual Studio: right-click **EProcure.Web > Manage User Secrets** and add `"DemoSeed:Password": "..."`.)
3. Press **F5** (profile `https`). In Development the app creates/updates the `EProcure` LocalDB database
   and loads the demo organisations, users and tenders by itself. The site opens at `https://localhost:7266`.

The connection string is in `src/EProcure.Web/appsettings.Development.json` (LocalDB, Windows sign-in, no password).

### Demo accounts (password = the one you set above)

| Who | Email | Signs in at |
| --- | --- | --- |
| RBIDZ SCM Officer (OrgAdmin) | `admin@rbidz.demo` | `/admin/rbidz` |
| RBIDZ BEC member (Evaluator, read-only) | `evaluator@rbidz.demo` | `/admin/rbidz` |
| MVLM SCM Officer (fictional municipality) | `admin@mvlm.demo` | `/admin/mvlm` |
| Supplier, Umhlathi Civils, B-BBEE Level 1 | `supplier1@demo.co.za` | `/` |
| Supplier, Khanya Office Supplies, B-BBEE Level 6 | `supplier2@demo.co.za` | `/` |

Sample files for uploading are in `docs/demo-files/`: `sample-document.pdf` (a valid PDF) and
`not-really-a-pdf.pdf` (a text file with a .pdf name, which eProcure rejects).

---

## 2. Demo script (about 10 minutes)

Use two browser windows (for example one normal and one private) so a supplier and an organisation can be
signed in at the same time.

**A. An organisation publishes a tender (window 1)**
1. Open `/admin/rbidz`: the RBIDZ-branded portal. Sign in as `admin@rbidz.demo`.
2. Dashboard: open tenders, applications received, action items. Sidebar: **Tender register**.
3. **New tender**: title, a reference such as `RBIDZ/2026/030`, category, closing date, fee R100,
   minimum **B-BBEE Level 4**, tick three required documents. Save: it is a **Draft** (suppliers cannot see it).
4. Open it: the publish checklist (closing date, documents, pre-qualification). Tick the confirmation and
   **Publish**. It is now locked: every bidder sees the same terms.

**B. A new supplier registers and applies (window 2)**
5. Open `/`, **Register**, fill in the form and accept the POPIA notice.
6. Phone verification: the demo shows the one-time code on screen (no SMS is sent). Enter it.
7. **Company profile**: CIPC number `2021/123456/07`, CSD `MAAA0012345`, tax PIN, **B-BBEE Level 2**.
8. Tender feed: the new tender shows **You qualify**. Open it and **Apply**.
9. Step 2 compliance, step 3 SBD 4/8/9 declarations (try **Yes** without details: it asks for them).
10. Step 4 documents: upload `not-really-a-pdf.pdf` and see it **rejected** (the content is checked, not only the
    name). Upload `sample-document.pdf` for each item.
11. Review: tick the declaration. Pay the fee on the **demo gateway**: first choose **failed** (nothing is
    submitted), then pay again and choose **successful**. Confirmation with reference `EP-2026-…`.

**C. The organisation receives it (window 1)**
12. **Applications**: the bid is listed only now that it is paid. Open it: answers, red flags, the 5 SBD answers
    and documents with their SHA-256 fingerprint. **Download** a PDF.
13. **Record a status change**: *Under evaluation*, with a note. (No "Awarded" button: the system records
    committee decisions, it does not make them.)
14. **Audit trail**: the view, the download and the status change, with name, time and IP address.

**D. The supplier sees the outcome (window 2)**
15. **Applications**: *Under evaluation*. Open it: the note appears on the timeline, from the organisation
    (staff names are not shown to bidders).

**E. The safety rules**
16. Sign in as `supplier2@demo.co.za` (Level 6) and open **RBIDZ/2026/014** (minimum Level 4): *Not eligible*.
    Pressing Apply shows the **hard stop**; nothing is created.
17. Sign in at `/admin/mvlm` as `admin@mvlm.demo` (teal/coral branding). MVLM sees only its own tenders and
    applications. Paste the RBIDZ application URL from step 12: **Page not found**.
18. Sign in as `evaluator@rbidz.demo`: can read and download bids, but has no status form and cannot create or
    publish tenders.
19. `RBIDZ/2026/018` closed two days ago: it is not in the supplier feed, and the RBIDZ register shows it as closed.

---

## 3. Run the tests

```
dotnet test
```

No database server needed: each test builds the real EF Core model (same query filters and unique indexes) on an
in-memory SQLite database. In Visual Studio: **Test > Run All Tests**. 72 tests cover tenant isolation, the B-BBEE
hard stop, one application per company, closing dates, PDF-only uploads, declarations and payment verification.

---

## 4. How the requirements are met

| Requirement | Where |
| --- | --- |
| Multi-tenant, isolation enforced on the server | EF Core global query filters from the signed-in user's organisation claim (`Data/EProcureDbContext.cs`); another organisation's id is "not found". D5, D36, D40 |
| Roles | SupplierOnly / OrgStaff / OrgAdminOnly policies; everything else requires sign-in by default. Evaluators are read-only |
| B-BBEE hard stop | `Services/EligibilityRules.cs`, re-checked on every step of an application. D35 |
| No duplicates | One application per company per tender (unique index + reopen existing); tender reference unique per organisation |
| Closing date | Nothing can be started, changed, submitted or paid after the closing date |
| PDF only, size limit | Extension + content type + `%PDF-` signature, 5 MB, SHA-256 stored, files outside `wwwroot`. D33 |
| Payments | Verified with the provider, never trusted from the browser; no card data stored. D34 |
| Audit trail | `AuditLog` table: registrations, failed sign-ins, phone verification, tender changes, submissions, views, downloads, status changes |
| POPIA | Consent at registration, minimum data, staff names hidden from bidders, register in `docs/DATA_MODEL.md` |
| Digitise, do not automate decisions | SBD answers are recorded and flagged, not judged; no "award" button. D35, D37 |
| No secrets in code | Demo password in user-secrets; LocalDB uses Windows sign-in; external services chosen in `appsettings.json` |
| Security headers | CSP (same-site only, no inline scripts), no framing, nosniff, strict referrer. D41 |

## 5. Mocked services and known limits

- **SMS (OTP)**, **payment gateway** and **file storage** are mocks behind `IOtpSender`, `IPaymentGateway` and
  `IFileStorage`. Moving to a real provider (e.g. SMSPortal, PayFast, Azure Blob) is a new class plus a setting
  under `ExternalServices`. The demo gateway keeps payments in memory, so an unpaid payment is forgotten when the
  app restarts (start the payment again).
- Out of MVP scope and shown as "Soon" in the console: approval queue, BEC scoring, BAC adjudication and awards,
  user management.
- Buttons and links meet WCAG AA contrast: a darker shade of each brand colour is used wherever text and the
  brand colour meet (D42).

## Repository layout

```
src/EProcure.Web/
  Domain/          entities and enums (no framework logic)
  Data/            DbContext, entity configurations, seed data, migrations
  Tenancy/         ITenantContext: "which organisation is this request for?"
  Services/        business rules (tenders, applications, eligibility) and external-service interfaces
  Areas/Admin/     organisation console (SCM Officer, BEC member)
  Areas/Supplier/  supplier web app
tests/EProcure.Tests/
  Rules/           pure rules: B-BBEE eligibility, PDF validator, allowed status changes
  Data/            tenant isolation on a real (SQLite in-memory) database
  Services/        tender and application journeys end to end
docs/
  DATA_MODEL.md    schema, relationships, tenant isolation, POPIA register
  schema/          generated SQL of the migrations, for review
  design/          design specification and prototype source
  demo-files/      sample PDFs for the demo
DECISIONS.md       architecture decisions (what / why / alternatives)
BUILD_LOG.md       step-by-step log with manual tests and commit messages
```
