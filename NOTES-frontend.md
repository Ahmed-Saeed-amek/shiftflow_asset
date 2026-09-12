# NOTES — frontend-shared (branch `fix/frontend-shared`)

What changed in the shared layer, what other agents have to change in the views they own, and
the helper signatures to call. Nothing outside the shared-file ownership list was edited.

---

## 1. Program.cs — nothing is required

`INavPermissions` has a registration extension in `ShiftFlow.Web/Services/NavPermissions.cs`:

```csharp
builder.Services.AddNavPermissions();   // optional, next to the other AddScoped lines
```

It is **optional**. The layout and views use the `HttpContext` extension instead, which
resolves its dependencies off `RequestServices` and needs no registration:

```csharp
@using ShiftFlow.Web.Services
@{ var perms = await Context.GetNavPermissionsAsync(); }
```

Register it only if you want `@inject INavPermissions Nav` / constructor injection.

---

## 2. Per-request permissions — `NavPermissionSet`

`_Layout` used to run ~21 `IAuthorizationService.AuthorizeAsync` calls (each opening its own DI
scope) plus a `UserManager.GetUserAsync`, and feature views repeated the same checks. All of it
is now **one** `IPermissionService.GetUserEffectivePermissionsAsync` call, cached on
`HttpContext.Items` for the rest of the request. Same Deny > Allow > Role semantics, same
IsAdmin super-permission behaviour — it is literally the same code path the authorization
handler used.

```csharp
@using ShiftFlow.Web.Services
@{
    var perms = await Context.GetNavPermissionsAsync();
}
@if (perms.CanViewAssets) { … }
@if (perms.Has(PermissionCatalog.AssetExport)) { … }   // any PermissionCatalog constant
@if (perms.HasAny(PermissionCatalog.WorkOrderManage, PermissionCatalog.WorkOrderAssign)) { … }
```

Also on it: `perms.UserId`, `perms.DisplayName` (FullName, falling back to the login name), and
named properties for every sidebar permission (`CanViewMyWork`, `CanManageInspectionOrders`,
`CanViewSpareParts`, `CanReportAssetAction`, `CanManageRbac`, …).

**Please migrate**: any view still doing `@inject IAuthorizationService AuthZ` +
`await AuthZ.AuthorizeAsync(User, …)` is re-doing work that is already cached. Swapping to
`perms.Has(...)` costs nothing extra per check. `Views/Account/AccessDenied.cshtml` and
`Views/MyHome/Index.cshtml` are already converted as examples.

---

## 3. Toasts — remove per-view TempData alerts

`_Layout` renders `Views/Shared/_Toast.cshtml` once, for the whole app:
`TempData["Success"]` → success toast (role=status, auto-hides after 5s),
`TempData["Error"]` → error toast (role=alert, stays until dismissed). Top-end positioned and
RTL-aware, still translated through `Loc.T` exactly as the old navbar badge was.

**Action required in views you own** — these still render their own copy and will now show the
message twice:

- `Views/AssetCategories/Index.cshtml`
- `Views/MaintenanceOrders/Details.cshtml`
- `Views/Rbac/Index.cshtml`, `Rbac/RolePermissions.cshtml`, `Rbac/UserPermissions.cshtml`
- `Views/SpareParts/Details.cshtml`
- `Views/Users/Profile.cshtml`
- `Views/VendorPortal/Details.cshtml`
- `Views/Vendors/Details.cshtml`
- `Views/WorkOrders/Details.cshtml`

Delete the `@if (TempData["Success"] …) { <div class="alert …"> }` blocks. Inline alerts that
are *not* TempData-driven (validation summaries, static hints) are unaffected.

From JS you can raise one yourself:

```js
window.showToast(message, 'success' | 'error');
```

---

## 4. Chart.js is no longer in the layout

It used to load on every page for the two views that draw charts. Add it to those two views'
`@section Scripts` (pinned + SRI):

```html
<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.2/dist/chart.umd.min.js"
        integrity="sha384-e6cc9LaIG7xZ3XD5B+jtr1NhTWPQGQdRCh6xiZ+ZFUtWCpg4ycv3Sh+SkZoopvUY"
        crossorigin="anonymous"></script>
```

Needed by: `Views/Dashboard/Index.cshtml`, `Views/SparePartsAnalytics/Index.cshtml`.
Use the existing `.chart-wrap` class + `maintainAspectRatio:false` (see site.css).

**jsQR** is also gone from the layout — `site.js` injects it (with SRI) on the first
`window.scanAssetQr()` call, so nothing needs to change in views that use the scan button.

---

## 5. Leaflet — please add SRI in the views that load it

`Views/Zones/Index.cshtml`, `Zones/Details.cshtml`, `Zones/_Form.cshtml`,
`ZoneOverview/Index.cshtml`, `VendorPortal/Index.cshtml` all load Leaflet 1.9.4 without
integrity attributes. Verified hashes:

```html
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/leaflet.css"
      integrity="sha384-sHL9NAb7lN7rfvG5lfHpm643Xkcjzp4jFvuavGOndn6pjVqS6ny56CAt3nsEVT4H" crossorigin="anonymous" />
<script src="https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/leaflet.js"
        integrity="sha384-cxOPjt7s7Iz04uaHJceBmS+qpjv2JkIHNVcuOrM+YHwZOmJGBXI00mdUXEq65HTH" crossorigin="anonymous"></script>
```

(The layout's own Bootstrap CSS/JS + Bootstrap Icons and Login/Error/AccessDenied's copies now
carry SRI, via the new `Views/Shared/_HeadLinks.cshtml`.)

---

## 6. Shared JS helpers (`wwwroot/js/site.js`)

All on `window`, all safe to call from a view's `@section Scripts`.

| Helper | Signature | What it does |
|---|---|---|
| `t` | `t(key, fallback?)` | Reads `window.i18n` (emitted by the layout). |
| `esc` | `esc(value) -> string` | HTML-escapes a value before it reaches `innerHTML`. |
| `fetchJson` | `fetchJson(url, options?) -> Promise` | `fetch` + `Accept: application/json` + **`r.ok` check** + `.json()`. Rejects on a non-2xx instead of returning `[]`. |
| `showListError` | `showListError(listGroupEl)` | Localized "Couldn't load — please try again." row inside a `.list-group` dropdown. |
| `showSelectError` | `showSelectError(selectEl)` | Same, as the only (disabled) option of a `<select>`. |
| `showToast` | `showToast(message, 'success'\|'error')` | See §3. |
| `typeaheadKeys` | `typeaheadKeys(searchInput, listEl, {onOpen, onClose})` | Keyboard behaviour for a typeahead dropdown. |
| `initCascadingSelect` | see below | Parent→child `<select>` cascade. |
| `restoreFixForm` | see below | Re-fills a fix-report form after a rejected POST. |

### `initCascadingSelect(parentSel, childSel, urlFn, opts)`

Replaces the hand-rolled `fillSelect` + change-handler duplicated in
`Views/Assets/_Form.cshtml`, `Views/InspectionOrders/Details.cshtml` and
`Views/WorkOrders/Report.cshtml`.

```js
initCascadingSelect('#categoryParentSelect', '#categorySubSelect',
    id => `/AssetCategories/ByParent?parentId=${id}`,
    {
      placeholder: noneLabel,     // string, or null for no blank row. Default '—'
      selectedId: preselectedId,  // preselect on the initial load
      label: i => i.name,         // default: nameAr in RTL, else name
      onChange: items => updateCategoryHidden(),
      onFilled: items => {},
      loadInitial: true           // default: loads immediately if the parent has a value
    });
// returns { reload(selectedId), fill(items, selectedId) }
```

Accepts a selector string or an element for either select. On a failed fetch it calls
`showSelectError(child)` instead of leaving a silently empty dropdown.

*Caller note*: `Assets/_Form.cshtml` sets the hidden `CategoryId` to the parent synchronously in
its own `change` handler before the fetch resolves (deliberate — see the comment there). Keep
that line and do the rest through `onChange`.

### `restoreFixForm(containerId, data?)`

Replaces the `(function restoreFixForm() { … })()` copies in
`Views/WorkOrders/Details.cshtml` (~line 477) and `Views/VendorPortal/Details.cshtml` (~line 203).

```html
<script>
  window.__fixFormRestore = {
    completionDate: @Html.Raw(System.Text.Json.JsonSerializer.Serialize(TempData["FixForm_CDate"] as string)),
    sparePartIds:   @Html.Raw((string?)TempData["FixForm_SparePartIds"] ?? "[]"),
    partQuantities: @Html.Raw((string?)TempData["FixForm_PartQuantities"] ?? "[]")
  };
  restoreFixForm(document.getElementById('employeeFixPartsList') ? 'employeeFixPartsList'
               : 'advanceWithoutVendorPartsList');
</script>
```

Same behaviour as before: no-ops when the form isn't on the page, and waits on
`window.__sparePartsReady[containerId]` before adding rows.

### Typeahead keyboard behaviour

Every picker (`_EmployeePicker`, `_AssetSinglePicker`, `_ZoneCombobox`, `_AssetMultiPicker`) now
has: **Enter** picks the highlighted row and **never submits the parent form**, **Up/Down** move
the highlight, **Escape** closes the list. The search input gets `role=combobox` +
`aria-activedescendant`, the list `role=listbox`, the rows `role=option`. If you build your own
dropdown out of `<button>` rows inside a `.list-group` that toggles `.d-none`, call
`typeaheadKeys(input, list, {onOpen, onClose})` and you get the same behaviour for free.

