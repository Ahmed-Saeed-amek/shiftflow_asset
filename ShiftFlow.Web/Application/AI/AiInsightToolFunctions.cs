using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Application.AI;

/// <summary>The "smart helper" tools: one-call daily briefing, the two bulk operations (both
/// preview-then-confirm), and page navigation.
///
/// The briefing deliberately checks each section's own permission itself and simply omits the
/// sections the caller can't see, rather than being registered behind a single blanket permission —
/// a technician asking "what's on today?" should still get their own overdue work.</summary>
public class AiInsightToolFunctions : AiToolsBase
{
    private readonly IPermissionService _permissions;
    private readonly IInspectionOrderService _inspectionOrders;
    private readonly IMaintenanceOrderService _maintenanceOrders;
    private readonly IWorkOrderService _workOrders;
    private readonly IPendingActionStore _pending;

    public AiInsightToolFunctions(ApplicationDbContext db, IAssetScopeService scope, IPermissionService permissions,
        IInspectionOrderService inspectionOrders, IMaintenanceOrderService maintenanceOrders,
        IWorkOrderService workOrders, IPendingActionStore pending) : base(db, scope)
    {
        _permissions = permissions;
        _inspectionOrders = inspectionOrders;
        _maintenanceOrders = maintenanceOrders;
        _workOrders = workOrders;
        _pending = pending;
    }

    // ── Daily briefing ────────────────────────────────────────────────────────

