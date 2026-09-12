# Orders UI pass — notes for the merge (`ui/ui-orders`)

This branch codes against the shared-UI API that is being built in parallel. Nothing in
`Views/Shared/`, `site.css`, `site.js` or `Translations.cs` was touched. Below is everything the
merge has to reconcile.

## 1. Temporary files — delete all five on merge

| File | Why it exists |
| --- | --- |
| `ShiftFlow.Web/ViewModels/_UiStubs.TEMP.cs` | Declares `PageHeaderWithBreadcrumbs` and `EmptyStateModel` (see below). |
| `ShiftFlow.Web/Views/Orders/_EmptyState.cshtml` | Local copy of the shared empty-state partial. |
| `ShiftFlow.Web/Views/WorkOrders/_EmptyState.cshtml` | ditto |
| `ShiftFlow.Web/Views/VendorPortal/_EmptyState.cshtml` | ditto |
| `ShiftFlow.Web/Views/Users/_EmptyState.cshtml` | ditto |

**A folder-local partial wins over `Views/Shared`,** so if these four `_EmptyState.cshtml` copies
survive the merge they will keep shadowing the real shared partial. They are byte-identical to each
other and carry the same `EmptyStateModel` contract, so deleting them is the whole cleanup. They
exist because a view that references a partial which does not exist throws at render time — without
them every empty list on this branch is a 500, not a placeholder.

## 2. `Breadcrumbs` on `PageHeaderModel`

`PageHeaderModel` is not `partial` and lives in `ViewModels/ViewModels.cs`, which this branch does
not own — so the agreed `new PageHeaderModel{ …, Breadcrumbs = … }` shape could not be stubbed by
adding a property from another file. The stub is a subclass instead:

```csharp
public class PageHeaderWithBreadcrumbs : PageHeaderWithActions
{
    public List<(string Label, string? Url)>? Breadcrumbs { get; set; }
}
```

**Post-merge:** delete the class and replace `PageHeaderWithBreadcrumbs` with `PageHeaderModel`
(or `PageHeaderWithActions` where `Actions` is also set) in these views:

- `Views/Orders/Index.cshtml`, `Views/Orders/Create.cshtml`
- `Views/WorkOrders/Index.cshtml`, `Create.cshtml`, `Report.cshtml`
- `Views/VendorPortal/Index.cshtml`
- `Views/Users/MyOrders.cshtml`, `MyHistory.cshtml`

Until the shared `_PageHeader` learns to render `Breadcrumbs`, the property is simply ignored — the
crumb trail on those pages is invisible (no dead ends: every one of them still has a Cancel or a
sidebar route out).

## 3. Detail pages render their breadcrumb inline — on purpose

`WorkOrders/Details`, `InspectionOrders/Details`, `MaintenanceOrders/Details` and
`VendorPortal/Details` do **not** use `_PageHeader`: their title line carries the status/priority
badges beside the `<h1>` (item 5 of the brief: "status badge next to the title, not floating"),
which `_PageHeader` has no slot for. Each renders the same small `<nav class="breadcrumb">` block
itself. If `_PageHeader` later grows a badge slot, these four can be folded back into it; until
then they must **not** also get a partial-rendered breadcrumb or the page shows two.

## 4. Shared CSS/JS this branch assumes (not yet present)

| Hook | Used by | Behaviour today |
| --- | --- | --- |
| `.row-link` + `data-href` delegated handler in `site.js` | every list table | Rows are not clickable yet. Harmless: the first column is a real `<a>` in every table, so nothing is unreachable. |
| `.filter-bar`, `.filter-bar__count` | every list page | Classes are applied *and* the bar carries `d-flex flex-wrap align-items-center gap-2 mb-3` utilities, so it lays out correctly with or without the CSS. |
| `.form-actions` | `Orders/Create`, `WorkOrders/Create`, `WorkOrders/Report` | Same approach — utilities alongside the class. |
| `.required` label modifier | the three forms above | No asterisk renders until the shared CSS lands. |
| `data-hide-mobile` | low-value cells | Cells still show on phones until the rule exists. |
| `.table-card-stack` + `data-label` | every list table | **Already in `site.css`** — mobile cards work today. |

## 5. `StatusStyle.Label` → used `StatusDisplay.Label`

`StatusStyle` is a static class in `Services/StatusStyle.cs` (not owned here), so `Label` could not
be stubbed onto it. The views call the existing `StatusDisplay.Label(status)` in
`ViewModels/InspectionOrderViewModels.cs`, which produces exactly the same spaced English label.
Post-merge, swap the call to `StatusStyle.Label` and drop `StatusDisplay` if it becomes unused.

**One colour gap to close in `StatusStyle.Map` (shared branch owns that file):** the map keys
`"PendingApproval"` unspaced. Now that `_StatusBadge` is handed the *spaced* label, a
`Pending Approval` badge falls through to the neutral grey default instead of the warning colour it
had before. `"In Progress"` is already mapped correctly. Adding
`["Pending Approval"] = "bg-warning-subtle text-warning-emphasis border-warning-subtle"` restores it.

## 6. Things the brief asked for that live outside this branch

- **"Add Part" disabled-state text** (`VendorPortal/Details`, item 7) is inside
  `Views/Shared/_SparePartPicker.cshtml` — not editable here, and it already explains itself. Left
  alone.
- **`_ExportButtons`** is used as-is on `WorkOrders/Index`. `Orders/Index` does not use it: that page
  needs one dropdown with two Excel entries (inspection / maintenance), which the partial has no
  shape for.

## 7. Behaviour changes worth knowing at review time

- `OrderCreateVm.AssigneeType` now defaults to `"User"` (was `"Group"`), so a fresh Orders/Create
  form opens on Employee. Only affects the initial GET render — the POST always carries the radio.
- `Orders/Index` category badges now use the Dashboard's soft blue (Inspection) / soft teal
  (Maintenance) instead of the per-order-type auto-assigned hex, so the `ReadableTextColor` helper
  and the colour-carrying type chips are gone. Order type *name* is still what the badge says.
- `Orders/Index` lost both chip rows (type + status) in favour of two dropdowns in the filter bar —
  dropdown-only, per the brief's preference; a ≤5-item status chip row could not be kept without
  wrapping on a phone once the type filter was in the same bar.
- `WorkOrders/Details` and `MaintenanceOrders/Details` reorder their columns on phones
  (`order-1/order-2` + `-lg-` overrides) so the action card comes before the reference sidebar;
  Work Order stage history moved into its own `col-lg-8 offset-lg-4` column so it can stay last.
- Auto-save feedback on `WorkOrders/Details` (item 4): both controls already ended in a redirect
  that raises the existing `TempData` toast (`Employee assignment updated.` / `Priority updated.` /
  the error message) — what was missing was anything *during* the round trip, so both forms now show
  an inline `Saving…` on submit. No new toast plumbing was needed.
- `DbSeeder` gained `SeedOrderTypesAsync` (the only change in that file): idempotent add-if-missing
  for Inspection / Quick Check / Standard, plus a deliberately narrow fix-up that only touches a row
  whose English name matches **and** whose `NameAr` is still null — i.e. only the untouched
  migration-seeded row, never a catalog an admin has edited.
