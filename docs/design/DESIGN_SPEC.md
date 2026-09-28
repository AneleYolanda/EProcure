# eProcure: Design Specification (source of truth for the UI)

The UI must match the Claude Design prototypes **exactly**: same colours, font, sizes, spacing,
radii, shadows, wording and layout. When in doubt, open the source file and copy the values.

| Prototype | Source in this repo | Live reference |
|---|---|---|
| Bidder (supplier) app, iPhone 402 × 874 | `docs/design/bidder-app.source.html` | https://claude.ai/artifact/FJsD9ubBD2rstpeUYStc38 |
| Admin console, desktop 1440 × 900 | `docs/design/admin-console.source.html` | https://claude.ai/artifact/U4A6KQiBRu6NcUbuNiRthC |

How to read the source files: each screen is a block `<sc-if value="{{ sName }}"> … </sc-if>` with
**inline styles** (copy them into CSS classes). `{{ … }}` are data placeholders; `<sc-for list=…>` is a
loop (→ Razor `@foreach`). Sample data and list labels are in the `<script type="text/x-dc">` block at
the end of each file. Ignore the first big `<style>` block ("Modernist" tokens, `h1–h4` Archivo rules):
the screens themselves override it with IBM Plex Sans and inline styles.

## 1. Assets (already copied into wwwroot, all local, no CDN)

| Asset | Path |
|---|---|
| IBM Plex Sans (variable, 400–700), latin + latin-ext | `wwwroot/fonts/ibm-plex-sans-latin.woff2`, `…-latin-ext.woff2` |
| Archivo (variable), latin + latin-ext (only if needed) | `wwwroot/fonts/archivo-latin.woff2`, `…-latin-ext.woff2` |
| Bidder splash photo | `wwwroot/img/brand/splash.jpg` |
| RBIDZ logo | `wwwroot/img/orgs/rbidz-logo.png` |

Fonts are SIL Open Font License, free to bundle. Declare them with `@font-face` in `wwwroot/css/fonts.css`
using the same `unicode-range` values as the source file (latin and latin-ext blocks), `font-display: swap`,
one `@font-face` per weight 400/500/600/700 pointing to the same variable file.

## 2. Design tokens (put in `wwwroot/css/eprocure.css` `:root`)

| Token | Value | Used for |
|---|---|---|
| `--ep-accent` | `#1CA3EC` | primary buttons, links, active tab, "e" of the wordmark, progress, focus ring |
| `--ep-accent-hover` | `#1587C7` | link hover |
| `--ep-accent-soft` | `#7FD0F7` | icons on navy |
| `--ep-ink` | `#1B2A4A` | main text, navy cards (payment amount card, outcome card) |
| `--ep-navy` | `#0F1B33` | admin sidebar, admin splash, admin login left panel, compliance card |
| `--ep-muted` | `#5A6B7B` | secondary text, form labels |
| `--ep-subtle` | `#8C9CAB` | small uppercase captions (CLOSES, TENDER FEE) |
| `--ep-chevron` | `#B9CBDA` | chevrons, dashed borders, bullet dots |
| `--ep-input-border` | `#D6E2EC` | inputs, secondary buttons |
| `--ep-line` | `#E4ECF3` (bidder) / `#E1E9F0` (admin) | card borders, header borders |
| `--ep-line-soft` | `#F0F5F9` / `#EDF2F7` | dividers inside cards |
| `--ep-chip` | `#F2F6FA` | category chips, search box bg |
| `--ep-soft` | `#EEF4F9` | info boxes, secondary pill bg, admin role badge |
| `--ep-page` | `#F7FAFC` (bidder screens) / `#F4F7FA` (admin content) | page background |
| `--ep-desk` | `#EEF4F9` | background outside the phone column on desktop |
| success | text `#0F7B5A`, strong `#0B5F45`, bg `#E6F4EF`/`#F2FAF7`, border `#CFE8DE` | eligible, verified, awarded |
| warning | text `#7A5314`, body `#8A7452`, icon `#B4791C`, bg `#FDF8EF`/`#FDF2DC`, border `#F0DFC0` | not eligible, fee unpaid, awaiting |
| info | text `#1163A0`, bg `#E3F1FB` | advertised / under evaluation badges |
| danger | `#D8483F`; sign-out text `#B3453B` border `#E8CFCC` | notification dot, sign out |