    public async Task<object> GetDailyBriefingAsync(string userId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var scopedAssetIds = await ScopedAssetIdsAsync(userId);
        var myGroupIds = await Db.GroupMembers.Where(g => g.UserId == userId).Select(g => g.GroupId).ToListAsync(ct);

        IQueryable<WorkOrder> workOrders = Db.WorkOrders.AsNoTracking();
        IQueryable<MaintenanceOrder> maintenanceOrders = Db.MaintenanceOrders.AsNoTracking();
        IQueryable<InspectionOrder> inspectionOrders = Db.InspectionOrders.AsNoTracking();
        if (scopedAssetIds != null)
        {
            workOrders = workOrders.Where(w => scopedAssetIds.Contains(w.AssetId));
            maintenanceOrders = maintenanceOrders.Where(m => scopedAssetIds.Contains(m.AssetId));
            inspectionOrders = inspectionOrders.Where(o => o.InspectionRun != null
                && o.InspectionRun.Items.Any(i => scopedAssetIds.Contains(i.AssetId)));
        }

        var canSeeWorkOrders = await _permissions.HasPermissionAsync(userId, PermissionCatalog.WorkOrderView);
        var canSeeMaintenance = await _permissions.HasPermissionAsync(userId, PermissionCatalog.MaintenanceOrderView);
        var canSeeInspections = await _permissions.HasPermissionAsync(userId, PermissionCatalog.InspectionOrderView)
            || await _permissions.HasPermissionAsync(userId, PermissionCatalog.InspectionOrderManage);
        var canSeeAssets = await _permissions.HasPermissionAsync(userId, PermissionCatalog.AssetView);
        var canSeeParts = await _permissions.HasPermissionAsync(userId, PermissionCatalog.SparePartView);
        var canSeeContracts = await _permissions.HasPermissionAsync(userId, PermissionCatalog.ContractView);

        var items = new List<AiLinkItem>();
        var cards = new List<AiCard>();

        // Overdue — all three order kinds. Work orders carry no due date of their own; the only
        // date that means "was expected by" is ScheduledDate on generated (PM/recurring) ones.
        var overdueInspections = canSeeInspections
            ? await inspectionOrders.Where(o => o.DueDate != null && o.DueDate < today && OpenInspectionStatuses.Contains(o.Status))
                .OrderBy(o => o.DueDate).Take(5)
                .Select(o => new { id = o.Id, number = o.OrderNumber, due = o.DueDate, status = o.Status }).ToListAsync(ct)
            : [];
        var overdueMaintenance = canSeeMaintenance
            ? await maintenanceOrders.Where(m => m.DueDate != null && m.DueDate < today && OpenMaintenanceStatuses.Contains(m.Status))
                .OrderBy(m => m.DueDate).Take(5)
                .Select(m => new { id = m.Id, number = m.OrderNumber, due = m.DueDate, status = m.Status }).ToListAsync(ct)
            : [];
        var overdueWorkOrders = canSeeWorkOrders
            ? await workOrders.Where(w => w.ScheduledDate != null && w.ScheduledDate < today && OpenWorkOrderStages.Contains(w.Stage))
                .OrderBy(w => w.ScheduledDate).Take(5)
                .Select(w => new { id = w.Id, number = w.WorkOrderNumber, due = w.ScheduledDate, status = w.Stage }).ToListAsync(ct)
            : [];

        var awaitingReview = canSeeWorkOrders ? await workOrders.CountAsync(w => w.Stage == WorkOrderStages.Draft, ct) : 0;
        var awaitingFixConfirmation = canSeeWorkOrders ? await workOrders.CountAsync(w => w.Stage == WorkOrderStages.FixedPendingConfirmation, ct) : 0;

        // Defective assets nobody has opened work against — the gap the ops team most wants surfaced.
        var defectiveNoOrder = canSeeAssets
            ? await (await ScopedAssetsAsync(userId))
                .Where(a => a.Status == AssetStatuses.Defective
                    && !Db.WorkOrders.Any(w => w.AssetId == a.Id && OpenWorkOrderStages.Contains(w.Stage))
                    && !Db.MaintenanceOrders.Any(m => m.AssetId == a.Id && OpenMaintenanceStatuses.Contains(m.Status)))
                .OrderBy(a => a.AssetTag).Take(5)
                .Select(a => new { id = a.Id, tag = a.AssetTag, name = a.Name, zone = a.Zone!.Name })
                .ToListAsync(ct)
            : [];

        var lowStock = canSeeParts
            ? await Db.SpareParts.AsNoTracking()
                .Where(p => p.IsActive && p.ReorderThreshold != null && p.StockQuantity <= p.ReorderThreshold)
                .OrderBy(p => p.StockQuantity).Take(5)
                .Select(p => new { id = p.Id, name = p.Name, stock = p.StockQuantity, threshold = p.ReorderThreshold })
                .ToListAsync(ct)
            : [];

        var expiringContracts = canSeeContracts
            ? await Db.Contracts.AsNoTracking()
                .Where(c => c.EndDate != null && c.EndDate >= today && c.EndDate <= today.AddDays(30))
                .OrderBy(c => c.EndDate).Take(5)
                .Select(c => new { id = c.Id, number = c.ContractNumber, vendor = c.Vendor!.Name, endDate = c.EndDate })
                .ToListAsync(ct)
            : [];

        // "My own open orders" is always included — it needs no permission beyond being the assignee.
        var myWorkOrders = await Db.WorkOrders.AsNoTracking()
            .Where(w => w.AssignedToUserId == userId && OpenWorkOrderStages.Contains(w.Stage))
            .OrderByDescending(w => w.CreatedDate).Take(5)
            .Select(w => new { id = w.Id, number = w.WorkOrderNumber, stage = w.Stage, asset = w.Asset!.AssetTag })
            .ToListAsync(ct);
        var myMaintenanceOrders = await Db.MaintenanceOrders.AsNoTracking()
            .Where(m => OpenMaintenanceStatuses.Contains(m.Status)
                && (m.AssignedToUserId == userId || (m.AssignedToGroupId != null && myGroupIds.Contains(m.AssignedToGroupId.Value))))
            .OrderByDescending(m => m.CreatedDate).Take(5)
            .Select(m => new { id = m.Id, number = m.OrderNumber, status = m.Status, asset = m.Asset!.AssetTag })
            .ToListAsync(ct);
        var myInspectionOrders = await Db.InspectionOrders.AsNoTracking()
            .Where(o => OpenInspectionStatuses.Contains(o.Status)
                && (o.AssignedToUserId == userId || (o.AssignedToGroupId != null && myGroupIds.Contains(o.AssignedToGroupId.Value))))
            .OrderByDescending(o => o.CreatedAt).Take(5)
            .Select(o => new { id = o.Id, number = o.OrderNumber, status = o.Status })
            .ToListAsync(ct);

        var overdueTotal = overdueInspections.Count + overdueMaintenance.Count + overdueWorkOrders.Count;
        var myTotal = myWorkOrders.Count + myMaintenanceOrders.Count + myInspectionOrders.Count;

        cards.Add(new AiCard
        {
            Title = "Today at a glance",
            Subtitle = today.ToString("dddd, dd/MM/yyyy"),
            Fields =
            [
                new("Overdue orders", overdueTotal.ToString()),
                new("Work orders awaiting review", awaitingReview.ToString()),
                new("Fixes awaiting confirmation", awaitingFixConfirmation.ToString()),
                new("Defective assets with no open order", defectiveNoOrder.Count.ToString()),
                new("Low-stock parts", lowStock.Count.ToString()),
                new("Contracts expiring in 30 days", expiringContracts.Count.ToString()),
                new("My open orders", myTotal.ToString()),
            ],
        });

        foreach (var o in overdueInspections) items.Add(new AiLinkItem($"Overdue: {o.number}", AiLinks.InspectionOrder(o.id), "alert"));
        foreach (var o in overdueMaintenance) items.Add(new AiLinkItem($"Overdue: {o.number}", AiLinks.MaintenanceOrder(o.id), "alert"));
        foreach (var o in overdueWorkOrders) items.Add(new AiLinkItem($"Overdue: {o.number}", AiLinks.WorkOrder(o.id), "alert"));
        foreach (var a in defectiveNoOrder) items.Add(new AiLinkItem($"Defective, no order: {a.tag}", AiLinks.Asset(a.id), "box"));
        foreach (var c in expiringContracts) items.Add(new AiLinkItem($"Expiring: {c.number ?? $"#{c.id}"} ({c.vendor})", AiLinks.Contract(c.id), "file"));
        if (awaitingReview > 0) items.Add(new AiLinkItem($"{awaitingReview} work order(s) awaiting review", AiLinks.WorkOrders(), "clipboard"));
        if (lowStock.Count > 0) items.Add(new AiLinkItem($"{lowStock.Count} low-stock part(s)", AiLinks.SpareParts(), "package"));
        if (myTotal > 0) items.Add(new AiLinkItem($"My {myTotal} open order(s)", AiLinks.MyOrders(), "user"));

        var ui = new List<object> { new AiCardsAttachment { Items = cards } };
        if (items.Count > 0) ui.Add(new AiLinksAttachment { Items = items.Take(15).ToList() });

        return new
        {
            date = today.ToString("yyyy-MM-dd"),
            overdue = new
            {
                total = overdueTotal,
                inspectionOrders = overdueInspections.Select(o => new { o.id, o.number, due = D(o.due), o.status }),
                maintenanceOrders = overdueMaintenance.Select(o => new { o.id, o.number, due = D(o.due), o.status }),
                workOrders = overdueWorkOrders.Select(o => new { o.id, o.number, due = D(o.due), o.status }),
            },
            workOrdersAwaitingReview = awaitingReview,
            workOrdersAwaitingFixConfirmation = awaitingFixConfirmation,
            defectiveAssetsWithNoOpenOrder = defectiveNoOrder,
            lowStockParts = lowStock,
            contractsExpiringWithin30Days = expiringContracts.Select(c => new { c.id, c.number, c.vendor, endDate = D(c.endDate) }),
            myOpenOrders = new
            {
                total = myTotal,
                workOrders = myWorkOrders,
                maintenanceOrders = myMaintenanceOrders,
                inspectionOrders = myInspectionOrders,
            },
            omittedSections = new
            {
                workOrders = !canSeeWorkOrders,
                maintenanceOrders = !canSeeMaintenance,
                inspectionOrders = !canSeeInspections,
                assets = !canSeeAssets,
                spareParts = !canSeeParts,
                contracts = !canSeeContracts,
            },
            ui = ui.ToArray(),
        };
    }

    // ── Bulk: create inspection orders ────────────────────────────────────────

    public async Task<object> BulkCreateInspectionOrdersAsync(int? zoneId, int? categoryId, List<int>? assetIds,
        string? assignedToUserId, int? assignedToGroupId, DateTime? dueDate, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignedToUserId) == (assignedToGroupId is null))
            return Failed("Assign to exactly one of an employee or a group.");

        var resolved = await ResolveTargetAssetsAsync(zoneId, categoryId, assetIds, userId, ct);
        if (resolved.Count == 0)
            return Failed("No assets matched that zone/category/list inside the assets you have access to.");

        var assignee = await DescribeAssigneeAsync(assignedToUserId, assignedToGroupId, ct);
        var preview = resolved.Take(10).Select(a => a.Tag).ToList();
        var summary = $"Create one inspection order covering {resolved.Count} asset(s), assigned to {assignee}"
            + (dueDate.HasValue ? $", due {D(dueDate)}" : "") + ".";

        var targetIds = resolved.Select(a => a.Id).ToList();
        var token = _pending.Create(userId, summary, PermissionCatalog.InspectionOrderManage, async (sp, innerCt) =>
        {
            var tools = sp.GetRequiredService<AiInsightToolFunctions>();
            return await tools.BulkCreateInspectionOrdersConfirmedAsync(targetIds, assignedToUserId, assignedToGroupId, dueDate, userId, innerCt);
        });

        return new
        {
            pendingConfirmation = true,
            token,
            summary,
            assetCount = resolved.Count,
            firstAssets = preview,
            ui = new AiConfirmAttachment
            {
                Token = token,
                Title = "Create inspection order",
                Summary = summary,
                Items = preview.Count < resolved.Count ? [.. preview, $"… and {resolved.Count - preview.Count} more"] : preview,
                ActionLabel = "Create order",
                Danger = false,
            },
        };
    }

