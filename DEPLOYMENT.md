# ShiftFlow — SmarterASP.NET Deployment Notes

> **No real credentials in this file.** It *is* tracked in git. Every secret below is a
> placeholder (`<LIKE_THIS>`). Real values live only in
> `ShiftFlow.Web/appsettings.Production.json` on the server (uploaded via File Manager/FTP,
> never committed) and in your local `prod-settings.local.txt` — both are git-ignored.
> Never replace a placeholder here with a real value.

## Hosting summary

- **Host:** SmarterASP.NET, account `<HOSTING_ACCOUNT>`
- **Live URL:** `<SITE_URL>`
- **Deploy method:** SmarterASP's "GitHub Deploy" feature (APPS → GitHub Deploy),
  which uses **Railpack** (a Node.js-oriented auto-builder) under the hood.
- **Repo:** `<GIT_REPO_URL>`, branch `main`

## Credentials (placeholders — look the real values up in your password manager)

| System | Where | Username | Password |
|---|---|---|---|
| SmarterASP control panel | https://www.smarterasp.net/login.aspx | `<HOSTING_ACCOUNT>` | `<HOSTING_PASSWORD>` |
| Temp-URL security lock (Basic Auth) | prompted on `ftempurl.com` links | `<BASIC_AUTH_USER>` | `<BASIC_AUTH_PASSWORD>` |
| MSSQL database | Server `<DB_SERVER>`, database `<DB_NAME>` | `<DB_USER>` | `<DB_PASSWORD>` |
| SMTP (outgoing mail) | `<SMTP_HOST>:<SMTP_PORT>` | `<SMTP_USER>` | `<SMTP_PASSWORD>` |
| Entra ID app registration | Azure portal | Client ID `<ENTRA_CLIENT_ID>` (tenant `<ENTRA_TENANT_ID>`) | `<ENTRA_CLIENT_SECRET>` |

Application accounts are **not** listed here. Demo/sample logins are seeded only in the
Development environment (or when `Seed:DemoAccounts` is `true`) — see `DbSeeder`. Production
accounts are created through the Users page and always start with a one-time temporary
password that must be changed on first login.

## Server-side configuration

Upload `appsettings.Production.json` into the site root via File Manager/FTP. IIS apps default
to the `Production` environment, so no environment variable is needed. GitHub Deploy's
"Environment Variables" panel is scoped to the **build container**, not the IIS worker process,
so overrides set there never reach the running app.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=<DB_SERVER>;Database=<DB_NAME>;User Id=<DB_USER>;Password=<DB_PASSWORD>;TrustServerCertificate=True;Encrypt=True;"
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<ENTRA_TENANT_ID>",
    "ClientId": "<ENTRA_CLIENT_ID>",
    "ClientSecret": "<ENTRA_CLIENT_SECRET>",
    "CallbackPath": "/signin-oidc-entra"
  },
  "Smtp": {
    "Host": "<SMTP_HOST>",
    "Port": "587",
    "EnableSsl": "true",
    "User": "<SMTP_USER>",
    "Password": "<SMTP_PASSWORD>",
    "From": "<SMTP_FROM>"
  }
}
```

`ShiftFlow.Web/web.config` sets `stdoutLogEnabled="true"` so IIS writes real startup exceptions
to `logs/stdout_*.log` under the site root (visible via File Manager) instead of only the
generic ANCM 500.30 page.

## Why deployment wasn't a simple `git push`

SmarterASP's GitHub Deploy pipeline only supports **single-project** repos — it has no "root
directory" setting and doesn't reliably honor a custom `Dockerfile`. Three real blockers had to
be fixed:

### 1. Multi-project solution → merged into one project
The original solution was `ShiftFlow.Web` + `ShiftFlow.Application` + `ShiftFlow.Domain` +
`ShiftFlow.Infrastructure` (separate `.csproj` files with `ProjectReference`s). Railpack only
copied the `ShiftFlow.Web` folder into its build context, so the sibling projects' types
couldn't resolve — every build failed with `CS0234`/`CS0246`.

**Fix:** merged all three library projects' source files into `ShiftFlow.Web` as subfolders
(`Domain/Entities`, `Application/Services`, `Application/AI`, `Infrastructure/Data`,
`Infrastructure/Migrations`), consolidated all NuGet package references into one `.csproj`,
deleted the now-empty library projects, and updated `ShiftFlow.sln`.

### 2. LocalDB connection string → real SQL Server, via file not env var
`appsettings.json` ships with a local dev connection string that only exists on a dev machine.
The app calls `db.Database.MigrateAsync()` on startup (`DbSeeder.SeedAsync`), so it crashed
immediately in production (HTTP 500.30). Fixed by creating the database under DATABASES → MSSQL
and uploading `appsettings.Production.json` as described above.

### 3. Broken migration history → regenerated
The `InitialCreate` migration's `Up()` method was empty (a leftover "baseline: database already
exists" placeholder), so it never created `AspNetRoles`/`AspNetUsers`/any base table. Invisible
locally because the dev database already had the tables. Two schema bugs surfaced alongside it:
multiple cascading FK paths from the same table to `AspNetUsers`, which SQL Server rejects.

**Fix:** deleted the old migration files, changed the conflicting FKs to
`DeleteBehavior.Restrict` in `ApplicationDbContext`, and regenerated a single clean
`InitialCreate` migration containing the full real schema. Verified end-to-end against a
brand-new empty database before pushing.

## Redeploy checklist

1. `git push origin main` (GitHub Deploy auto-builds on push, or trigger it manually).
2. Watch the build log in SmarterASP → APPS → GitHub Deploy → Deployments.
3. If the connection string or any secret changes, edit `appsettings.Production.json` directly
   via File Manager/FTP — don't rely on the Environment Variables panel for this app.
4. If the app 500s after a deploy, check `logs/stdout_*.log` in File Manager under the site
   root — that's the real exception, not just the generic IIS error page.
5. If you add new EF Core migrations, run `dotnet ef database update` locally against a
   **fresh** empty database first to catch schema issues before they hit production.
6. After deploying, recycle the app pool from WEBSITES → Server Overview → "Pool" if the site
   doesn't pick up the change immediately.
