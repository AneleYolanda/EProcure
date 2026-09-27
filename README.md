# eProcure

Multi-tenant digital procurement platform for South African public-sector tenders
(TISP 2.0 / RECADI programme). One application serves many buying organisations, each with its own
branded admin workspace; suppliers use one shared marketplace.

**Stack:** ASP.NET Core MVC (.NET 8) · EF Core 8 · SQL Server LocalDB · ASP.NET Core Identity · Razor.
Runs fully offline on a laptop.

## Run locally

```
dotnet tool restore
dotnet ef database update --project src/EProcure.Web
dotnet run --project src/EProcure.Web
```

Requires Visual Studio 2026 (ASP.NET workload, includes LocalDB) and the .NET 8 ASP.NET Core Runtime.

## Repository layout

```
src/EProcure.Web/
  Domain/          entities and enums (no framework logic)
  Data/            DbContext, entity configurations, seed data, migrations
  Tenancy/         ITenantContext: "which organisation is this request for?"
docs/
  DATA_MODEL.md    schema, relationships, tenant isolation, POPIA register
  schema/          generated SQL of the migrations, for review
DECISIONS.md       architecture decisions (what / why / alternatives)
BUILD_LOG.md       step-by-step log with manual tests and commit messages
```
