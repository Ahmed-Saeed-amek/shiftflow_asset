# ShiftFlow — SmarterASP.NET Deployment Notes

> **⚠️ Contains real credentials.** This file is for your own reference only.
> It is **not** committed to git (see `.gitignore`) — do not remove that
> exclusion, and never paste this file's contents into a public place.

## Hosting summary

- **Host:** SmarterASP.NET, account `ahmedamek-001`
- **Live URL:** https://ahmedamek-001-site1.ftempurl.com
- **Deploy method:** SmarterASP's "GitHub Deploy" feature (APPS → GitHub Deploy),
  which uses **Railpack** (a Node.js-oriented auto-builder) under the hood.
- **Repo:** https://github.com/Ahmed-Saeed-amek/shiftflow.git, branch `main`

## Login credentials

| System | URL | Username | Password |
|---|---|---|---|
| SmarterASP control panel | https://www.smarterasp.net/login.aspx (Customer Login) | (your account login) | (your account password) |
| Temp-URL security lock (Basic Auth) | prompted on `ftempurl.com` links | `11320364` | `60-dayfreetrial` |
| MSSQL database | Server: `SQL5101.site4now.net` | `db_acbc2c_ahmedamek_admin` | `Infernus2000` |
| App demo account — Admin | in-app login | `admin@shiftflow.com` | `Admin@123456` |
| App demo account — Manager | in-app login | `manager@shiftflow.com` | `Manager@123456` |
| App demo account — Engineer | in-app login | `engineer@shiftflow.com` | `Engineer@123456` |
| App demo account — HR | in-app login | `hr@shiftflow.com` | `HR@123456` |

Database name: `db_acbc2c_ahmedamek`

**Recommendation:** rotate the SQL password and demo-account passwords once
you're done testing — they've been shared in plaintext across chat/tooling
during setup.

## Why deployment wasn't a simple `git push`

SmarterASP's GitHub Deploy pipeline only supports **single-project** repos —
it has no "root directory" setting and doesn't reliably honor a custom
`Dockerfile`. Three real blockers had to be fixed:

### 1. Multi-project solution → merged into one project
The original solution was `ShiftFlow.Web` + `ShiftFlow.Application` +
`ShiftFlow.Domain` + `ShiftFlow.Infrastructure` (separate `.csproj` files with
`ProjectReference`s). Railpack only copied the `ShiftFlow.Web` folder into its
build context, so the sibling projects' types (`ApplicationDbContext`,
`ApplicationUser`, all services, etc.) couldn't resolve — every build failed
with `CS0234`/`CS0246`.

**Fix:** merged all three library projects' source files into `ShiftFlow.Web`
as subfolders (`Domain/Entities`, `Application/Services`, `Application/AI`,
`Infrastructure/Data`, `Infrastructure/Migrations`), consolidated all NuGet
package references into one `.csproj`, deleted the now-empty library
projects, and updated `ShiftFlow.sln`.

### 2. LocalDB connection string → real SQL Server, via file not env var
`appsettings.json` ships with `(localdb)\MSSQLLocalDB`, which only exists on a
dev machine. The app calls `db.Database.MigrateAsync()` on startup
(`DbSeeder.SeedAsync`), so it crashed immediately in production (HTTP 500.30).

GitHub Deploy's "Environment Variables" panel turned out to be scoped to the
**build container**, not the actual IIS worker process that ends up serving
the app (confirmed by the `Microsoft-IIS/10.0` response header and the
ANCM-specific 500.30 error page) — so environment-variable overrides there
never reached the running app.

**Fix:** created a database under DATABASES → MSSQL, then uploaded
`appsettings.Production.json` **directly via SmarterASP's File Manager** (FTP)
into the site's root folder, containing just the real connection string. IIS
apps default to the `Production` environment automatically, so no environment
variable is needed for this file to be picked up. This file is **not** in git
— it's server-side only.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=SQL5101.site4now.net;Database=db_acbc2c_ahmedamek;User Id=db_acbc2c_ahmedamek_admin;Password=Infernus2000;TrustServerCertificate=True;Encrypt=True;"
  }
}
```

Also added `ShiftFlow.Web/web.config` with `stdoutLogEnabled="true"` so IIS
writes real startup exceptions to `logs/stdout_*.log` on the server (visible
via File Manager) — this is what let us diagnose both this and the next
issue, instead of only ever seeing the generic 500.30 page.

### 3. Broken migration history → regenerated
Once the DB connected, migrations still failed. The root cause: the
`InitialCreate` migration's `Up()` method was **empty** (a leftover
"baseline: database already exists, no-op" placeholder from early
development against an already-manually-created local DB) — it never
actually created `AspNetRoles`, `AspNetUsers`, or any base table. This was
invisible locally because the dev LocalDB already had all the tables from
before migrations existed. On a genuinely fresh database, every later
migration that referenced those tables failed.

Along the way, two more real schema bugs surfaced (both would have hit any
fresh database, not just this one):
- `LeaveRequests` had two foreign keys to `AspNetUsers` (`EngineerId`
  cascade-delete by convention, `ApprovedByUserId` explicit `SetNull`) — SQL
  Server rejects two cascading paths from the same table to the same target.
- Same issue on `WorkOrders` (`AssignedEngineerId` / `AssignedByUserId`) and
  `SafetyPermits` (`RequestedByUserId` implicit cascade / `SafetyOfficerId`
  `SetNull`).

**Fix:** deleted all 21 old migration files, changed the conflicting FKs to
`DeleteBehavior.Restrict` in `ApplicationDbContext.cs`, and regenerated a
single clean `InitialCreate` migration containing the full real schema.
Verified locally end-to-end against a brand-new LocalDB database before
pushing.

## Redeploy checklist

1. `git push origin main` (GitHub Deploy auto-builds on push, or trigger
   manually from the GitHub Deploy screen).
2. Watch the build log in SmarterASP → APPS → GitHub Deploy → Deployments.
3. If you ever change the connection string or add new secrets, edit
   `appsettings.Production.json` directly via File Manager/FTP — don't rely
   on the Environment Variables panel for this app.
4. If the app 500s after a deploy, check `logs/stdout_*.log` in File Manager
   under the site root — that's the real exception, not just the generic
   IIS error page.
5. If you add new EF Core migrations, run `dotnet ef database update`
   locally against a **fresh** empty database first to catch any schema
   issues before they hit production.
6. After deploying, recycle the app pool from WEBSITES → Server Overview →
   "Pool" (circular-arrow button) if the site doesn't pick up the change
   immediately.
