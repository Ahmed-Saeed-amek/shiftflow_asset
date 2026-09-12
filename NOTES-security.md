# Security / configuration review — follow-up notes (branch `fix/security-config`)

## 1. Manual owner action: git-history purge + credential rotation

`git rm --cached "prod settings.txt"` removes the file from the index going forward. **It does not
remove it from git history** — every past commit still contains the real values, and so does every
clone and the GitHub remote. Purging history (`git filter-repo`, or BFG) and force-pushing is a
destructive, repo-wide operation that has to be coordinated with everyone holding a clone, so it is
deliberately left to the owner rather than done from a feature branch.

Until that happens, and regardless of it, **every credential below must be rotated** — it has been
committed in plaintext and shared through chat/tooling. Listed by kind; no values here.

| Kind | Where it was exposed | Rotate by |
|---|---|---|
| Production MSSQL login password (`db_..._admin`) | `DEPLOYMENT.md`, `prod settings.txt` | SmarterASP → DATABASES → MSSQL, then update `appsettings.Production.json` |
| Entra ID (Azure AD) app client secret | `prod settings.txt` | Azure portal → App registrations → Certificates & secrets → new secret, delete old |
| SMTP mailbox password / Gmail app password | `prod settings.txt` | Provider account security page; revoke the old app password |
| Temp-URL Basic Auth username + password | `DEPLOYMENT.md` | SmarterASP site security settings |
| SmarterASP control-panel account password | referenced in `DEPLOYMENT.md` | SmarterASP account settings |
| Demo application accounts (admin/manager/engineer/hr/vendor) | `DEPLOYMENT.md`, `README.md`, `DbSeeder` | No longer seeded outside Development. On any environment where they were already created, change the password or delete the accounts. They now also carry `must_change_password`, but only for newly seeded ones. |
| Local dev SQL `sa` password in `appsettings.json` | still committed (see below) | see §2 |

## 2. `appsettings.json` — what was and wasn't changed

- SMTP `User`/`From` were personal mailbox addresses; replaced with `no-reply@example.com`.
- `Seed:DemoAccounts` added (default `false`).
- **Left alone:** `ConnectionStrings:DefaultConnection` still contains a local dev SQL `sa`
  password. Replacing it would have broken the shared local dev database that other agents and
  the running app depend on this round. Recommended follow-up for the owner: move it to
  `dotnet user-secrets` (the project already has a `UserSecretsId`) or
  `appsettings.Development.json` (git-ignored), leave a placeholder in `appsettings.json`, and
  rotate the local `sa` password.

## 3. Files not owned this round — changes described instead of made

| File | Needed change |
|---|---|
| `ShiftFlow.Web/Localization/Translations.cs` | Append every row from `TRANSLATIONS-security.md`. Until then those strings render in English in the Arabic UI. |
| `ShiftFlow.Web/Controllers/ContractsController.cs` (`DownloadAttachment`, `DeleteAttachment`) and `ShiftFlow.Web/Controllers/WorkOrdersController.cs` (`DownloadAttachment`) | `ContractAttachmentStorage.ResolvePhysicalPath` / `WorkOrderAttachmentStorage.ResolvePhysicalPath` now return `string?` (null when the resolved path would escape `App_Data/uploads`). The existing `File.Exists(path)` guards already make this safe — `File.Exists(null)` is `false` — so nothing breaks, but an explicit `if (path is null) return NotFound();` would read better. |
| `ShiftFlow.Web/Views/Shared/_Layout.cshtml` | No change required: the sidebar already gates My Home / My Orders on `MyWork.View`, which the controllers now enforce too. |

## 4. No schema changes

Nothing in this round needed a migration. `Seed:DemoAccounts` is configuration, not schema, and
the `must_change_password` claim uses the existing `AspNetUserClaims` table.

## 5. Permission grants — MyWork.View

`MyWork.View` is now enforced on `UsersController.MyOrders`/`MyHistory` and `MyHomeController.Index`.
No seeding change was needed: the `DbSeeder` matrix already grants it to OperationsManager,
Supervisor, Section Head, Senior Engineer, Engineer, Operation Engineer and Technician. Admin is
covered by `System.IsAdmin` (which short-circuits every check). HR deliberately does not hold it and
already lands on `/Users` (see `AccountController.BuildLandingPath`). Vendors are redirected to
`/VendorPortal` before the permission check runs, so the redirect still works for them.

## 6. `Rbac/RolePermissions.cshtml` — "Locations" skip removed

The `if (group.Key == "Locations") continue;` guard was dead: no permission in
`PermissionCatalog.All` or in `DbSeeder`'s catalog has category `"Locations"`. `Location.Manage`
was retired and `DbSeeder` actively deletes its rows on every boot. Removed.

## 7. Known gaps / not done

- The remaining Playwright specs still hardcode `BASE_URL` and the demo `Admin@123456` password
  inline rather than using the new `baseURL` in `playwright.config.js`. They will only pass against
  a Development instance that has demo accounts seeded.
- `tests/rbac-all-permissions.spec.js` was pruned to the real `PermissionCatalog`, but its
  round-trip helper still depends on demo accounts existing.
- CSP still allows `'unsafe-inline'` for scripts and styles (pre-existing; needs nonce plumbing
  through every view).