Typography: `font-family: "IBM Plex Sans", system-ui, sans-serif` everywhere. Sizes used (px):
10 / 10.5 captions (uppercase, letter-spacing .07em), 11.5 / 12 / 12.5 small text, 13–13.5 body,
14.5–16 titles, 20–26 page headings (weight 600, letter-spacing −.3 to −.5px), 34 / 52 wordmark.
Form labels: 11.5–12px, weight 500, UPPERCASE, letter-spacing .02–.03em, colour `--ep-muted`.

Shape: bidder inputs & buttons radius **10px**, cards **12px**, chips/badges **6–8px**;
admin cards **10px**, inputs/buttons **8–9px**. Card shadow `0 1px 2px rgba(27,42,74,.04)`.
Primary button shadow `0 6px 16px rgba(28,163,236,.28)`. Input height 48 (bidder) / 44–46 (admin).
Primary button height 50–52 (bidder) / 40–48 (admin), font 16px/600 white on `--ep-accent`.

Wordmark: `<span class="ep-e">e</span>Procure`, "e" in `--ep-accent`, "Procure" in ink (white on navy).
Animations: `epFade` (fade + 8–10px rise) and `epSpin` exactly as in the source `<style>` blocks.

## 3. Components (build once as CSS classes / Razor partials, reuse everywhere)

`ep-btn-primary`, `ep-btn-secondary` (white, `--ep-input-border`), `ep-btn-outline-accent`, `ep-input`,
`ep-label`, `ep-card`, `ep-chip`, `ep-badge` (+ `--success/--warning/--info/--neutral`), `ep-caption`,
`ep-kv-row` (label left / value right with bottom divider), `ep-banner` (+ success/warning),
`ep-choice` (radio-style option card with 1.5px border and dot), `ep-toggle-options` (Yes/No pill buttons),
`ep-timeline` (dot + vertical line), `ep-topbar` (back chevron + title + right meta),
`ep-progress` (3px bar), `ep-tabbar` (bottom navigation), `_TenderCard.cshtml`, `_StatusBadge.cshtml`,
`_EligibilityBanner.cshtml`, admin `ep-sidebar`, `ep-kpi`, `ep-table` (CSS grid rows like the tender register).

## 4. Layouts

**Bidder / public (`Views/Shared/_Layout.cshtml`)**: the design is a phone app. On phones it is full width.
On tablets/desktop render the same app in a centred column `max-width: 480px; min-height: 100vh`
with white/`--ep-page` surface and `--ep-desk` around it. **Do not draw a fake iPhone bezel.**
Signed-in supplier screens get the sticky bottom tab bar from the design (`showChrome` block):
Tenders · Applications · Profile (+ "For you" shown only when that feature exists). Translucent white
`rgba(255,255,255,.94)` + `backdrop-filter: blur(12px)`, top border `--ep-line`, icons 22px, labels 10px/600.

**Admin (`Areas/Admin/Views/Shared/_AdminLayout.cshtml`)**: exactly the `inApp` block: 246px sidebar
(org logo in 38px white circle, org short name + subtitle, nav with 17px line icons, counts pills, user
footer with initials tile, name, role label, sign-out icon), 64px white top bar (page title left, search box
280px, "Audit trail" button, role badge), content area padding 24px 26px 40px on `--ep-page`.
On screens < 992px the sidebar becomes an off-canvas drawer.

**Multi-tenant branding** (keep what Step 2 built): the org's `PrimaryColour` replaces `--ep-navy`
(sidebar, admin splash, admin login panel) and `AccentColour` replaces `--ep-accent` inside the admin
area. RBIDZ must be re-seeded to the design values: Primary `#0F1B33`, Accent `#1CA3EC`,
Logo `/img/orgs/rbidz-logo.png`. MVLM keeps teal `#00695C` / coral `#E4572E` so the demo shows two brands.
The supplier side is always the generic eProcure brand (never org colours).

## 5. Screen → route map