---

## 7. `_PageHeader` / `_KpiCard` no longer translate

Decision: **the partial does not translate — callers pass final text.** Both used to call
`Loc.T(Model.Title)` even though every caller already passes `Loc.T("…")` or a live value
(an asset tag, a vendor name), which meant user data was being fed through the dictionary.

I grepped every caller: **no caller passes an untranslated key**, so nothing needs changing.
One caller is actively improved by this — `Views/Dashboard/Index.cshtml` line 28 passes
`$"{Model.CriticalOpenWorkOrders} {Loc.T("critical")}"` as a `KpiCardModel.Subtitle`, which was
previously being looked up as a whole ("3 critical") and missing.

### Action buttons without string concatenation

`PageHeaderModel.ActionsHtml` (raw HTML string) still works — no existing caller has to change.
For new/refactored callers there is a safer shape, plus a partial for the common Excel/PDF pair:

```csharp
@{
    var actions = await Html.PartialAsync("_ExportButtons",
        new ExportButtonsModel { ExcelUrl = Url.Action("ExportExcel"), PdfUrl = Url.Action("ExportPdf") });
}
<partial name="_PageHeader" model='new PageHeaderWithActions { Title = Loc.T("Assets"), Actions = actions }' />
```

`PageHeaderWithActions : PageHeaderModel` lives in the new file
`ShiftFlow.Web/ViewModels/PageHeaderWithActions.cs` (a separate file specifically so it doesn't
conflict with anyone editing `ViewModels.cs`). `_PageHeader` prefers `Actions` when present and
falls back to `ActionsHtml`.

---

## 8. `StatusStyle` (`ShiftFlow.Web/Services/StatusStyle.cs`)

The 56-entry status→badge-class map moved out of `_StatusBadge.cshtml` (where it was rebuilt on
every render of every table row) into a static type. Identical output.

```csharp
<span class="badge border @StatusStyle.For(status)">@Loc.T(status)</span>
```

Use `StatusStyle.For(status)` anywhere you need the same colour outside a badge.
`StatusStyle.JsLabelNames` is the subset the layout serializes into `window.i18n.status` for
zone-map.js popups.

---

## 9. Other shared changes worth knowing

- **`_SparePartPicker`**: the "Add Part" button lost its inline `onclick=` in favour of a
  delegated listener (`data-add-spare-part="<containerId>"`). `window.addSparePartRow(id)` is
  unchanged, so direct callers still work. Its fetch now checks `r.ok` and shows a localized
  failure instead of an empty picker that looks like "no compatible parts".
- **`_FloatingAvatar`** is markup-only; the toggle and scroll-hide behaviour live in `site.js`
  (`initFloatingAvatar`). The bubble is a real `<button>`, so it's keyboard-reachable.
- **Sidebar active state** is set server-side from `RouteData` (`NavCls(controller, action?)` in
  `_Layout`). The old JS compared the full href, including the query string, so any filtered or
  paged list highlighted nothing. `initSidebarActiveFallback()` only fills in when the server
  marked nothing, by longest path-prefix match.
- **Navbar date** renders in the viewer's timezone/locale via `[data-local-date]` +
  `initLocalDates()`; the server-rendered `dd/MM/yyyy` stays as the no-JS fallback. Reuse the
  attribute anywhere a UTC date is shown: `<span data-local-date="@d.ToString("o")">@fallback</span>`.
- **Skip link** added; `<main>` is `id="mainContent" tabindex="-1"`. Don't add a second `<main>`.
- **`:focus-visible`** now draws a primary-coloured ring globally.
- **Google Fonts** moved from an `@import` at the top of site.css to `preconnect` + `stylesheet`
  links in `_HeadLinks.cshtml`.
- **Dead CSS removed** (grep-confirmed unused in `Views/`): the whole `.cal-*` shift-calendar
  block, `.task-status-select`, `.mobile-collapsible`, `.task-actions`, `.bg-purple`. If you
  were about to use one of these, it's gone on purpose — it belonged to the previous product.
- **`.card` shadow**: every card in the app is `card border-0 shadow-sm`, and Bootstrap's
  `.shadow-sm` carries `!important`, so the designed `.card` shadow never rendered. Fixed by
  remapping `--bs-box-shadow-sm` to the new `--card-shadow` token — no markup change needed.
- **`.font-heading`** was used by several views but never defined. It now exists.
- **site.css tokens** are documented in a header block at the top of the file. Use
  `hsl(var(--primary))` / `hsl(var(--primary) / .15)`; don't hard-code a token's hex.

---

## 10. New strings

Every new English string (with Arabic) is in `TRANSLATIONS-frontend.md` at the repo root, for
whoever owns `Localization/Translations.cs` to merge in. Until then they render in English.