    public async Task<object> BulkCreateInspectionOrdersConfirmedAsync(List<int> assetIds, string? assignedToUserId,
        int? assignedToGroupId, DateTime? dueDate, string userId, CancellationToken ct)
    {
        // Re-resolve through scope: the token may be up to 10 minutes old and the caller's scope
        // (or the assets themselves) can have changed in between.
        var stillVisible = await (await ScopedAssetsAsync(userId)).Where(a => assetIds.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        if (stillVisible.Count == 0) return Failed("None of those assets are available any more.");

        var orderTypeId = await Db.OrderTypes
            .Where(t => t.IsActive && !t.IsDirectFix)
            .OrderByDescending(t => t.TracksDefectOutcome).ThenBy(t => t.SortOrder)
            .Select(t => t.Id).FirstOrDefaultAsync(ct);
        if (orderTypeId == 0) return Failed("No active inspection-style order type is configured.");

        var order = await _inspectionOrders.CreateAsync(orderTypeId, null, assignedToUserId, assignedToGroupId,
            stillVisible, dueDate, userId);

        return new
        {
            success = true,
            id = order.Id,
            orderNumber = order.OrderNumber,
            assetCount = stillVisible.Count,
            ui = new AiLinksAttachment { Items = [new AiLinkItem(order.OrderNumber, AiLinks.InspectionOrder(order.Id), "clipboard")] },
        };
    }

    // ── Bulk: reassign open orders ────────────────────────────────────────────

    private static readonly string[] AllKinds = ["inspection", "maintenance", "work"];

    public async Task<object> BulkReassignOpenOrdersAsync(string fromUserId, string toUserId, List<string>? kinds,
        string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fromUserId) || string.IsNullOrWhiteSpace(toUserId))
            return Failed("Both a from-user and a to-user are required.");
        if (fromUserId == toUserId) return Failed("The from-user and to-user are the same person.");
        if (!await Db.Users.AnyAsync(u => u.Id == toUserId && u.IsActive, ct))
            return Failed("The target employee doesn't exist or is deactivated.");

