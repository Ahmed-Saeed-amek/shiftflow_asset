# ShiftFlow

**ShiftFlow** is an asset-maintenance management system: an ASP.NET Core 8 MVC web application
for tracking a physical asset register and everything that happens to it — inspections, in-house
maintenance, vendor work orders, recurring/preventive schedules, spare parts, contracts and
vendors — with fine-grained role-based access control and a fully bilingual English/Arabic (RTL)
user interface.

## Tech Stack

- **ASP.NET Core 8 MVC** (C#), Razor views
- **EF Core 8** Code First on **SQL Server**; migrations applied automatically at startup
- **ASP.NET Core Identity** for authentication, extended with a custom permission layer
- **Bootstrap 5** + Bootstrap Icons, **Chart.js** for dashboards, **Leaflet** for zone/asset maps
- **EPPlus** (Excel export), **iText7** (PDF export), **QRCoder** / **ZXing.Net** (asset tag QR/barcodes)
- **Azure OpenAI / OpenAI** + **Azure Speech** for the in-app AI assistant
- **Serilog** structured logging

## Solution Layout

The solution is **not** split into separate Domain/Application/Infrastructure class libraries —
those are folders *inside* the single web project:

```
ShiftFlow.sln
├── ShiftFlow.Web/                     # the application (single ASP.NET Core project)
│   ├── Domain/Entities/               # Asset, AssetCategory, Zone, WorkOrder, InspectionOrder,
│   │                                  # MaintenanceOrder, RecurringOrder, SparePart, Contract,
│   │                                  # Vendor, Group, Location, AuditLog, RBAC entities, ...
│   ├── Application/Services/          # domain/business services
│   ├── Application/AI/                # AI assistant pipeline (chat, tools, speech)
│   ├── Authorization/                 # PermissionCatalog, policy handlers, claims transformation
│   ├── Infrastructure/Data/           # ApplicationDbContext, DbSeeder
│   ├── Infrastructure/Migrations/     # EF Core migrations live here
│   ├── Infrastructure/Mvc/            # MVC conventions and filters
│   ├── Controllers/ (+ Controllers/Api/)
│   ├── ViewModels/, Views/, Localization/, wwwroot/
├── ShiftFlow.Web.Tests/               # unit tests
└── ShiftFlow.Mobile/                  # mobile companion app
```

End-to-end browser tests live in `tests/` at the repo root and run under Playwright
(`npx playwright test`, config in `playwright.config.js`).

## Features

- **Assets register** — assets with categories and subcategories, printable/scannable **asset tags**,
  zone placement, and per-asset history of every order raised against it.
- **Zones & location categories** — a location hierarchy (locations, zones, location categories)
  plus a **Zone Overview** with an asset map.
- **Work Orders** — the vendor pipeline. Stages run `Draft → New → Sent to Vendor →
  Fixed - Pending Confirmation → Closed`, with `Rejected` and `Blocked` (block reasons are a managed
  catalog) as side states; every stage change is recorded as a stage event.
- **Inspection Orders** — scheduled/ad-hoc inspections against assets, with inspection runs and reports.
- **Maintenance Orders** — standalone in-house repairs that do not go through a vendor or the work-order pipeline.
- **Recurring Orders** — preventive maintenance: recurrence rules that generate inspection/maintenance
  orders on a schedule.
- **Spare Parts** — inventory with stock status, plus a **Spare Parts Analytics** page.
- **Contracts** — vendor contracts with file attachments and expiry tracking.
- **Vendors & Vendor Portal** — vendor records, and a separate portal login where a vendor sees and
  updates only the work orders assigned to them.
- **Groups** — user groups with their own asset scopes.
- **RBAC** — roles, per-permission grants to roles, per-user permission overrides, and
  **asset scopes** that limit which assets a user or group can see. Administered from the
  Permissions and Asset Visibility pages. Permission names are defined in
  `ShiftFlow.Web/Authorization/PermissionCatalog.cs`; one authorization policy is registered per entry.
- **AI Assistant** — in-app assistant over the app's own data, with optional speech.
- **Audit Log** — records key system actions.
- **Exports** — Excel and PDF exports on the main lists and dashboards.
- **Bilingual UI** — English/Arabic with full RTL layout switching.

## Roles

The seeder (`ShiftFlow.Web/Infrastructure/Data/DbSeeder.cs`) creates these roles, each with an
Arabic display name:

`Admin`, `OperationsManager`, `Engineer`, `HR`, `Supervisor`, `Section Head`, `Senior Engineer`,
`Operation Engineer`, `Technician`, `Vendor`

Roles are only the starting point: actual access is decided by the permission grants attached to a
role, any per-user overrides, and the user's asset scopes.

## Getting Started

Requires the **.NET 8 SDK** and a reachable **SQL Server** instance.

1. Set the connection string. Either edit `ConnectionStrings:DefaultConnection` in
   `ShiftFlow.Web/appsettings.Development.json`, or (preferred — keeps it out of source control)
   use user-secrets:

   ```bash
   cd ShiftFlow.Web
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<host>;Database=<db>;User Id=<user>;Password=<password>;TrustServerCertificate=True;"
   ```

2. Restore, build and run:

   ```bash
   dotnet restore ShiftFlow.sln
   dotnet build ShiftFlow.sln
   cd ShiftFlow.Web
   dotnet run
   ```

3. Migrations are applied automatically on startup, so the database is created and brought up to
   date on first run. Browse to the URL printed in the console.

### Sample data and demo accounts

Sample/demo accounts (and the sample vendors, assets and work orders that go with them) are seeded
**only in the Development environment**, or when the configuration flag `Seed:DemoAccounts` is set
to `true`. They are never seeded in Production by default.

**No demo passwords are published in this repository.** Retrieve them from your team's secret store,
or create your own first administrator manually against a fresh database.

## Configuration Keys

`appsettings.json` holds the shape of the configuration; supply real values through
`appsettings.Development.json` (git-ignored), user-secrets, or environment variables. **Never commit
real secrets.** Placeholder values below:

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string — `Server=<host>;Database=<db>;User Id=<user>;Password=<password>;TrustServerCertificate=True;` |
| `Smtp:Host` / `Port` / `EnableSsl` / `User` / `Password` / `From` | Outbound email for notifications |
| `Twilio:AccountSid` / `AuthToken` / `WhatsAppFrom` | WhatsApp / SMS notifications |
| `OpenAI:ApiKey` / `Model` | OpenAI backend for the AI assistant |
| `AzureOpenAI:Endpoint` / `ApiKey` / `DeploymentName` | Azure OpenAI backend for the AI assistant |
| `AzureSpeech:Key` / `Region` | Speech synthesis/recognition for the AI assistant |
| `YouTube:ApiKey` | YouTube lookups used by the assistant |
| `AzureAd:Instance` / `TenantId` / `ClientId` / `ClientSecret` / `CallbackPath` | Microsoft Entra ID (Azure AD) sign-in |
| `Seed:DemoAccounts` | `true` to seed demo/sample accounts outside Development |

Environment-variable form uses double underscores, e.g.
`ConnectionStrings__DefaultConnection`, `AzureOpenAI__ApiKey`.

## Notes

- Uploaded files (work-order and contract attachments) are written under
  `ShiftFlow.Web/wwwroot/uploads/` at runtime and are git-ignored.
- EF Core migrations live in `ShiftFlow.Web/Infrastructure/Migrations`; add new ones with
  `dotnet ef migrations add <Name> --project ShiftFlow.Web --output-dir Infrastructure/Migrations`.
