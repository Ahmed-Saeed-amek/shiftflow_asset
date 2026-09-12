# Notes — `ui/ui-assets` (assets, zones, spare parts, contracts, lookups, audit log, dashboard)

## Temporary stub the merger must delete

`ShiftFlow.Web/ViewModels/_UiStubs.TEMP.cs` exists **only** so this branch builds on its own. It
declares, matching the agreed shared API exactly:

* `PageHeaderModel.Breadcrumbs` — `List<(string Label, string? Url)>?`
* `EmptyStateModel` — `Icon` / `Title` / `Text` / `ActionUrl` / `ActionLabel`

Both already exist on `ui/ui-shared`. **When merging: delete `_UiStubs.TEMP.cs`.** It attaches
`Breadcrumbs` via a `partial` class, which required adding the single word `partial` to
`public class PageHeaderModel` in `ViewModels/ViewModels.cs` (no other change to that class — its
body is untouched, so it merges cleanly with the shared branch's additions). Drop the `partial`
keyword too if you prefer; nothing else depends on it.

## Shared-layer pieces this branch codes against but does not provide

These are referenced by the views here and must land from `ui/ui-shared` or the pages break at
runtime (they are *not* build errors, so the 0-error build does not cover them):

* `Views/Shared/_EmptyState.cshtml` — used by Assets, Zones, SpareParts, SparePartsAnalytics,
  Contracts, RecurringOrders and AuditLogs index pages.
* `_PageHeader` rendering `Model.Breadcrumbs`.
* CSS: `.filter-bar`, `.filter-bar__count`, `.row-link`, `.row-actions`, `.form-actions`,
  `.help-text`, `.form-label.required`, and `[data-hide-mobile]` (expected to hide the cell inside
  a `.table-card-stack` card at <768px). `.table-card-stack` itself already exists.
* `site.js`: click/Enter delegation for `tr.row-link[data-href]`. Every per-row `onclick` and
  `cursor-pointer` in these views has been removed in favour of it.

## Deliberate behaviour changes

* **Filter bars submit explicitly.** The Assets list previously auto-submitted on every
  `<select>` change; that is gone in favour of the shared bar's single `Filter` button, so every
  list in the app behaves the same way. `Clear` only renders when a filter is actually active.
* **Zones and Contracts and RecurringOrders gained server-side filters.** New optional query-string
  parameters, new view-model properties, no business-logic change:
  * `ZonesController.Index(page, q, locationCategoryId)`
  * `ContractsController.Index(page, q, vendorId, contractType, status)` — `status` applies the same
    expired/active rule as `ContractRow.StatusLabel`, pushed into SQL so paging and the count agree.
  * `RecurringOrdersController.Index(q, orderTypeId, status)` — the view model changed from
    `List<RecurringOrder>` to the new `RecurringOrderIndexViewModel`.
* **Assets `_Form` inlines the zone picker** instead of using `Views/Shared/_ZoneCombobox.cshtml`.
  The shared partial renders its category `<select>` with only a placeholder option, which read as an
  unlabelled second dropdown beside "Zone"; the brief asks for a clearly labelled "Zone Category"
  select above the zone search. The inlined markup keeps the identical `data-zc-*` hooks, so
  `initZonePickers()` in site.js drives it unchanged. Other callers of `_ZoneCombobox` are untouched.
  If the shared partial later grows a `CategoryLabel`, this view should go back to using it.
* **Assets/Details** lost its standalone right-aligned Delete form row; Delete now lives at the
  bottom of the header "More" menu, `text-danger`, after a divider, with `data-confirm`.
* **Dashboard KPI tiles are links** (`Orders?status=Open`, `Orders?overdue=true`, `Groups`,
  `Assets?status=Defective`, `WorkOrders`, `SpareParts?lowStockOnly=true`). The chart card gained
  `h-100` so it matches the Overdue card's height on desktop.

## Things the brief asked for that could not be done here

* **"Deactivate" in the lookup kebabs.** None of AssetActionTypes, MaintenanceActionTypes,
  WorkOrderBlockReasons or AssetCategories has a Deactivate endpoint — active/inactive is the
  `IsActive` checkbox inside the existing edit modal, and adding an endpoint would be a
  business-logic change, which this branch is not allowed to make. The kebabs therefore carry
  **Edit** (plus **Delete** on Order Types, the only lookup with a Delete action). If a Deactivate
  POST is added later, it drops straight into the same `<ul class="dropdown-menu">`.
* **Contracts `_Form` "Add by category" label.** The category select + "Add All" button live inside
  `Views/Shared/_AssetMultiPicker.cshtml`, which this branch does not own; they already sit on one
  line there, labelled "Category". A `.help-text` line above the picker now explains the two ways to
  add assets. Renaming that inner label to "Add by category" is a one-line change in the shared
  partial.
* **RecurringOrders "failure / last-run info".** `RecurringOrder` has no last-run or failure
  columns and the Index never showed any, so there was nothing to preserve. Nothing was removed.
* **AssetActionTypes has no table**, so the kebab was applied to its card headers and cause rows
  instead of table rows; the card layout, the inline "new cause"/"new action type" forms and the
  modal-editor pattern are unchanged.

## Other

* New EN strings are listed with Arabic in `TRANSLATIONS-assets.md` (root of this worktree) because
  `Translations.cs` is owned by another agent this round.
* Terminology: "Asset Locations" → "Zones", "Location Category" → "Zone Category" in every view in
  this branch's scope. The nav label in `Views/Shared/_Layout.cshtml` is **not** in scope and may
  still say the old thing.
* All dates in these views now render `dd/MM/yyyy` (`dd/MM/yyyy HH:mm` in the audit log) through
  `Loc.TDate`; the remaining `yyyy-MM-dd` renders in Contracts' PM schedule and Assets' contract
  table were converted.
* `wwwroot/js/zone-map.js`, `modal-editor.js` and `theme-colors.js` were not modified
  (`node --check` clean).