        var wanted = kinds is { Count: > 0 } ? kinds.Select(k => k.ToLowerInvariant()).ToList() : AllKinds.ToList();
        var targets = await FindReassignTargetsAsync(fromUserId, wanted, userId, ct);
        if (targets.Count == 0) return Failed("That employee has no open orders matching those kinds.");

        var fromName = await Db.Users.Where(u => u.Id == fromUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? fromUserId;
        var toName = await Db.Users.Where(u => u.Id == toUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? toUserId;
        var summary = $"Reassign {targets.Count} open order(s) from {fromName} to {toName}.";

        var token = _pending.Create(userId, summary, PermissionCatalog.InspectionOrderManage, async (sp, innerCt) =>
        {
            var tools = sp.GetRequiredService<AiInsightToolFunctions>();
            return await tools.BulkReassignOpenOrdersConfirmedAsync(fromUserId, toUserId, wanted, userId, innerCt);
        });

        var table = new AiTableAttachment
        {
            Title = "Orders to reassign",
            Columns = [new("number", "Order"), new("kind", "Kind"), new("status", "Status")],
            Rows = targets.Take(25).Select(t => new Dictionary<string, object?>
            {
                ["number"] = t.Number,
                ["kind"] = t.Kind,
                ["status"] = t.Status,
                ["_url"] = t.Url,
                ["_status"] = t.Status,
            }).ToList(),
        };

        return new
        {
            pendingConfirmation = true,
            token,
            summary,
            orderCount = targets.Count,
            orders = targets.Take(25).Select(t => new { t.Number, t.Kind, t.Status }),
            ui = new object[]
            {
                table,
                new AiConfirmAttachment
                {
                    Token = token,
                    Title = "Reassign open orders",
                    Summary = summary,
                    Items = targets.Take(10).Select(t => $"{t.Kind}: {t.Number}").ToList(),
                    ActionLabel = "Reassign",
                    Danger = true,
                },
            },
        };
    }

    public async Task<object> BulkReassignOpenOrdersConfirmedAsync(string fromUserId, string toUserId, List<string> kinds,
        string userId, CancellationToken ct)
    {
        var targets = await FindReassignTargetsAsync(fromUserId, kinds, userId, ct);
        var reassigned = new List<string>();
        var failures = new List<string>();

        foreach (var t in targets)
        {
            try
            {
                switch (t.Kind)
                {
                    case "inspection": await _inspectionOrders.ReassignAsync(t.Id, toUserId, null, userId); break;
                    case "maintenance": await _maintenanceOrders.ReassignAsync(t.Id, toUserId, null, userId); break;
                    default: await _workOrders.AssignEmployeeAsync(t.Id, toUserId, userId); break;
                }
                reassigned.Add(t.Number);
            }
            catch (InvalidOperationException ex)
            {
                failures.Add($"{t.Number}: {ex.Message}");
            }
        }

        return new
        {
            success = failures.Count == 0,
            reassignedCount = reassigned.Count,
            reassigned,
            failures,
            ui = new AiLinksAttachment { Items = [new AiLinkItem("Orders", AiLinks.Orders(), "clipboard")] },
        };
    }

    private sealed record ReassignTarget(int Id, string Kind, string Number, string Status, string Url);

    private async Task<List<ReassignTarget>> FindReassignTargetsAsync(string fromUserId, List<string> kinds, string userId, CancellationToken ct)
    {
        var scopedAssetIds = await ScopedAssetIdsAsync(userId);
        var targets = new List<ReassignTarget>();

        if (kinds.Contains("inspection"))
        {
            var rows = await Db.InspectionOrders.AsNoTracking()
                .Where(o => o.AssignedToUserId == fromUserId && OpenInspectionStatuses.Contains(o.Status))
                .Select(o => new { o.Id, o.OrderNumber, o.Status }).Take(100).ToListAsync(ct);
            targets.AddRange(rows.Select(r => new ReassignTarget(r.Id, "inspection", r.OrderNumber, r.Status, AiLinks.InspectionOrder(r.Id))));
        }
        if (kinds.Contains("maintenance"))
        {
            var q = Db.MaintenanceOrders.AsNoTracking()
                .Where(m => m.AssignedToUserId == fromUserId && OpenMaintenanceStatuses.Contains(m.Status));
            if (scopedAssetIds != null) q = q.Where(m => scopedAssetIds.Contains(m.AssetId));
            var rows = await q.Select(m => new { m.Id, m.OrderNumber, m.Status }).Take(100).ToListAsync(ct);
            targets.AddRange(rows.Select(r => new ReassignTarget(r.Id, "maintenance", r.OrderNumber, r.Status, AiLinks.MaintenanceOrder(r.Id))));
        }
        if (kinds.Contains("work"))
        {
            var q = Db.WorkOrders.AsNoTracking()
                .Where(w => w.AssignedToUserId == fromUserId && OpenWorkOrderStages.Contains(w.Stage));
            if (scopedAssetIds != null) q = q.Where(w => scopedAssetIds.Contains(w.AssetId));
            var rows = await q.Select(w => new { w.Id, w.WorkOrderNumber, w.Stage }).Take(100).ToListAsync(ct);
            targets.AddRange(rows.Select(r => new ReassignTarget(r.Id, "work", r.WorkOrderNumber, r.Stage, AiLinks.WorkOrder(r.Id))));
        }

        return targets;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private static readonly (string[] Keys, string Label, string Url, string Icon)[] NavTargets =
    [
        (["assets", "asset list", "inventory of assets", "equipment"], "Assets", AiLinks.Assets(), "box"),
        (["orders", "all orders", "order list"], "Orders", AiLinks.Orders(), "clipboard"),
        (["new order", "create order", "raise an order"], "New order", AiLinks.NewOrder(), "plus"),
        (["work orders", "workorders"], "Work orders", AiLinks.WorkOrders(), "clipboard"),
        (["inspection orders", "inspections"], "Inspection orders", AiLinks.InspectionOrders(), "check-square"),
        (["maintenance orders", "maintenance"], "Maintenance orders", AiLinks.MaintenanceOrders(), "wrench"),
        (["spare parts", "parts", "stock", "inventory"], "Spare parts", AiLinks.SpareParts(), "package"),
        (["parts analytics", "spare parts analytics"], "Spare parts analytics", AiLinks.SparePartsAnalytics(), "bar-chart"),
        (["zone overview", "zones overview"], "Zone overview", AiLinks.ZoneOverview(), "map-pin"),
        (["zones"], "Zones", AiLinks.Zones(), "map-pin"),
        (["contracts"], "Contracts", AiLinks.Contracts(), "file"),
        (["vendors", "suppliers"], "Vendors", AiLinks.Vendors(), "briefcase"),
        (["dashboard", "kpis", "executive dashboard"], "Dashboard", AiLinks.Dashboard(), "bar-chart"),
        (["audit logs", "audit log", "audit trail"], "Audit logs", AiLinks.AuditLogs(), "list"),
        (["users", "employees", "staff"], "Users", AiLinks.Users(), "user"),
        (["groups", "teams"], "Groups", AiLinks.Groups(), "users"),
        (["my orders", "my work", "my tasks"], "My orders", AiLinks.MyOrders(), "user"),
        (["home", "my home"], "My home", AiLinks.MyHome(), "home"),
        (["recurring orders", "schedules"], "Recurring orders", AiLinks.RecurringOrders(), "repeat"),
    ];

    /// <summary>Resolves a spoken page name, or an order number like "WO-2026-0004", to a link the
    /// client renders as a button. Order numbers are resolved through the caller's scope like every
    /// other read, so this can't be used to probe for records the user can't open.</summary>
    public async Task<object> NavigateToAsync(string target, string userId, CancellationToken ct)
    {
        var term = (target ?? "").Trim();
        if (term.Length == 0) return Failed("Say which page you want to open.");

        var lower = term.ToLowerInvariant();
        // Exact key first, then a contains-match, so "open the spare parts page" still resolves
        // while "parts" doesn't win over an exact "spare parts".
        var exact = NavTargets.FirstOrDefault(t => t.Keys.Contains(lower));
        var match = exact.Label != null ? exact : NavTargets.FirstOrDefault(t => t.Keys.Any(k => lower.Contains(k)));

        if (match.Label != null)
            return new
            {
                success = true,
                page = match.Label,
                url = match.Url,
                ui = new AiLinksAttachment { Items = [new AiLinkItem(match.Label, match.Url, match.Icon)] },
            };

        // Not a page name — try it as an order number or asset tag.
        var scopedAssetIds = await ScopedAssetIdsAsync(userId);

        var wo = await Db.WorkOrders.AsNoTracking()
            .Where(w => w.WorkOrderNumber == term && (scopedAssetIds == null || scopedAssetIds.Contains(w.AssetId)))
            .Select(w => new { w.Id, w.WorkOrderNumber }).FirstOrDefaultAsync(ct);
        if (wo != null) return NavLink(wo.WorkOrderNumber, AiLinks.WorkOrder(wo.Id), "clipboard");

        var mo = await Db.MaintenanceOrders.AsNoTracking()
            .Where(m => m.OrderNumber == term && (scopedAssetIds == null || scopedAssetIds.Contains(m.AssetId)))
            .Select(m => new { m.Id, m.OrderNumber }).FirstOrDefaultAsync(ct);
        if (mo != null) return NavLink(mo.OrderNumber, AiLinks.MaintenanceOrder(mo.Id), "wrench");

        var io = await Db.InspectionOrders.AsNoTracking()
            .Where(o => o.OrderNumber == term).Select(o => new { o.Id, o.OrderNumber }).FirstOrDefaultAsync(ct);
        if (io != null) return NavLink(io.OrderNumber, AiLinks.InspectionOrder(io.Id), "check-square");

        var asset = await (await ScopedAssetsAsync(userId)).Where(a => a.AssetTag == term)
            .Select(a => new { a.Id, a.AssetTag }).FirstOrDefaultAsync(ct);
        if (asset != null) return NavLink(asset.AssetTag, AiLinks.Asset(asset.Id), "box");

        return new
        {
            error = "not_found",
            message = "No page or record matches that.",
            knownPages = NavTargets.Select(t => t.Label),
        };
    }

    private static object NavLink(string label, string url, string icon) => new
    {
        success = true,
        label,
        url,
        ui = new AiLinksAttachment { Items = [new AiLinkItem(label, url, icon)] },
    };

    // ── shared helpers ────────────────────────────────────────────────────────

    private sealed record TargetAsset(int Id, string Tag);

    private async Task<List<TargetAsset>> ResolveTargetAssetsAsync(int? zoneId, int? categoryId, List<int>? assetIds,
        string userId, CancellationToken ct)
    {
        var q = await ScopedAssetsAsync(userId);
        if (assetIds is { Count: > 0 }) q = q.Where(a => assetIds.Contains(a.Id));
        else if (zoneId.HasValue) q = q.Where(a => a.ZoneId == zoneId);
        else if (categoryId.HasValue) q = q.Where(a => a.CategoryId == categoryId || (a.Category != null && a.Category.ParentCategoryId == categoryId));
        else return [];

        // Retired assets are excluded — an inspection order against decommissioned equipment is
        // never what "inspect the whole zone" means.
        return await q.Where(a => a.Status != AssetStatuses.Retired)
            .OrderBy(a => a.AssetTag).Take(500)
            .Select(a => new TargetAsset(a.Id, a.AssetTag)).ToListAsync(ct);
    }

    private async Task<string> DescribeAssigneeAsync(string? assignedToUserId, int? assignedToGroupId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(assignedToUserId))
            return await Db.Users.Where(u => u.Id == assignedToUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "that employee";
        var name = await Db.Groups.Where(g => g.Id == assignedToGroupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
        return name != null ? $"group {name}" : "that group";
    }
}
