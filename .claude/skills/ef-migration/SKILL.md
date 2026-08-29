---
name: ef-migration
description: Create, apply, or inspect EF Core migrations for ShiftFlow's ApplicationDbContext. Use when the user asks to add a migration, update the database schema, run `dotnet ef`, or debug a "pending migration" / schema-drift error.
disable-model-invocation: true
---

# EF Core Migration Helper

Wraps `dotnet ef` for this repo's single EF Core project. Despite the README's Clean
Architecture diagram, migrations, the `DbContext`, and `DbSeeder` all actually live under
`ShiftFlow.Web/Infrastructure/` — there is no separate `ShiftFlow.Infrastructure` project.
Both `--project` and `--startup-project` should point at `ShiftFlow.Web/ShiftFlow.Web.csproj`.

## Commands (run from the repo root)

Add a new migration after changing an entity or `OnModelCreating`:
```bash
dotnet ef migrations add <PascalCaseName> --project ShiftFlow.Web/ShiftFlow.Web.csproj --startup-project ShiftFlow.Web/ShiftFlow.Web.csproj
```

List migrations and see what's pending against the currently configured connection:
```bash
dotnet ef migrations list --project ShiftFlow.Web/ShiftFlow.Web.csproj --startup-project ShiftFlow.Web/ShiftFlow.Web.csproj
```

Apply pending migrations by hand (normally not needed — `DbSeeder.SeedAsync` calls
`db.Database.MigrateAsync()` automatically on app startup):
```bash
dotnet ef database update --project ShiftFlow.Web/ShiftFlow.Web.csproj --startup-project ShiftFlow.Web/ShiftFlow.Web.csproj
```

Remove the last (not-yet-applied) migration if you need to redo it:
```bash
dotnet ef migrations remove --project ShiftFlow.Web/ShiftFlow.Web.csproj --startup-project ShiftFlow.Web/ShiftFlow.Web.csproj
```

## Which connection string is actually used

`ASPNETCORE_ENVIRONMENT=Development` is set in `launchSettings.json`, so **.NET user
secrets override `appsettings.Development.json`/`appsettings.json`** for
`ConnectionStrings:DefaultConnection`. Check the real target before assuming:
```bash
dotnet user-secrets list --project ShiftFlow.Web/ShiftFlow.Web.csproj | grep -i connection
```
`appsettings.json` currently says `ShiftFlowAssetsDB4`, but the live dev DB used at runtime
may be a different name (e.g. `ShiftFlowDB2`) — always verify via user secrets, not the
checked-in appsettings file, before running raw SQL against "the" dev database.

## Schema drift (known issue in this repo)

This project's dev database has previously drifted from its migration history — tables or
columns referenced by a migration (e.g. `Locations`, `AspNetUsers.LocationId`) were removed
manually with raw SQL outside of `dotnet ef`, while `__EFMigrationsHistory` still listed the
migration that created them as applied. This makes `dotnet ef database update` fail with
errors like "invalid table" or "invalid column name" on a migration that looks unrelated to
the actual missing object.

If you hit this:
1. Run `dotnet ef migrations list` against the live connection to see the true applied/pending split.
2. Open the failing migration's `Up()` method and identify exactly which `CreateTable`/`AddColumn`/`ForeignKey` call is failing.
3. Check whether the referenced table/column physically exists (`sys.tables` / `sys.columns` via `sqlcmd`).
4. If it's missing, recreate just that object to match the migration's definition (don't re-run the whole migration), then retry `database update`. If a column/table already exists but the migration tries to (re)create it, insert a row into `__EFMigrationsHistory` for that migration instead of editing the migration file.
5. Never edit or delete a migration file that has already shipped to other environments — patch the live database to match it instead.
