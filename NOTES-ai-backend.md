# NOTES — ai/ai-backend

What the merger and the frontend need to know about the expanded STEP assistant.

## Endpoints (all on `AiAssistantController`, all `[Authorize(AiAssistant.Use)]` + rate-limited "ai")

| Route | Body | Response |
|---|---|---|
| `POST /AiAssistant/Query` | `{ text, history?: [{role,text}], context?: { page?, entityType?, entityId?, title? } }` | `{ answerText, voice, attachments: Attachment[] }` |
| `POST /AiAssistant/Confirm` | `{ token }` | same shape as Query |
| `POST /AiAssistant/Dismiss` | `{ token }` | `{ ok: true }` |

All three require the anti-forgery token (`[ValidateAntiForgeryToken]`), exactly as Query already
did. `attachments` is always present, possibly `[]` — existing clients that only read `answerText`
keep working unchanged.

### Context

`entityType` is allow-listed server-side to
`WorkOrder | InspectionOrder | MaintenanceOrder | Asset | Zone | SparePart | Contract | Vendor | User | Group`;
anything else is dropped (and then `entityId` is dropped with it), and `page`/`title` are trimmed to
200 chars. `entityId` may be sent as a JSON string **or** number — it is read as a `JsonElement` and
flattened, so `4` and `"WO-2026-0004"` both work. The context becomes one system line; there is no
`getCurrentContext` tool.

### Attachments emitted

Exactly the agreed five kinds, camelCase, discriminated by `kind`:

- `{ kind: "table", title?, columns: [{key,label}], rows: [{ [key]: string|number|null, _url?, _status? }] }`
  — `_url` is app-relative; `_status` is a **raw** status/stage string (`"Sent to Vendor"`,
  `"PendingApproval"`, `"Defective"`, `"Expired"`, `"Active"`, …) for the client to badge. Row
  dictionary keys are serialized verbatim (the web JSON defaults camelCase property names, not
  dictionary keys), so `_url`/`_status` survive.
- `{ kind: "cards", items: [{ title, subtitle?, badge?: {text,status?}, fields: [{label,value}], url? }] }`
- `{ kind: "links", items: [{ label, url, icon? }] }` — `icon` is a bare hint string
  (`box`, `clipboard`, `wrench`, `package`, `file`, `user`, `users`, `map-pin`, `bar-chart`,
  `check-square`, `plus`, `repeat`, `list`, `home`, `briefcase`, `alert`); map it to your icon set
  and fall back gracefully to no icon.
- `{ kind: "download", label, url }` — always an **existing** export action, never a generated file.
  Downloads are plain GETs and carry that action's own `[Authorize]` policy.
- `{ kind: "confirm", token, title, summary, items?: string[], actionLabel, danger? }`

A single turn can emit several attachments, in tool-call order. One tool may emit more than one
(`getAssetDetail` → cards + links; `bulkReassignOpenOrders` → table + confirm).

### Confirm flow

1. A destructive/bulk tool returns `{ pendingConfirmation: true, token, summary }` plus a `confirm`
   attachment and **does nothing**.
2. The user presses Confirm → `POST /AiAssistant/Confirm { token }`, or types "yes, do it" → the
   model calls the `confirmPendingAction` tool with the same token. Both paths run identical code.
3. Declining → `POST /AiAssistant/Dismiss { token }`.

Tokens are 32 URL-safe chars, single-use, 10-minute TTL, owned by one user. Another user's token is
invisible *and* cannot be consumed or dismissed by them. The permission recorded when the action was
proposed is re-checked at confirm time, and the action re-resolves its rows through the caller's
asset scope before acting — a scope or permission change between proposing and confirming stops it.

**Deployment caveat:** `PendingActionStore` is `IMemoryCache`-backed and therefore process-local. On
a multi-instance deployment (or with an app-pool recycle mid-conversation) a Confirm can land on an
instance that never saw the token and will answer "that confirmation has expired". Swap in a
distributed store (or sticky sessions) before scaling out. `IPendingActionStore` exists precisely so
that swap is a one-line DI change.

