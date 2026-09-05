using System.ComponentModel.DataAnnotations;

namespace ShiftFlow.Web.ViewModels;

/// <summary>Bound by the unified Views/Orders/Create.cshtml form — every field an Order Type's
/// configuration (AllowsMultipleAssets, AssignmentMode) might call for, regardless of IsDirectFix.
/// No IValidatableObject here: OrdersController.Create (POST) resolves which fields actually apply
/// server-side (never trusting the client) and relies on IInspectionOrderService/
/// IMaintenanceOrderService's own "exactly one assignee"/"at least one asset" guards, surfacing
/// their InvalidOperationException as a page-level error banner like every other failure in that
/// action already does — simpler than re-deriving per-type-conditional field validation here.</summary>
public class OrderCreateVm
{
    [Required] public int OrderTypeId { get; set; }
    public DateTime? DueDate { get; set; }

    /// <summary>"User" or "Team" — which side of the toggle is active when AssignmentMode=="Either".
    /// Ignored (server re-derives from OrderType.AssignmentMode) when the mode is EmployeeOnly/TeamOnly.</summary>
    public string AssigneeType { get; set; } = "Team";
    public string? AssignedToUserId { get; set; }
    public int? AssignedToTeamId { get; set; }

    /// <summary>Used when the type's AllowsMultipleAssets is false.</summary>
    public int AssetId { get; set; }
    /// <summary>Used when the type's AllowsMultipleAssets is true.</summary>
    public List<int>? AssetIds { get; set; }
}

/// <summary>Row shape for the Profile page's "recent orders" list — unrelated to the
/// unified My Orders page (MyWorkOrderRow below); this one is Inspection-Orders-only, scoped to
/// whichever employee's profile is being viewed.</summary>
public sealed class InspectionOrderRow
{
    public int OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime? DueDate { get; init; }
    public string AssignedContext { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public int TotalAssets { get; init; }
    public int CheckedAssets { get; init; }
}

/// <summary>One row on the unified "My Orders" page — combines Inspection Orders, Maintenance
/// Orders, and Work Orders assigned to the current user (or their team, for Inspection Orders)
/// into a single list, with Category identifying which one it actually is. Replaces what used to
/// be three separate pages (My Tasks / My Maintenance Orders / My Assigned Work Orders).</summary>
public sealed class MyWorkOrderRow
{
    /// <summary>"Inspection" | "Maintenance" | "WorkOrder" — internal routing bucket only (drives
    /// which controller "View" links to); NOT what's shown to the user any more, see OrderTypeLabel/Color.</summary>
    public string Category { get; init; } = "";
    public string CategoryLabel { get; init; } = "";
    /// <summary>The actual OrderType this order was created with — null for Work Orders, which
    /// aren't created via an OrderType. Drives the Orders list's per-type filter tab and row badge.</summary>
    public int? OrderTypeId { get; init; }
    public string OrderTypeLabel { get; init; } = "";
    /// <summary>Hex color for the badge/filter chip — from OrderType.Color, or a fixed neutral for Work Orders.</summary>
    public string OrderTypeColor { get; init; } = "#6c757d";
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    /// <summary>Asset tag for single-asset orders (Maintenance/Work Order), or "N assets" for a multi-asset Inspection Order.</summary>
    public string? AssetLabel { get; init; }
    /// <summary>Raw status/stage string (each category has its own vocabulary) — rendered via _StatusBadge same as before.</summary>
    public string Status { get; init; } = "";
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Controller to route "View" to — InspectionOrders / MaintenanceOrders / WorkOrders.</summary>
    public string DetailsController { get; init; } = "";
    /// <summary>Assignee display text (a name, "Team: X", or null) — only populated where the
    /// caller needs to show who an order belongs to (e.g. an org-wide recent-orders feed).</summary>
    public string? AssignedToLabel { get; init; }
}

/// <summary>One calendar-month row on the employee's unified History page.</summary>
public sealed class MyHistoryMonthRow
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int InspectionCount { get; init; }
    public int MaintenanceCount { get; init; }
    public int WorkOrderCount { get; init; }
}
