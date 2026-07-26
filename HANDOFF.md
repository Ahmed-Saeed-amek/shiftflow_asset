# ShiftFlow – Session Handoff

**Date:** 2026-06-30  
**Project:** `C:\Users\Ahmed\Desktop\ahmed\solution 1`  
**Stack:** ASP.NET Core 8 MVC, EF Core, SQL Server LocalDB, Bootstrap 5, Razor Runtime Compilation

---

## 1. Open Bug to Fix First

**Calendar view on Schedule Details page shows all groups as "–" (Off) instead of their actual shifts.**

### What is confirmed:
- `DailyGroupShift` rows **exist** in the DB — schedule 2031 has **150 rows** (5 groups × 30 days).
- Data reaches the Razor view correctly: debug div shows `calPlan groups=5, total rows=150`.
- The **date dictionary lookup fails** — `dayMap.TryGetValue(d.Date, out shift)` never matches.
- Root cause: likely `DateTime.Kind` mismatch between DB-returned dates and view-generated dates.

### The fix (not yet applied):

Change the inner dictionary key from `DateTime` to `string` to eliminate all Kind ambiguity.

**`ShiftFlow.Web/Controllers/ShiftMakerController.cs` — Details action (~line 138):**
```csharp
// CURRENT (broken):
g => g.ToDictionary(d => d.Date.Date, d => d.ShiftType));
ViewBag.GroupShiftLookup = groupShiftLookup;

// REPLACE WITH:
g => g.ToDictionary(d => d.Date.ToString("yyyy-MM-dd"), d => d.ShiftType));
ViewBag.GroupShiftLookup = groupShiftLookup;
```
So the full block becomes:
```csharp
var groupShiftLookup = rawPlan
    .GroupBy(d => d.ShiftGroupId)
    .ToDictionary(
        g => g.Key,
        g => g.ToDictionary(d => d.Date.ToString("yyyy-MM-dd"), d => d.ShiftType));
ViewBag.GroupShiftLookup = groupShiftLookup;
```

**`ShiftFlow.Web/Views/ShiftMaker/Details.cshtml` — calendar section (~line 295):**
```csharp
// CURRENT (broken):
var calPlan = ViewBag.GroupShiftLookup as Dictionary<int, Dictionary<DateTime, string>>
              ?? new Dictionary<int, Dictionary<DateTime, string>>();

// REPLACE WITH:
var calPlan = ViewBag.GroupShiftLookup as Dictionary<int, Dictionary<string, string>>
              ?? new Dictionary<int, Dictionary<string, string>>();
```

**Same file — inside the day/group loop (~line 350):**
```csharp
// CURRENT (broken):
string? shift = null;
if (calPlan.TryGetValue(g.Id, out var dayMap))
    dayMap.TryGetValue(d.Date, out shift);
shift ??= "Off";

// REPLACE WITH:
string? shift = null;
if (calPlan.TryGetValue(g.Id, out var dayMap))
    dayMap.TryGetValue(d.ToString("yyyy-MM-dd"), out shift);
shift ??= "Off";
```

Also **remove the debug div** block (~lines 298–310) once the calendar works:
```csharp
@{
    var firstGroupId = calPlan.Keys.FirstOrDefault();
    ...
}
<div class="alert alert-info ...">DEBUG: ...</div>
```

---

## 2. Database State

```powershell
# Query the DB directly:
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShiftFlowDB -Q "YOUR QUERY"
```

### Schedules and their DailyGroupShift row counts:
```
Id    Name         RotationTemplateId  WorkAreaId  Status     DgsRows
2031  test233      8                   1003        Generated  150
2030  test2        7                   NULL        Generated  960
2029  gv2          6                   1003        Generated  150
...
1019  2026         1                   1           Generated  1975
```

### Sample DailyGroupShift rows (schedule 2031):
```
ShiftGroupId  GroupName  Date                        ShiftType
1013          A          2026-06-30 00:00:00.000     Morning
1014          B          2026-06-30 00:00:00.000     Evening
1015          C          2026-06-30 00:00:00.000     Night
1023          D          2026-06-30 00:00:00.000     Off
1024          F          2026-06-30 00:00:00.000     Off
1013          A          2026-07-01 00:00:00.000     Off
1014          B          2026-07-01 00:00:00.000     Morning
...
```

---

## 3. What Was Completed This Session

