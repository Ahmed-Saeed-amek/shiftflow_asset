# NOTES — branch `fix/data-scope-perf`

## 1. Program.cs registration required (ONE LINE — the app will not start without it)

`ILookupCache` is a new scoped service injected by `AssetsController`, `SparePartsController`,
`SparePartsAnalyticsController`, `ZonesController`, `ContractsController`,
`RecurringOrdersController`, `AssetCategoriesController`, `OrderTypesController` and
`GroupsController`. Add, next to the other `builder.Services.Add…` lines (it also calls
`AddMemoryCache()`, which is idempotent):

```csharp
builder.Services.AddLookupCache();      // ShiftFlow.Application.Services
```

No other Program.cs change is needed. The extension lives in
`ShiftFlow.Web/Application/Services/LookupCache.cs`.

---

## 2. Schema changes (for the single merged migration)

**All are index-only — no column, table, or type changes, and no RowVersion.**
Each was checked against the existing configuration first; none duplicates an index already present.

| Entity / table | Index | Why |
| --- | --- | --- |
| `WorkOrder` | `IX` on `AssignedToUserId` | "My work orders" filters by assignee. |
| `WorkOrder` | `IX` on `CreatedDate` | Dashboard/recent-orders ordering. |
| `InspectionOrder` | `IX` on `AssignedToUserId` | Assignee filters. |
| `InspectionOrder` | `IX` on `AssignedToGroupId` | Group queue filters. |
| `InspectionOrder` | `IX` on `CreatedAt` | Recent-orders ordering. |
| `InspectionOrder` | `IX` on `DueDate` | Overdue widgets / KPI. |
| `MaintenanceOrder` | `IX` on `AssignedToUserId` | Assignee filters. |
| `MaintenanceOrder` | `IX` on `AssignedToGroupId` | Group queue filters. |
| `MaintenanceOrder` | `IX` on `CreatedDate` | Recent-orders ordering. |
| `GroupMember` | `IX` on `UserId` (alone) | The existing composite is `(GroupId, UserId)` — GroupId-leading, so "which groups is this user in" (asset-scope resolution, once per request) could not seek on it. |
| `ContractAsset` | `IX` on `AssetId` (alone) | The existing composite is `(ContractId, AssetId)`; the derived-vendor lookup searches by AssetId. |
| `Contract` | `IX` on `ContractType` | The PM scheduler scans by ContractType every tick. |
| `InspectionRunAsset` | **UNIQUE** `IX` on `(InspectionRunId, AssetId)` | An asset may only appear once per run. |

> ⚠️ The `InspectionRunAssets` unique index will fail to create if the existing data already
> contains a duplicate `(InspectionRunId, AssetId)` pair. Worth a one-off check before the migration
> is applied to an existing database:
> ```sql
> SELECT InspectionRunId, AssetId, COUNT(*)
> FROM InspectionRunAssets GROUP BY InspectionRunId, AssetId HAVING COUNT(*) > 1;
> ```

Already present and deliberately **not** re-added: `Asset(AssetTag)` unique, `Asset(Status)`,
`Asset(ZoneId)`, `WorkOrder(AssetId)`, `WorkOrder(Stage, Priority)`, `WorkOrder(WorkOrderNumber)`
unique, `MaintenanceOrder(AssetId)`, `MaintenanceOrder(Status)`, `InspectionOrder(Status)`,
`InspectionRunAsset(InspectionRunId)`, `Contract(VendorId)`, `RecurringOrder(IsActive)`,
`AuditLog(UserId, CreatedDate)`, and all the filtered scheduler-dedup unique indexes.

---

## 3. Changes needed in files this agent does not own

### 3.1 `Domain/Entities/RecurringOrder.cs` (owned by the order-services agent)

Finding 9 asked for the scheduler to record its outcome on the schedule row. The two columns are
**not** added here because `Domain/Entities` is not owned by this agent. Requested shape:

```csharp
public DateTime? LastRunAt { get; set; }
public string? LastError { get; set; }   // HasMaxLength(500) in ApplicationDbContext
```

Until they exist, `RecurringOrderSchedulerService` surfaces every failure two ways instead:

* `_logger.LogError` with the schedule id and the reason (previously only `LogWarning`), and
* an audit row via `IAuditService.LogAsync("GenerationFailed", "RecurringOrder", scheduleId, …)`,
  so failures are visible on **Audit Logs → filter Entity = `RecurringOrder`** with no shell access.

`PreventiveMaintenanceSchedulerService` does the same with `EntityType = "Contract"`.

If the two columns land, the follow-up is mechanical: set them inside the existing
`ReportFailureAsync` local function in each scheduler, plus `LastRunAt = DateTime.UtcNow` at the end
of each schedule's loop iteration, and add the DbContext `HasMaxLength(500)` for `LastError`.

### 3.2 System-context create signature needed from `InspectionOrderService` / `MaintenanceOrderService`

The schedulers currently pass `creatorUserId` into the normal `CreateAsync` overloads, which
re-apply that user's **current** asset scope. A schedule created by an employee who is later given a
narrower scope therefore silently stops generating orders for the assets outside it. This branch
mitigates it (falls back to the system Admin account when the creator is inactive, and reports the
skip) but cannot fix it properly without a scope-bypassing entry point. Requested signatures:

```csharp
// IInspectionOrderService
Task<InspectionOrder> CreateSystemAsync(
    int orderTypeId, string? description, string? assignedToUserId, int? assignedToGroupId,
    List<int> assetIds, DateTime? dueDate, string systemUserId,
    int? sourceRecurringOrderId = null, DateTime? scheduledDate = null);

// IMaintenanceOrderService
Task<MaintenanceOrder> CreateSystemAsync(
    int assetId, string? assignedToUserId, int? assignedToGroupId, string? description,
    DateTime? dueDate, string systemUserId, int orderTypeId,
    int? sourceRecurringOrderId = null, DateTime? scheduledDate = null);
```

