# Working on ShiftFlow — Branching & Conflict-Free Workflow

This doc exists so two of us can work on this repo at the same time without
stepping on each other. Read it before you start a new feature.

## 1. Branching model

- `main` is always buildable. Never commit directly to `main`.
- One branch per feature/fix, branched from the latest `main`:
  ```bash
  git checkout main
  git pull
  git checkout -b feature/<short-name>      # e.g. feature/spare-parts-export
  # or: fix/<short-name>, chore/<short-name>
  ```
- Keep branches short-lived (days, not weeks). Small PRs merge faster and
  conflict less.
- Before opening a PR, **always rebase onto latest `main`** rather than
  merging `main` into your branch — keeps history linear and conflicts
  easier to resolve one commit at a time:
  ```bash
  git fetch origin
  git rebase origin/main
  ```

## 2. Where new features live (the part that actually prevents conflicts)

The app follows one controller + one view folder per module — **almost every
feature is fully isolated to its own files**. If you stick to this pattern,
your branch and a coworker's branch will almost never touch the same line.

```
ShiftFlow.Web/Controllers/<Feature>Controller.cs   <- new file per feature
ShiftFlow.Web/Views/<Feature>/Index.cshtml         <- new folder per feature
ShiftFlow.Web/Views/<Feature>/Create.cshtml
ShiftFlow.Web/Views/<Feature>/Edit.cshtml
ShiftFlow.Web/Views/<Feature>/_Form.cshtml         <- shared Create/Edit partial, if needed
```

**Adding a brand-new module** (e.g. a new "Vendors" page) touches *only new
files* — zero conflict risk. Copy the pattern from an existing module closest
to what you're building (e.g. `LocationsController.cs` for simple CRUD,
`AssetsController.cs` for CRUD + KPIs + filters, `ShiftsController.cs` for
CRUD with a join-table relationship).

**Editing an existing module** (e.g. adding a field to Work Orders) — only
touches that module's controller/view files, so conflicts only happen if two
people edit the *same* module at the same time. Coordinate verbally if you
both need to touch `WorkOrdersController.cs` in the same week.

## 3. Shared files — coordinate before editing these

A handful of files are touched by *every* feature and are the actual conflict
risk. Treat changes to these as small and surgical — add your line, don't
reformat or reorder existing lines:

| File | What goes here | How to avoid conflicts |
|---|---|---|
| `ShiftFlow.Web/Views/Shared/_Layout.cshtml` | Sidebar nav links | Add your new `<li>` link at the end of the relevant role's section, don't reorder existing links |
| `ShiftFlow.Web/Localization/Translations.cs` | EN→AR dictionary entries | Append new keys at the **end of the dictionary**, never re-sort it. If you're unsure a key already exists, search before adding — duplicate keys throw a runtime crash on startup (`Dictionary.Add` on an existing key) |
| `ShiftFlow.Web/ViewModels/ViewModels.cs` | New `*ViewModel` classes | Append your new class at the end of the file |
| `ShiftFlow.Web/Program.cs` | DI registrations (`AddScoped<...>`) | Add your line to the existing chained `builder.Services.AddScoped<...>()` statement, append at the end |
| `ShiftFlow.Infrastructure/Data/ApplicationDbContext.cs` | New `DbSet<T>` + `OnModelCreating` config | Add your `DbSet` at the end of the existing list; add your `b.Entity<T>(...)` config at the end of `OnModelCreating` |
| `ShiftFlow.Domain/Entities/` | New entity classes | New file per entity — no conflict risk |

If you need to add a new EF Core migration, **run it last**, right before
opening your PR, after rebasing onto latest `main` — migrations generated
against a stale model will conflict with anyone else's migration.

## 4. Before every push

```bash
dotnet build ShiftFlow.sln     # must succeed with 0 errors
```

Run the app locally and click through whatever you changed — there's no
automated test suite yet, so this is the only safety net.

## 5. Commit messages

Plain, descriptive, present tense. No strict convention enforced, but keep
one logical change per commit:

```
Add spare part low-stock email notification
Fix shift calendar trailing-month shading
```

## 6. Opening a PR

- Push your branch, open a PR against `main`.
- PR description: what changed and why, one line is fine for small changes.
- Self-review the diff before requesting review — catches accidental
  formatting-only changes that bloat the diff and cause needless conflicts
  for whoever rebases after you.
- Squash-merge (or rebase-merge) into `main` — keeps `main`'s history clean
  of WIP commits.

## 7. If you do hit a conflict

Conflicts will almost always be in one of the "shared files" listed in §3,
and almost always a simple "both of us appended a line near each other" —
keep both lines, don't pick one over the other unless they're genuinely the
same change. Re-run `dotnet build` after resolving to confirm nothing broke.
