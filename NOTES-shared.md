# Shared UI conventions — copy-pasteable

Everything here lives in `Views/Shared/*`, `wwwroot/css/site.css`, `wwwroot/js/site.js`.
Feature views consume it; don't re-implement any of it locally.

House rules that all of it assumes:
- Product name is **STEP** everywhere it's shown to a user.
- **Zone** / **Zone Category** — never "Asset Location", "Location Category", "Location Type".
- Dates are `dd/MM/yyyy`. Identifiers (order numbers, asset tags, contract numbers) are normal
  sans-serif `fw-semibold` links (`.identifier`), never monospace.
- Normal lists: no per-row Edit/View buttons — the primary column is the link and the row is
  clickable. Admin lists get a kebab. Status chips only for short (≤5) status sets.
- Detail/create/edit pages get a breadcrumb, never a Back button.

---

## 1. Breadcrumbs (`_PageHeader`)

Labels are passed in **already translated** — the partial never calls `Loc.T`.
Last item has a `null` Url and renders as `aria-current="page"` text.

```cshtml
<partial name="_PageHeader" model='new PageHeaderModel{
    Title = Model.OrderNumber,
    Breadcrumbs = new(){ (Loc.T("Work Orders"), Url.Action("Index")), (Model.OrderNumber, null) }
}' />
```

Works identically through `PageHeaderWithActions` (it derives from `PageHeaderModel`).
The `/` separator is a CSS token (`--bs-breadcrumb-divider`) so it mirrors under RTL.

## 2. Header actions + More menu

At most **one** `btn-primary`, up to **two** `btn-outline-*` secondaries, everything else —
destructive actions included — inside the More dropdown.

`PageHeaderWithActions.Actions` is an `IHtmlContent` — render the buttons with a partial (or a
small `<text>` block) first, then hand the result to `_PageHeader`:

```cshtml
@{ var actions = await Html.PartialAsync("_DetailsActions", Model); }
<partial name="_PageHeader" model='new PageHeaderWithActions{
    Title = Model.OrderNumber,
    Breadcrumbs = new(){ (Loc.T("Work Orders"), Url.Action("Index")), (Model.OrderNumber, null) },
    Actions = actions
}' />
```

Inside that actions partial:

```html
<a href="@Url.Action("Edit", new { id = Model.Id })" class="btn btn-primary">
    <i class="bi bi-pencil me-1"></i>@Loc.T("Edit")
</a>
<a href="@Url.Action("Print", new { id = Model.Id })" class="btn btn-outline-secondary">…</a>
<partial name="_HeaderMoreMenu" model='new List<HeaderMenuItem>{
    new(){ Label = Loc.T("Duplicate"), Url = Url.Action("Duplicate", new { id = Model.Id }), Icon = "bi-files" },
    new(){ Label = Loc.T("Delete"),    FormAction = Url.Action("Delete", new { id = Model.Id }),
           Icon = "bi-trash", IsDanger = true, ConfirmText = Loc.T("Are you sure?") }
}' />
```

`HeaderMenuItem` = `Label, Url?, FormAction?, Icon?, IsDanger, ConfirmText?, FormFields?`.
`Url` renders an `<a class="dropdown-item">`; `FormAction` renders a POST mini-form with an
antiforgery token and `data-confirm` (handled by site.js). Danger items are auto-moved below a
`<hr class="dropdown-divider">` and get `text-danger`. Actions wrap on phones and the cluster
goes full width under 576px (`.page-header__actions`).

## 3. Filter bar

One search box + dropdowns + **one** Filter button. No auto-submitting selects.

```cshtml
<form method="get" class="filter-bar mb-3">
    <input type="search" name="q" value="@ViewBag.Q" class="form-control form-control-sm filter-bar__search"
           placeholder="@Loc.T("Search")" aria-label="@Loc.T("Search")" />
    <select name="status" class="form-select form-select-sm">
        <option value="">@Loc.T("All Statuses")</option>
    </select>
    <button type="submit" class="btn btn-outline-secondary btn-sm">@Loc.T("Filter")</button>
    @if (hasActiveFilter)
    {
        <a href="@Url.Action("Index")" class="btn btn-link btn-sm">@Loc.T("Clear")</a>
    }
    <span class="filter-bar__count">@total @Loc.T("records")</span>
</form>
```

Search grows (`flex:1 1 220px`), selects are `flex:0 1 180px`, the count is pushed to the far end.
Everything goes full width under 576px.

## 4. Clickable rows

No inline `onclick`. One delegated handler in site.js covers the whole app.

```cshtml
<tr class="row-link" data-href="@Url.Action("Details", new { id = row.Id })">
    <td data-label="@Loc.T("Order #")">
        <a class="identifier" href="@Url.Action("Details", new { id = row.Id })">@row.OrderNumber</a>
    </td>
</tr>
```

Clicks inside `a, button, input, select, textarea, label, .dropdown, [data-no-row-link]` are
ignored, as is a click that ends a text selection. `tabindex="0"` is applied by the script and
Enter activates the row. The primary column keeps a real `<a>` — that's the accessible path,
which is why the row carries no `aria-label`.

## 5. Mobile cards (`.table-card-stack`)

Add `class="table table-card-stack"` to the table and `data-label="Column"` to each `<td>`.
Under 768px each row becomes a card: first cell is the bold, larger card title (no caption), other
cells show their `data-label` as a small muted caption **above** the value, badges align to the
end of their line, and the action cell pins to the card's top-inline-end corner.