Semantics: identical to the existing `CreateAsync`, except the asset-scope check is skipped (the
schedule was already scope-validated by `RecurringOrderService.ValidateAsync` when it was saved)
while every other validation — asset exists, asset not Retired, assignee active, order type active —
still applies. `WorkOrderService.CreateRecurringVendorOccurrenceAsync` already has this shape and
needs no change.

### 3.3 `Services/ContractAttachmentStorage.cs` (not owned)

`SaveAsync` writes every file to disk **before** inserting any `ContractAttachment` row, so a failed
`SaveChangesAsync` leaves orphaned bytes. `ContractsController.UploadAttachment` now compensates
from the outside: it checks the contract exists first, snapshots the upload directory, and deletes
whatever the request wrote if the save throws. The cleaner fix belongs in the helper — insert the
rows and save first, then write the streams, or wrap the whole thing in a try/catch that deletes the
files it created. Worth doing when that file's owner touches it.

### 3.4 `Controllers/VendorsController.cs` (not owned)

Vendor create/edit changes the `ILookupCache` active-vendor list, which `ContractsController` and
`RecurringOrdersController` read. Cache TTL is 5 minutes, so the list is at worst 5 minutes stale;
the exact fix is to inject `ILookupCache` and call `_lookups.InvalidateVendors();` after each
successful save/delete in that controller. Same applies to `Views/Vendors/Index.cshtml`, which still
uses the positional `openEdit(...)` modal pattern — it is outside the five views this finding named,
but `wwwroot/js/modal-editor.js` is ready for it.

### 3.5 `Localization/Translations.cs`

New strings are listed in `TRANSLATIONS-data.md` at the worktree root.

### 3.6 `Views/Shared/_Layout.cshtml`

Per the brief, Chart.js is being removed from the layout by another agent. `Views/Dashboard/Index.cshtml`
and `Views/SparePartsAnalytics/Index.cshtml` now load
`https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js` themselves inside
`@section Scripts { }`, before their chart code. The layout currently still loads 4.4.2 — once the
layout's tag is removed, nothing else needs changing here.

Also per the brief, the per-view `TempData["Success"]/["Error"]` alert blocks were removed from
`Views/SpareParts/Details.cshtml` and `Views/AssetCategories/Index.cshtml` on the assumption the
layout renders a single toast. If that toast is not in place, those two pages lose their feedback.

---

## 4. Decisions worth knowing at merge time

### 4.1 Spare-parts scope rule (finding 2)

The old filter was `p.AssetLinks.Any(l => scopedAssetIds.Contains(l.AssetId))`, applied
unconditionally — which also hid every part with **no** asset links from **every** user, including
unscoped ones, contradicting its own doc comment. The rule now implemented is:

* **No asset scope → see every part.** (`ScopedPartsAsync` returns the query untouched.)
* **Scoped → see a part if** it is linked to at least one in-scope asset **or** it has no asset
  links at all. An unlinked generic consumable belongs to nobody's zone; hiding it only makes the
  catalog incomplete with no confidentiality gained.

The same rule now gates `Details`, `Edit` (GET and POST) and `AdjustStock`, which previously gated
only `Details`. `Edit`'s chip list and `Details`' "Compatible Assets" list are built from the
scoped asset set, so an out-of-scope asset's tag/name is never rendered.

### 4.2 Inspection-order scope and empty orders (finding 2)

`Items.All(...)` is vacuously true for an order with zero run items, so an empty inspection order
passed every scope filter. All four sites (`DashboardService`, `DashboardController.Index`,
`ExportPdf`, `BuildOverdueOrdersAsync`, `BuildRecentOrdersAsync`) now read
`Items.Any() && Items.All(i => scoped.Any(a => a.Id == i.AssetId))`.

### 4.3 `ZoneOverviewController.Details(int zoneId)` renamed to `Details(int id)`

`Views/ZoneOverview/Index.cshtml` was updated (`asp-route-id`, and the row `onclick`). No other view
or controller referenced `asp-route-zoneId` for this controller.

### 4.4 `AuditLog` "GenerationFailed" action

New audit `Action` value written by both schedulers. It shows up in the Audit Logs screen's Action
filter automatically (the filter list is `DISTINCT` over the table).

### 4.5 Asset delete

`AssetService.DeleteAsync` had no caller at all. It now guards against referencing
WorkOrders / MaintenanceOrders / InspectionRunAssets / ContractAssets / SparePartAssets /
RecurringOrderAssets and throws a localized `InvalidOperationException` naming what blocks it. A new
`[HttpPost] AssetsController.Delete` action surfaces that message via `TempData["Error"]`, and
`Views/Assets/Details.cshtml` gained a confirm-guarded Delete button for `Asset.Manage` holders.

### 4.6 `PermissionService`

`_userManager` is still injected (constructor signature unchanged, so no DI edit) but is no longer
used by `ComputeEffectivePermissionsAsync`, which is now two queries (`UserPermissions`, then
`UserRoles ⨝ RolePermissions`) instead of four or five. The 5-minute sliding cache is unchanged.

### 4.7 CSS tokens used by charts (finding 13f)

`wwwroot/js/theme-colors.js` resolves `--chart-1…5`, `--primary`, `--destructive`, `--success`,
`--warning`, `--muted-foreground` and `--foreground` from `site.css`. All of them exist, so no
`hsl(var(--primary))` fallback was actually needed; the helper still falls back to it if a token is
ever removed. `site.css` was not edited.
