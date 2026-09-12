# NOTES-admin.md — admin UI pass (branch `ui/ui-admin`)

## Temporary stub the merger must delete

`ShiftFlow.Web/ViewModels/_UiStubs.TEMP.cs` — **delete this file on merge.** It exists only so this
branch builds against shared APIs that live on the parallel shared-UI branch:

* `PageHeaderModel.Breadcrumbs` (`List<(string Label, string? Url)>?`)
* `EmptyStateModel` (`Icon`, `Title`, `Text`, `ActionUrl`, `ActionLabel`)
* `ShiftFlow.Web.Services.RoleDisplay.Name(roleName, nameAr, lang)`

Shapes match the agreed API exactly, so the real implementations drop straight in.

**One change outside the owned file list:** `ShiftFlow.Web/ViewModels/ViewModels.cs` line 41,
`public class PageHeaderModel` → `public partial class PageHeaderModel`. That single keyword is
what lets the stub add `Breadcrumbs` without touching the real class. If the shared branch adds
`Breadcrumbs` directly to `PageHeaderModel`, drop the `partial` keyword again along with the stub.

## Shared-layer pieces this branch consumes but does not provide

These are coded against per the agreed API; nothing here works until the shared branch lands them.

| Needed | Used by |
| --- | --- |
| `Views/Shared/_EmptyState.cshtml` | Users/Index, Vendors/Index, Groups/Index, UserAssetScopes/Index — **without it these pages throw at runtime when the list is empty** |
| `_PageHeader` rendering `Model.Breadcrumbs` | Users/Create, Users/Edit, Users/Profile, Rbac/RolePermissions, Rbac/UserPermissions, Vendors/Details, Vendors/Edit, Groups/Create, Groups/Edit, Groups/Details, UserAssetScopes/Create, Edit, CreateGroup, EditGroup |
| `site.js` delegation for `tr.row-link[data-href]` (click + Enter, ignoring clicks inside links/buttons/dropdowns) | every list table in this pass — the old inline `onclick="location.href=…"` handlers were removed |
| `site.css` `.filter-bar`, `.filter-bar__count`, `.row-actions`, `.form-actions`, `.help-text`, `.form-label.required`, `td[data-hide-mobile]` | all list and form views in this pass |
| `Services/RoleDisplay.cs` | Users/Index, Users/Create, Users/Profile, Rbac/Index, Rbac/RolePermissions, Rbac/UserPermissions |

`.table-card-stack` and `td[data-label]` already exist in `site.css`, so mobile cards work today.
`data-confirm` delegation already exists in `site.js`, so every confirm in this pass works today.

**Sticky save bar:** `Rbac/RolePermissions` and `Rbac/UserPermissions` use
`class="form-actions form-actions--sticky"` *plus* an inline `position:sticky;bottom:0`, so the
Save bar sticks even before `.form-actions--sticky` exists in `site.css`. Once the shared rule
lands, the inline style can be removed.

## Controller changes (view-model / query-string only)

* `UsersController.Index` / `ExportExcel` / `FilteredUsersQuery` — added a `status` query
  parameter (`Active` / `Inactive`, filtered in SQL) and `ViewBag.StatusFilter`. No change to
  authorization or to what the export contains beyond honouring the same filter the list shows.
* `VendorsController.Index` — added `search` (name / contact / email, `EF.Functions.Like`) and
  `status` query parameters plus `ViewBag.SearchFilter` / `StatusFilter` / `TotalCount`.
  Paging and authorization unchanged.

## Behaviour notes worth a second look at merge

* **Users/Index kebab.** The five icon buttons became one kebab: Profile (User.View),
  Edit (User.Manage, hidden for Vendor accounts), Permissions (Rbac.Manage), Asset scope
  (User.Manage), divider, Deactivate/Activate and Delete (User.Manage, not on your own row).
  Same permission checks as before. One deliberate difference: the old page rendered a *disabled*
  delete button on your own row; the kebab simply omits the destructive section there.
* **Asset scope** links to the Asset Visibility index rather than a prefilled Create — the
  `UserAssetScopes.Create` GET action takes no `userId`, and wiring one would be a behaviour
  change beyond this pass.
* **Vendors/Details "More" menu** links to the Portal login card (`#portalLogin`) instead of
  duplicating the two POST forms in the dropdown. The forms themselves are untouched.
* **Rbac/Index bulk bar** already appeared only once a user is checked (`#bulkBar.d-none` toggled
  by `refreshBulk`) — verified, left as-is. Its filters moved out of the card header into the
  standard `.filter-bar`, and the per-user shield button now reads
  `<i class="bi bi-shield-lock"></i><span class="d-none d-sm-inline">Permissions</span>`.
* **Rbac/RolePermissions "Toggle all"** is now rendered in the card header by Razor
  (`[data-toggle-all]`) and bound by the page script, instead of the script creating a
  `float-end` button on every `.card` on the page. Same toggle behaviour.
* `Views/Users/MyOrders.cshtml` and `MyHistory.cshtml` were out of scope and are untouched.

## Verification

* `dotnet build ShiftFlow.Web/ShiftFlow.Web.csproj` → **0 errors**, 1 pre-existing warning
  (`ContractsController.cs(188)`). Razor views are compiled by the build in this project
  (confirmed with a deliberate bad symbol), so every `.cshtml` in this pass is compile-checked.
* `node --check` clean on `wwwroot/js/ai-assistant.js`, `wwwroot/js/group-member-picker.js`
  (neither needed changes) and on the inline script in `Rbac/RolePermissions.cshtml`.
