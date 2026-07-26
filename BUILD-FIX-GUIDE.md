# ShiftFlow — Build Fix Guide

## Root Cause Analysis

The cascading build failure has **one root cause**: the **ShiftFlow.Domain** project
references `Microsoft.AspNetCore.Identity` (via `ApplicationUser : IdentityUser`)
but its `.csproj` file had **no NuGet package** providing that namespace.

### Error Chain

```
1. Domain fails to compile  →  "IdentityUser could not be found"
2. Domain.dll never produced →  "Metadata file ...ShiftFlow.Domain.dll could not be found"
3. Infrastructure references Domain → cascading metadata error
4. Application references both    → cascading metadata error
```

### Why only Domain is affected

| Project        | Uses Identity? | Had the package? | Result          |
|----------------|----------------|-------------------|-----------------|
| Domain         | Yes (`IdentityUser`) | ❌ No        | **FAILS**       |
| Infrastructure | Yes (`IdentityDbContext`, `UserManager`) | ✅ `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | OK |
| Application    | No (uses Domain types only) | N/A (transitive) | OK |
| Web            | Yes (full ASP.NET Core) | ✅ `FrameworkReference Microsoft.AspNetCore.App` (built-in via Web SDK) | OK |

---

## 1. Package References — Which Goes Where

### ShiftFlow.Domain (THE FIX)

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Identity.Core" Version="8.0.0" />
</ItemGroup>
```

**Why this package?** `Microsoft.Extensions.Identity.Core` provides the `IdentityUser`
base class and nothing else — no Entity Framework, no ASP.NET Core runtime.
This is the **lightest possible** reference for a Clean Architecture domain layer.

> **Clean Architecture note:** Strictly speaking, the Domain layer should be
> framework-free. If you want to go fully purist, extract `ApplicationUser` into
> Infrastructure and use a plain `AppUser` POCO in Domain with an `IUser` interface.
> For pragmatism (and because EF navigation properties need the concrete type),
> `Microsoft.Extensions.Identity.Core` is the accepted compromise.

### ShiftFlow.Infrastructure (already correct — no changes needed)

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="8.0.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
</ItemGroup>
```

### ShiftFlow.Application (already correct — no changes needed)

No Identity packages — it references Domain and Infrastructure transitively.

---

## 2. dotnet CLI Commands

### Option A — Restore from the fixed .csproj (recommended)

The Domain .csproj in this ZIP already includes the fix. Just restore:

```bash
cd ShiftFlow
dotnet restore
```

### Option B — Manual install (if you're patching an existing solution)

```bash
# Add the missing package to Domain ONLY
cd ShiftFlow.Domain
dotnet add package Microsoft.Extensions.Identity.Core --version 8.0.0
cd ..

# Verify Infrastructure already has it (should already be present)
cd ShiftFlow.Infrastructure
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 8.0.0
cd ..
```

---

## 3. Clean, Restore, Rebuild — Step by Step

```bash
# Step 1: Delete all build artifacts (bin/obj folders)
# On Windows (PowerShell):
Get-ChildItem -Path . -Recurse -Include bin,obj -Directory | Remove-Item -Recurse -Force

# On macOS/Linux:
find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +

# Step 2: Restore all NuGet packages
dotnet restore

# Step 3: Build the entire solution
dotnet build --no-restore

# Step 4 (optional): Run with database migration
cd ShiftFlow.Web
dotnet ef database update
dotnet run
```

### If errors persist after the above

1. **Clear NuGet cache:**
   ```bash
   dotnet nuget locals all --clear
   dotnet restore
   ```

2. **Build in dependency order to isolate failures:**
   ```bash
   dotnet build ShiftFlow.Domain/ShiftFlow.Domain.csproj
   dotnet build ShiftFlow.Infrastructure/ShiftFlow.Infrastructure.csproj
   dotnet build ShiftFlow.Application/ShiftFlow.Application.csproj
   dotnet build ShiftFlow.Web/ShiftFlow.Web.csproj
   ```
   The first project that fails is your root cause — fix it before moving on.

3. **Verify the SDK version:**
   ```bash
   dotnet --version   # Must be 8.0.x
   ```
   If not, install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0

---

## Summary of Changes in This ZIP

| File | Change |
|------|--------|
| `ShiftFlow.Domain/ShiftFlow.Domain.csproj` | Added `Microsoft.Extensions.Identity.Core` 8.0.0 |
| `BUILD-FIX-GUIDE.md` | This document |

All other files are unchanged from the original generation.
