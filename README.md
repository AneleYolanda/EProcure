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
| RBIDZ BEC member (Evaluator: evaluates bids, cannot change tenders) | `evaluator@rbidz.demo` | `/admin/rbidz` |
| MVLM SCM Officer (fictional municipality) | `admin@mvlm.demo` | `/admin/mvlm` |
| Supplier, Umhlathi Civils, B-BBEE Level 1 | `supplier1@demo.co.za` | `/` |
| Supplier, Khanya Office Supplies, B-BBEE Level 6 | `supplier2@demo.co.za` | `/` |
| Supplier, Siyakha Business Solutions, B-BBEE Level 2 | `supplier3@demo.co.za` | `/` |

**Evaluation demo tender:** `RBIDZ/2026/011` (printing and document management) closed three days before the database
was created and already has three bids, one from each demo supplier, so evaluation and award can be shown at once.

**Fresh demo:** to start again from clean demo data, stop the app, delete the `EProcure` database (Visual Studio:
**View > SQL Server Object Explorer > (localdb)\MSSQLLocalDB > Databases > EProcure > Delete**, tick "Close existing
connections"), then press F5. The database and demo data are recreated.

Sample files for uploading are in `docs/demo-files/`: `sample-document.pdf` (a valid PDF) and
`not-really-a-pdf.pdf` (a text file with a .pdf name, which eProcure rejects).

---

## 2. Demo script (about 15 minutes)

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

**C. The organisation receives it, sealed (window 1)**
12. **Applications**: the bid is listed only now that it is paid, as **Sealed bid · opens (closing date)**. Open it:
    only the reference and time are shown. Bids stay sealed until the closing date, so nobody inside can see a
    competitor's price early.

**D. Evaluation by the BEC (window 1, the closed demo tender)**
13. Sign in at `/admin/rbidz` as `evaluator@rbidz.demo`. Sidebar: **BEC scoring**, open **RBIDZ/2026/011**.
14. Open each bid, **Download** its *Pricing schedule* PDF and read the price. Mark it **Responsive** and type the
    price: Siyakha **1240000**, Khanya **1150000**, Umhlathi **1380000**. (Try saving "Responsive" without a price:
    refused. Khanya has a declared-interest flag for the committee to consider.)
15. The scoresheet calculates the points (80/20): **Siyakha 91.74** (73.74 + 18) ranks first, ahead of the cheapest
    bid, Khanya (86.00 = 80 + 6), and Umhlathi (84.00 = 64 + 20). Nobody types points in.
16. **Submit to the BAC** with Siyakha selected (try Khanya: reasons are required because it is not ranked first).
    The scoresheet is now locked.

**E. The BAC decision (window 1)**
17. Sign in as `admin@rbidz.demo`. Sidebar: **BAC adjudication**, open the tender. (Optionally **Return to the BEC**
    with a note, and resubmit as the evaluator.)
18. **Record the BAC decision**: the recommended bid is pre-selected; enter a minute reference such as `BAC 2026/41`,
    today's date and the reasons, then **Record award**. (Choosing another bid needs the BAC's full reasons and is
    flagged in the audit trail.)
19. **Audit trail**: every evaluation, the submission to the BAC and the award, with names and times.

**F. The bidders see the outcome (window 2)**
20. Sign in as `supplier3@demo.co.za`: **Awarded to your company**, with the amount. As `supplier2@demo.co.za`:
    **Not awarded**, "your bid scored 86.00 points and ranked 2 of 3". Staff names are never shown to bidders.
21. The tender page shows the public **award notice**: winner, contract value, points, B-BBEE level and date.

**G. The safety rules**
22. As `supplier2@demo.co.za` (Level 6) open **RBIDZ/2026/014** (minimum Level 4): *Not eligible*. Pressing Apply
    shows the **hard stop**; nothing is created.
23. Sign in at `/admin/mvlm` as `admin@mvlm.demo` (teal/coral branding). MVLM sees only its own tenders. Paste an RBIDZ
    scoresheet or application URL: **Page not found**.
24. Separation of duties: the evaluator cannot record an award or change tenders; the SCM Officer cannot evaluate.

**H. Accounts, people and withdrawals**
25. **Forgot your password?** on the sign-in page: enter `supplier2@demo.co.za`. The page says "Check your email" for any
    address. Open the **demo mailbox** at `/dev/mailbox`, press **Choose a new password**, set one, sign in. Using the same
    link again is refused.