## Tool list

| Tool | Permission | Write |
|---|---|---|
| getMyInspectionOrders | — (self-scoped) | |
| getInspectionOrderDetail | — (self/assignee checked in the tool) | |
| getDashboardKpis | — | |
| findEmployee | InspectionOrder.Manage | |
| listGroups | Group.Manage | |
| getGroupDetail | Group.Manage | |
| getAssetRepairGuidance | Asset.View | |
| createInspectionOrder | InspectionOrder.Manage | ✔ |
| reportInspectionOutcome | InspectionOrder.Report | ✔ |
| cancelInspectionOrder | InspectionOrder.Manage | ✔ |
| createGroup / addGroupMember / removeGroupMember | Group.Manage | ✔ |
| searchAssets | Asset.View | |
| getAssetDetail | Asset.View | |
| getAssetHistory | Asset.View | |
| getAssetHealthSummary | Asset.View | |
| reportAssetDefect | Asset.ReportAction | ✔ |
| updateAssetStatus | Asset.Manage | confirm |
| listAssetCategories | Asset.View | |
| listActionTypes | Asset.View | |
| searchWorkOrders | WorkOrder.View | |
| getWorkOrderDetail | WorkOrder.View | |
| acceptWorkOrder | WorkOrder.Manage | ✔ |
| rejectWorkOrder | WorkOrder.Manage | confirm |
| sendWorkOrderToVendor | WorkOrder.Manage | ✔ |
| assignWorkOrderEmployee | WorkOrder.Assign | ✔ |
| setWorkOrderPriority | WorkOrder.Manage | ✔ |
| confirmWorkOrderFix | WorkOrder.Manage | ✔ |
| forceCloseWorkOrder | WorkOrder.Manage | confirm (danger) |
| listVendorsForAsset | WorkOrder.View | |
| searchMaintenanceOrders | MaintenanceOrder.View | |
| getMaintenanceOrderDetail | MaintenanceOrder.View | |
| createMaintenanceOrder | MaintenanceOrder.Manage | ✔ |
| completeMaintenanceOrder | MaintenanceOrder.Report | ✔ |
| approveMaintenanceOrder | MaintenanceOrder.Manage | ✔ |
| returnMaintenanceOrderToOpen | MaintenanceOrder.Manage | confirm |
| cancelMaintenanceOrder | MaintenanceOrder.Manage | confirm |
| searchSpareParts | SparePart.View | |
| getSparePartDetail | SparePart.View | |
| getCompatibleParts | SparePart.View | |
| getLowStockParts | SparePart.View | |
| adjustSparePartStock | SparePart.Manage | ✔ / confirm when the change is >50% of current stock or sets it to 0 |
| searchContracts | Contract.View | |
| getContractDetail | Contract.View | |
| listVendors | Vendor.View | |
| getVendorDetail | Vendor.View | |
| listZones | Asset.View | |
| getZoneOverview | Asset.View | |
| exportReport | *(self-checking)* Asset.Export / WorkOrder.Export / Contract.View / InspectionOrder.Export / MaintenanceOrder.Export / User.View / InspectionOrder.Manage per report kind | |
| getDailyBriefing | *(self-checking)* omits any section the caller can't see | |
| bulkCreateInspectionOrders | InspectionOrder.Manage | confirm |
| bulkReassignOpenOrders | InspectionOrder.Manage | confirm (danger) |
| navigateTo | — | |
| confirmPendingAction | — (re-checks the pending action's own permission) | ✔ |

"confirm" = the tool itself never writes; it returns a token and the write happens on Confirm.
Registry lives in `AiAssistantOrchestrator.Tools`; a tool missing from it fails closed.

## Architecture notes

- `AiRunResult(string Text, List<object> Attachments)` is what `RunAsync`/`ConfirmAsync` return.
  Attachments are already-serialized `JsonNode`s, so the controller hands them straight to the JSON
  result with no second, differently-configured serialization pass.
- `AiToolResultEnvelope.Strip(result, options)` is pure and static (and unit-tested): it serializes
  a tool result, removes a top-level `ui` property, and returns `(json, attachments)`. `ui` may be
  one attachment, an array of them, or `null` (search tools set `null` when they found nothing).
- Tool classes: `AiAssetToolFunctions`, `AiWorkOrderToolFunctions`, `AiMaintenanceToolFunctions`,
  `AiInventoryToolFunctions`, `AiContractVendorToolFunctions`, `AiZoneToolFunctions`,
  `AiReportToolFunctions`, `AiInsightToolFunctions` — all scoped, all injected into the
  orchestrator. `AiInspectionToolFunctions` is unchanged.
- `AiToolsBase` holds the scope helpers. **Every** read resolves ids through
  `IAssetScopeService`; no model-supplied id reaches `DbSet.Find`. Work/maintenance order lists
  filter by the caller's scoped asset ids exactly like `WorkOrdersController.Index` and
  `MaintenanceOrderService.GetAllAsync`. Contracts/vendors are deliberately **not** asset-scoped,
  matching `ContractsController`/`VendorsController`.
- All writes go through the existing services (`WorkOrderService`, `MaintenanceOrderService`,
  `SparePartService`, `AssetService`, `InspectionOrderService`, `GroupService`), so business rules,
  stock movements and audit rows stay in one place. Writes are additionally audited as
  `AI:{toolName}` / `AI:ConfirmedAction` on `AiTool`.
- `MaxIterations` 6 → 8; the per-user "ai" rate limiter is untouched.
- `AiLinks` is the only place a URL is built. Two route facts worth knowing:
  `MaintenanceOrdersController` has no `Index`, so "maintenance orders" links to `/Orders`; the
  defect-report page is `/WorkOrders/Report?assetId={id}`.

## Behaviour worth flagging to the owner

- **Overdue work orders**: work orders have no due date of their own, so `getDailyBriefing` treats
  only generated (PM / recurring) ones with a past `ScheduledDate` as overdue. Manually created work
  orders never count as overdue. If the owner wants an age-based rule ("open > N days"), that's a
  one-line change in `AiInsightToolFunctions`.
- **`bulkCreateInspectionOrders`** creates **one** order covering all matched assets (that is what
  `InspectionOrderService.CreateAsync` models), not one order per asset. Retired assets are excluded
  from the target set. Cap: 500 assets.
- **`bulkReassignOpenOrders`** moves orders assigned to an individual only; group-assigned orders are
  untouched (there is no "from group" concept in the services). Work orders move via
  `AssignEmployeeAsync`, inspection/maintenance via their `ReassignAsync`. Per-order failures are
  collected and reported rather than aborting the batch.
- **`updateAssetStatus`** always requires confirmation, not just for Retired (only Retired sets
  `danger: true`).

## Not done / out of scope

- `Translations.cs` untouched — see `TRANSLATIONS-ai-backend.md`. One 400-guard message in
  `Confirm`/`Dismiss` is currently unlocalized for that reason.
- No views/JS/CSS touched; the chat UI is the frontend agent's branch.
- No new export/file generation: `exportReport` only links existing actions.
- Attachment chrome (column labels, card field labels) is English-only by design — the model
  narrates in the user's language around it.

## Tests

`ShiftFlow.Web.Tests/AiAssistantTests.cs` — 11 tests: pending-action ownership / single-use /
TTL (via an injected `ISystemClock`, no sleeping) / token shape; `Strip` for single, array, absent
and null `ui` plus the confirm attachment's exact serialized shape; `getDailyBriefing`'s section
shape and attachment order on SQLite; and `bulkCreateInspectionOrders` preview → confirm creating
exactly one order with the right assignee and asset, with the token spent afterwards. The confirm
half runs against a **second** DbContext through a stub `IServiceProvider`, which is how the real
Confirm request behaves. `dotnet build` 0 errors, `dotnet test` 30/30 green.
