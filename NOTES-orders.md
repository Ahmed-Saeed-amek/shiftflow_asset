# fix/order-services — merge notes

Things this branch needs from files it does not own.

## 1. `ApplicationDbContext` — map `OrderNumberSequence` (required, one migration)

New entity: `ShiftFlow.Web/Domain/Entities/OrderNumberSequence.cs`.
It backs the single order-number allocator (`Application/Services/OrderNumberGenerator.cs`) that
replaced the three per-service "load every number for the year and take the max" helpers.

Add the DbSet and mapping:

```csharp
public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();
```

```csharp
b.Entity<OrderNumberSequence>(e =>
{
    e.ToTable("OrderNumberSequences");              // table name is used verbatim by the seed INSERT
    e.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
    e.HasIndex(x => new { x.Prefix, x.Year }).IsUnique();   // required: the insert-on-missing path
                                                            // relies on this to detect a concurrent seed
});
```

Columns must be named `Id` (identity), `Prefix`, `Year`, `LastValue` — `OrderNumberGenerator`
seeds a missing row with raw SQL (`INSERT INTO [OrderNumberSequences] ([Prefix],[Year],[LastValue])`)
so nothing lands in the caller's change tracker mid-create.

Then generate **one** migration covering this table + unique index. No data migration is needed:
the first allocation for a given prefix/year seeds `LastValue` from the highest number already
present across WorkOrders / InspectionOrders / MaintenanceOrders, so existing numbers are never
reused.

**Until this mapping exists the app will throw at order creation** (`Set<OrderNumberSequence>()`
on an unmapped type). Tests are unaffected: `ShiftFlow.Web.Tests/OrderTestContext.cs` has a
`TestDbContext : ApplicationDbContext` that adds exactly the mapping above, so SQLite's
`EnsureCreated` builds the table. Once the production mapping lands, that override can be deleted.

## 2. `Program.cs` — one DI line (required)

`IOrderCreationService` (new, `Application/Services/OrderCreationService.cs`) holds the business
logic lifted out of `OrdersController.Create` (POST). Register it with the extension method already
provided in that file:

```csharp
builder.Services.AddOrderServices();   // next to the other AddScoped<...> order-service lines
```

Without it, `Orders/Create` fails to resolve the controller.

## 3. `Translations.cs`

See `TRANSLATIONS-orders.md` — four new English strings with Arabic.

## 4. `Views/Shared/_StatusBadge.cshtml` (optional, nice-to-have)

The badge dictionary keys off the raw token, so `"InProgress"` falls through to the grey default and
`"PendingApproval"` renders without a space. `Views/Orders/Index.cshtml` now renders those two
statuses with its own small colour map + `StatusDisplay.Label`. If the shared partial gains
`["In Progress"]`/`["Pending Approval"]` entries (and localizes the spaced form), that local map can
be dropped.

## 5. Layout toast

Per-view `TempData["Success"]/["Error"]` alert blocks were removed from `WorkOrders/Details`,
`MaintenanceOrders/Details` and `VendorPortal/Details` on the understanding that the layout renders a
single toast. If that change does not land, those three pages lose their inline error feedback.

## 6. Behaviour changes worth calling out at merge

- `IWorkOrderService.AdvanceWithoutVendorAsync` lost its `bool isManager` parameter — manager-ness is
  resolved inside the service via `IPermissionService`. `RequiresVendorResponse == true` now rejects
  the call outright.
- `IInspectionOrderService.UpdateInspectionItemAsync` changed shape: it now takes
  `(itemId, outcome, actionTypeId, causeId, notes, userId)` and returns the spawned Work Order id.
  The "Defective spawns a Work Order" rule moved into the service, inside its transaction.
- `IMaintenanceOrderService` gained `RejectApprovalAsync`; `CancelAsync` now also accepts
  `PendingApproval`.
- `ISparePartService` gained `IncrementStockAsync`.
- Maintenance order numbers now honour `OrderType.Prefix` (falling back to `MO`), like inspection
  orders. New maintenance orders created under a typed order will get that type's prefix.
- `InspectionOrdersController.Index` now redirects to `Orders/Index`; `Views/InspectionOrders/Index.cshtml`
  was deleted.