### Bidder app
| Design screen | Route | Build in |
|---|---|---|
| `sSplash` | `/` for anonymous users: auto-continue to login after 2.5 s, "Skip" button | Design phase |
| `sLogin` | `/Account/Login` | Design phase |
| `sRegister` | `/Account/Register` (full name, email, **cellphone**, password, POPIA consent card, "Send OTP to my phone") | Design phase |
| `sOtp` | `/Account/VerifyPhone`: OTP boxes; code via `IOtpSender` (mock shows it on screen) | Design phase |
| `sProfile` | `/Supplier/Profile`: company card(s) + "Add company" | Step 4 |
| `sAddCo` | `/Supplier/Company/Create` and `/Edit` | Step 4 |
| `sFeed` | `/Tenders`: search, "All tenders" tab, filter chips, tender cards with eligibility badge | Step 5 |
| `sDetail` | `/Tenders/Details/{id}`: eligibility banner, key facts grid, scope, criteria, documents, Apply / blocked box | Step 5 |
| `sGate` | shown when eligibility fails at apply time | Step 6 |
| `sApply` steps 1–5 | `/Supplier/Apply/{tenderId}?step=1..5`: Bidding as → Compliance (CSD, tax) → Declarations (SBD 4, SBD 8, SBD 9) → Documents checklist (PDF upload) → Review | Step 6 |
| `sPay` / `sPaying` / `sConfirm` | `/Supplier/Payment/{submissionId}` → mock gateway → confirmation with application reference | Step 6 |
| `sApps` | `/Supplier/Applications` | Step 7 |
| `sAppDetail` | `/Supplier/Applications/{id}`: status badge, state box, "Your audit trail" timeline | Step 7 |
| `sSettings` | `/Supplier/Account`: profile header, rows, Security card, Sign out | Step 7 |

### Admin console
| Design screen | Route | Build in |
|---|---|---|
| `sSplash` | `/admin/{orgCode}` (e.g. `/admin/rbidz`): org-branded splash, auto-continue 2 s | Design phase |
| `sLogin` | `/admin/{orgCode}/login`: split screen, org branding before sign-in | Design phase |
| `inApp` shell + `sDash` | `/Admin/Dashboard`: 4 KPI cards, "Needs your action", pipeline, compliance card | Design phase (real counts where data exists) |
| `sTenders` | `/Admin/Tenders`: filter pills + register table + "Create tender" | Step 3 |
| `sCreate` | `/Admin/Tenders/Create`: left step list + step cards (Details, Pre-qualification, Evaluation method (point system only), Documents, Review) | Step 3 |
| `sPublish` | publish checklist + publish action | Step 3 |
| `sApps` | `/Admin/Submissions` | Step 7 |
| `sAudit` | `/Admin/Audit` | Step 7 |

## 6. Scope rules (MVP) — what NOT to build even though the design shows it

Show these **nav items/tabs greyed out with a small "Soon" pill** (not clickable). Do not build fake screens or dead buttons:
Approval queue, BEC scoring, BAC adjudication, Roles and users, award/outcome notifications, debrief requests,
"For you" feed & industries of interest, TenderBuddy (hide the floating button entirely), company switcher /
multiple companies per profile, company document vault, notifications bell (hide), CIDB grading, functionality
scoring weights, "Forgot password" (render as muted text "Contact support" or hide), 2FA text in admin login
(replace with the true statement: accounts lock after 5 failed attempts).

Role labels: show `OrgAdmin` as **"SCM Officer"** and `Evaluator` as **"BEC member"** in badges and the sidebar footer
(display names only — the security roles stay OrgAdmin/Evaluator).

Wording must be true for our system. Keep the design's copy except where it describes a feature we do not have.
Examples: keep "Card details are never stored by eProcure"; replace "Sessions expire after 15 minutes idle" with our
real cookie setting; keep "These are gates, not preferences. All bidders are held to them equally."

## 7. Data the design needs (additive migration `AddDesignFields`)

- `Tender.EstimatedValue` (`decimal?`) shown as "Est. R8 600 000".
- `Company.EnterpriseSize` (`EME` / `QSE` / `Generic`, text enum) shown as the badge on the company card.
- `Submission.ReferenceNumber` (`string`, unique, format `EP-{yyyy}-{000000}`) shown on the confirmation screen.
- Registration now collects `SupplierProfile.ContactNumber` (SA format `0XX XXX XXXX` / `+27…`) and
  `PhoneNumberConfirmed` is set after OTP verification. Update the POPIA register in `docs/DATA_MODEL.md`.
- Re-seed RBIDZ branding values (section 4).