```cshtml
<div class="table-responsive">
    <table class="table table-hover align-middle table-card-stack">
        <thead><tr><th>@Loc.T("Asset Tag")</th><th>@Loc.T("Zone")</th><th data-hide-mobile>@Loc.T("Created")</th><th></th></tr></thead>
        <tbody>
        <tr class="row-link" data-href="@Url.Action("Details", new { id = a.Id })">
            <td data-label="@Loc.T("Asset Tag")"><a class="identifier" href="@Url.Action("Details", new { id = a.Id })">@a.AssetTag</a></td>
            <td data-label="@Loc.T("Zone")">@Loc.LocalizedName(a.ZoneName, a.ZoneNameAr)</td>
            <td data-label="@Loc.T("Created")" data-hide-mobile>@a.CreatedAt.ToString("dd/MM/yyyy")</td>
            <td class="row-actions-cell"><!-- kebab --></td>
        </tr>
        </tbody>
    </table>
</div>
```

- `data-hide-mobile` on a `th`/`td` drops that column from the card.
- Empty `<td>`s are hidden automatically (no orphan caption).
- The whole card is tappable via the same `row-link` handler.

## 6. Kebab row actions (admin lists only)

```html
<div class="dropdown row-actions">
    <button class="btn btn-sm btn-outline-secondary" type="button" data-bs-toggle="dropdown"
            aria-expanded="false" aria-label="@Loc.T("Actions")">
        <i class="bi bi-three-dots" aria-hidden="true"></i>
    </button>
    <ul class="dropdown-menu dropdown-menu-end">
        <li><a class="dropdown-item" href="@Url.Action("Edit", new { id })"><i class="bi bi-pencil me-2"></i>@Loc.T("Edit")</a></li>
        <li><hr class="dropdown-divider"></li>
        <li>
            <form method="post" action="@Url.Action("Delete", new { id })" class="m-0">
                @Html.AntiForgeryToken()
                <button type="submit" class="dropdown-item text-danger w-100 text-start" data-confirm="@Loc.T("Are you sure?")">
                    <i class="bi bi-trash me-2"></i>@Loc.T("Delete")
                </button>
            </form>
        </li>
    </ul>
</div>
```

Put the kebab in its own `<td class="row-actions-cell">`. `.table-responsive:has(.row-actions)`
drops overflow clipping so the menu isn't cut off inside the scroll container.

## 7. Forms

```cshtml
<form method="post" class="row g-3 form-grid">
    <div class="col-12 col-md-6">
        <label asp-for="Name" class="form-label required">@Loc.T("Name")</label>
        <input asp-for="Name" class="form-control" />
        <span asp-validation-for="Name" class="text-danger small"></span>
        <div class="help-text">@Loc.T("Shown on every order raised against this asset.")</div>
    </div>
    <div class="col-12">
        <div class="form-actions">
            <button type="submit" class="btn btn-primary">@Loc.T("Save")</button>
            <a href="@Url.Action("Index")" class="btn btn-outline-secondary">@Loc.T("Cancel")</a>
        </div>
    </div>
</form>
```

- `.form-grid` is Bootstrap `row g-3`; fields are `col-12 col-md-6`, textareas `col-12`.
- `label.form-label.required` gets a red `*` automatically — don't type one.
- `.form-actions` is Save (primary) + Cancel (outline), start-aligned; under 576px it becomes a
  sticky bottom bar with safe-area padding.
- `.help-text` for field hints.

## 8. Empty state

```cshtml
<partial name="_EmptyState" model='new EmptyStateModel{
    Icon = "bi-clipboard-x",
    Title = Loc.T("No work orders yet"),
    Text = Loc.T("Work orders raised against your assets will appear here."),
    ActionUrl = Url.Action("Create"), ActionLabel = Loc.T("New Work Order") }' />
```

Use it inside a `<td colspan="N">` for an empty table, or straight in a card body.
`Text`, `ActionUrl` and `ActionLabel` are all optional.

## 9. Role display

Never render `ApplicationRole.Name` raw.

```cshtml
@Loc.Role(user.RoleName, user.RoleNameAr)   @* "OperationsManager" -> "Operations Manager" *@
@Loc.Role(roleName)                         @* no Arabic name available *@
```

Outside a view: `RoleDisplay.Name(roleName, nameAr, lang)` or `RoleDisplay.Humanize(roleName)`
(`Services/RoleDisplay.cs`). Acronyms survive: `"HRManager"` → `"HR Manager"`, `"HR"` → `"HR"`.

## 10. Status colour + label

```cshtml
<partial name="_StatusBadge" model="@row.Status" />   @* chip *@
@Loc.T(StatusStyle.Label(row.Status))                 @* status as plain text *@
```

`StatusStyle.Label(status)` is **the** canonical display label: it returns the spaced English form,
so `"PendingApproval"` and `"Pending Approval"` render and translate identically. Always wrap it in
`Loc.T`. `StatusStyle.For(status)` returns the badge classes and accepts either spelling too.

## 11. Sidebar group heading

The Administration section's heading is computed from `NavPermissionSet`: `"Administration"` when
the user holds any of Users / Permissions / Asset Visibility / Audit Logs, otherwise
`"Reference Data"` (the group then contains nothing but lookup lists). Nothing to do in feature
views — noted here so nobody hard-codes either string.
