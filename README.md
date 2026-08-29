# ShiftFlow

A maintenance and operations management system for electrical utility infrastructure, built for the Ministry of Electricity, Water & Renewable Energy — Kuwait. Tracks work orders, emergency tickets, preventive maintenance, safety permits (PTW), assets, spare parts, shifts, and leave requests across multiple stations, with role-based access and bilingual (EN/AR, RTL) support.

## Tech Stack

- **ASP.NET Core 8 MVC** (C#), Clean Architecture (Domain / Application / Infrastructure / Web)
- **EF Core 8** Code First, SQL Server
- **ASP.NET Core Identity** with role-based authorization (Admin, ShiftManager, Engineer, HR)
- **Bootstrap 5** + Bootstrap Icons, **Chart.js** for dashboards, **Leaflet.js** for asset/work-order maps
- **ClosedXML** (Excel export), **iText7** (PDF export)
- **Serilog** structured logging

## Project Structure

```
ShiftFlow.sln
├── ShiftFlow.Domain/           # Entities (Asset, WorkOrder, Shift, EmergencyTicket, SafetyPermit, ...)
├── ShiftFlow.Infrastructure/   # EF Core DbContext, migrations, DbSeeder
├── ShiftFlow.Application/      # Services (WorkOrder, Notification, Audit, Dashboard, Report)
└── ShiftFlow.Web/               # MVC controllers, Razor views, Identity, localization
```

## Features

- **Executive & Maintenance Dashboards** — KPIs, SLA tracking, work-order/asset charts, recent activity
- **Work Orders** — full lifecycle from creation to completion, with progress timeline and attachments
- **Emergency Tickets** — priority-based outage response tracking with response/restoration SLAs
- **Preventive Maintenance** — recurring schedules with checklists and one-click work order generation
- **Safety Permits (PTW)** — permit-to-work workflow with approval, PPE checklist, and printable permits
- **Assets** — full registry with maintenance history and a live map view (Leaflet)
- **Spare Parts** — inventory with automatic low/out-of-stock status
- **Shifts & Shift Calendar** — scheduling with multi-engineer assignment and a month-grid view
- **Leave Requests** — submission and manager approval workflow with notifications
- **Reports** — Excel/PDF exports plus in-app analytics charts
- **Bilingual UI** — English/Arabic with RTL layout switching
- **Audit Log** — tracks key system actions

## Getting Started

1. Open `ShiftFlow.sln` in Visual Studio 2022 (or run via `dotnet` CLI — .NET 8 SDK required)
2. Set `ConnectionStrings:DefaultConnection` in `ShiftFlow.Web/appsettings.json` (or `appsettings.Development.json`) to point at your SQL Server instance
3. From the repo root:
   ```bash
   dotnet restore ShiftFlow.sln
   dotnet build ShiftFlow.sln
   cd ShiftFlow.Web
   dotnet run
   ```
4. The database is created and seeded automatically on first run (migrations run at startup)
5. Browse to the URL shown in the console (typically `https://localhost:5xxx`)

## Seeded Test Accounts

| Role | Email | Password |
|------|-------|----------|
| Admin | admin@shiftflow.com | Admin@123456 |
| Shift Manager | manager@shiftflow.com | Manager@123456 |
| Engineer | engineer@shiftflow.com | Engineer@123456 |
| HR | hr@shiftflow.com | HrDept@123456 |

## Notes

- `appsettings.json` ships with a local `Trusted_Connection` SQL Server string and no secrets — update it for your environment before deploying.
- File uploads (work order attachments) are written to `ShiftFlow.Web/wwwroot/uploads/` at runtime and are git-ignored.