26. As `admin@rbidz.demo`: sidebar **Roles and users**. Invite a BEC member (any name, an address such as
    `bec2@rbidz.test`); the invitation appears in the demo mailbox; open it in a private window and choose a password.
    Change their role, **Deactivate** them (they can no longer sign in), **Reactivate** them. Your own row has no
    buttons, and the organisation always keeps at least one active SCM Officer.
27. As a supplier with a submitted bid on an open tender: open the application, **Withdraw this bid**, tick the
    confirmation. It is Withdrawn and will not be evaluated. **Reopen and resubmit**: tick the declaration again; it is
    submitted with the same reference and no second tender fee.

---

## 3. Run the tests

```
dotnet test
```

No database server needed: each test builds the real EF Core model (same query filters and unique indexes) on an
in-memory SQLite database. In Visual Studio: **Test > Run All Tests**. 137 tests cover tenant isolation, the B-BBEE
hard stop, one application per company, closing dates, PDF-only uploads, declarations, payment verification, the
PPPFA points arithmetic, sealed bids, the BEC and BAC workflow, bid withdrawal, staff management (with real
ASP.NET Core Identity), password and invitation links, and the emails.

---

## 4. How the requirements are met

| Requirement | Where |
| --- | --- |
| Multi-tenant, isolation enforced on the server | EF Core global query filters from the signed-in user's organisation claim (`Data/EProcureDbContext.cs`); another organisation's id is "not found". D5, D36, D40 |
| Roles | SupplierOnly / OrgStaff / OrgAdminOnly / BecOnly policies; everything else requires sign-in by default. BEC members evaluate, the SCM Officer records the BAC decision (separation of duties) |
| B-BBEE hard stop | `Services/EligibilityRules.cs`, re-checked on every step of an application. D35 |
| No duplicates | One application per company per tender (unique index + reopen existing); tender reference unique per organisation |
| Closing date | Nothing can be started, changed, submitted or paid after the closing date |
| PDF only, size limit | Extension + content type + `%PDF-` signature, 5 MB, SHA-256 stored, files outside `wwwroot`. D33 |
| Payments | Verified with the provider, never trusted from the browser; no card data stored. D34 |
| Audit trail | `AuditLog` table: registrations, failed sign-ins, phone verification, tender changes, submissions, views, downloads, refused sealed downloads, evaluations, recommendations, returns and awards |
| POPIA | Consent at registration, minimum data, staff names hidden from bidders, register in `docs/DATA_MODEL.md` |
| Digitise, do not automate decisions | SBD answers are flagged, not judged; the BEC decides responsiveness, the system only does the PPPFA arithmetic, the BAC decides the award with recorded reasons. D35, D44, D45 |
| Sealed bids and evaluation | Bids sealed until closing; PPPFA 2022 price and preference points; BEC recommendation; BAC award; outcome and reasons to every bidder; public award notice. D43, D44 |
| No secrets in code | Demo password in user-secrets; LocalDB uses Windows sign-in; external services chosen in `appsettings.json` |
| Security headers | CSP (same-site only, no inline scripts), no framing, nosniff, strict referrer. D41 |
| Accounts and people | Password reset with single-use expiring links; SCM Officers invite, re-role and deactivate their own staff; nobody can lock the organisation out. D47, D48 |
| Email | Confirmations, outcomes, withdrawal, "BAC decision needed", invitations and password changes; an email is a copy, never the record. D46 |
| Bid withdrawal | Before closing, with resubmission and no second fee; withdrawn bids are never evaluated. D49 |

## 5. Mocked services and known limits

- **SMS (OTP)**, **email**, **payment gateway** and **file storage** are mocks behind `IOtpSender`, `IEmailSender`,
  `IPaymentGateway` and `IFileStorage`. Emails can be read in the Development-only demo mailbox (`/dev/mailbox`).
  Moving to a real provider (e.g. SMSPortal, SendGrid, PayFast, Azure Blob) is a new class plus a setting
  under `ExternalServices`. The demo gateway keeps payments in memory, so an unpaid payment is forgotten when the
  app restarts (start the payment again).
- Shown as "Soon" in the console: approval queue. Not built yet: closing-date reminder emails (need a scheduled job).
  Evaluation simplifications (one consolidated
  BEC evaluation per bid, no functionality stage, B-BBEE level as the only specific goal) are listed in DECISIONS D44.
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