### 3a. Razor compilation fix ✅
`Details.cshtml` had `string[] groupNames = ["A","B","C","D","F"]` (C# 12 syntax). Razor compiler rejects it. Fixed to `new[] { "A", "B", "C", "D", "F" }`.

### 3b. Custom rotation on schedule creation ✅
**Problem:** `CreateScheduleAsync` ignored submitted rotation JSON; always used default A/B/C/D/F rotation.  
**Fix:** Controller Create POST now builds a `RotationTemplate` entity from `vm.RotationDays` directly, saves it, then calls `CreateFromTemplateAsync` (attaches template to schedule) and immediately `FillFromRotationAsync` (writes DailyGroupShift rows).

Key code in `ShiftMakerController.cs` Create POST (~lines 65–101):
```csharp
var template = new RotationTemplate
{
    Name = $"{vm.Name} (auto)",
    Days = vm.RotationDays.Select(d => new RotationTemplateDay
    {
        DayNumber        = d.DayNumber,
        MorningGroupName = d.MorningGroup,
        EveningGroupName = d.EveningGroup,
        NightGroupName   = d.NightGroup,
    }).ToList(),
    ...
};
_db.RotationTemplates.Add(template);
await _db.SaveChangesAsync();
templateId = template.Id;

var schedule = await _svc.CreateFromTemplateAsync(vm.Name, templateId, ...);
await _svc.FillFromRotationAsync(schedule.Id, CurrentUserId);
await _svc.GenerateAssignmentsAsync(schedule.Id, CurrentUserId);
```

### 3c. One-group-per-shift-per-day enforcement ✅
- `ShiftScheduleCreateVm` implements `IValidatableObject` — server-side: no duplicate groups per day, all 3 shifts must be filled.
- `RotationDays` starts with blank group names (no pre-populated defaults).
- Create view: `— pick —` placeholder is `disabled` so cannot be re-selected.
- Create view JS: `syncRow(i)` disables already-chosen groups in sibling dropdowns in real time.
- Create view: submit guard blocks form if any row is incomplete or has duplicate groups.

### 3d. Work Areas page ✅
- Removed "Number of Groups" input (always 5, hardcoded in service).
- Added per-area collapsible coordinate edit panel (Bootstrap collapse, `data-bs-toggle`).
- `UpdateAreaCoordinates` POST action added to controller.
- Auto-picks next unused color from a 10-color palette for new areas.

### 3e. Calendar view — data wiring (partial) ⚠️
- Removed hardcoded `GetRotationShift` / `DefaultRotation` logic from `Details.cshtml`.
- Controller now loads all `DailyGroupShift` rows for the schedule and passes as `ViewBag.GroupShiftLookup`.
- **Date key lookup fails** — see Section 1 for the fix.

### 3f. Regenerate Rotation button ✅
- `RegenerateRotation` POST action added to `ShiftMakerController.cs`.
- Button added to `Details.cshtml` next to "Edit Planner" (visible to schedulers when status = Draft).
- Calls `_svc.FillFromRotationAsync(id, userId)` — repopulates DailyGroupShift for old schedules that were created before the fix.

---

## 4. Key Domain Rules

| Thing | Value |
|---|---|
| Group names | A, B, C, D, F — no E, always 5 per area |
| Task status for handover | `"HandedOver"` — NOT "RolledOver", NOT "Handover" |
| Shift types | `"Morning"`, `"Evening"`, `"Night"`, `"Off"` |
| Schedule status flow | Draft → Published → Archived |
| Rotation cycle | 5 days (DayNumber 1–5), repeating |

---

## 5. Entity Relationships (relevant subset)

```
WorkArea
  └── Groups: List<ShiftGroup> (always 5 per area, names A/B/C/D/F)

ShiftSchedule
  ├── WorkAreaId (FK, nullable)
  ├── RotationTemplateId (FK, nullable)
  ├── StartDate / EndDate
  ├── StartRotationDay (1–5, which rotation day to start on)
  ├── GroupShifts: List<DailyGroupShift>    ← the actual per-day schedule
  └── Overrides: List<ShiftOverride>        ← manual corrections

RotationTemplate
  └── Days: List<RotationTemplateDay>       ← 5 entries, one per rotation day
        ├── DayNumber (1–5)
        ├── MorningGroupName ("A"/"B"/etc.)
        ├── EveningGroupName
        └── NightGroupName

DailyGroupShift                            ← THE CORE TABLE
  ├── ShiftScheduleId
  ├── ShiftGroupId
  ├── Date (midnight, one row per group per calendar day)
  └── ShiftType ("Morning"|"Evening"|"Night"|"Off")
```

---

## 6. Service Methods

| Method | What it does |
|---|---|
| `CreateFromTemplateAsync(...)` | Creates `ShiftSchedule` record only. Does NOT fill DailyGroupShift. |
| `FillFromRotationAsync(scheduleId, userId)` | Reads `RotationTemplate.Days`, matches group names, writes all `DailyGroupShift` rows. Falls back to `DefaultRotation` (A→B→C→D→F) if no template. |
| `GenerateAssignmentsAsync(scheduleId, userId)` | Creates `ShiftAssignment` rows by matching group members to their DailyGroupShift slots. |
| `BuildRotation(template)` | Returns `Dictionary<int, (string M, string E, string N)>` for days 1–5. |

---

## 7. Running the App

```powershell
# 1. Stop any running instance
Get-Process ShiftFlow.Web -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Build
cd "C:\Users\Ahmed\Desktop\ahmed\solution 1"
dotnet build ShiftFlow.Web --no-restore -c Debug

# 3. Run
cd "C:\Users\Ahmed\Desktop\ahmed\solution 1\ShiftFlow.Web"
dotnet run --no-build
```

**URLs:** `https://localhost:55248` / `http://localhost:55249`  
**Connection string:** `Server=(localdb)\MSSQLLocalDB;Database=ShiftFlowDB;Trusted_Connection=True;TrustServerCertificate=True;`

---

## 8. After Fixing the Calendar

1. Remove the debug div from `Details.cshtml` (~lines 298–310).
2. Create a new schedule with a custom rotation (not A/B/C/D/F defaults).
3. Open Details → Calendar tab — verify the circles show the custom rotation.
4. Test "Regenerate Rotation" button on an older schedule.
5. Commit: `fix: calendar view reads shifts from DailyGroupShift with string date keys`
