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

    /// <summary>"User" or "Group" — which side of the toggle is active when AssignmentMode=="Either".
    /// Ignored (server re-derives from OrderType.AssignmentMode) when the mode is EmployeeOnly/GroupOnly.
    /// Defaults to "User": assigning one named employee is the common case, and the old "Group"
    /// default opened a fresh Create form on the rarer branch with an empty group picker.</summary>
    public string AssigneeType { get; set; } = "User";
    public string? AssignedToUserId { get; set; }
    public int? AssignedToGroupId { get; set; }

    /// <summary>Used when the type's AllowsMultipleAssets is false.</summary>
    public int AssetId { get; set; }
    /// <summary>Used when the type's AllowsMultipleAssets is true.</summary>
    public List<int>? AssetIds { get; set; }

    /// <summary>Everything the form needs to render itself - typed, so the view doesn't cast
    /// half a dozen ViewBag entries. Never bound from the request; repopulated on every render.</summary>
    public OrderCreateOptions Options { get; set; } = new();
}

/// <summary>Lookup data and pre-resolved labels for Views/Orders/Create.cshtml.</summary>
public sealed class OrderCreateOptions
{
    public List<ShiftFlow.Domain.Entities.OrderType> OrderTypes { get; set; } = [];
    public List<ShiftFlow.Domain.Entities.Group> Groups { get; set; } = [];
    public List<ShiftFlow.Domain.Entities.AssetCategory> Categories { get; set; } = [];
    public List<ShiftFlow.Domain.Entities.LocationCategory> LocationCategories { get; set; } = [];
    /// <summary>Per-type flags the client script reads to show/hide fields.</summary>
    public string OrderTypeMetaJson { get; set; } = "{}";
    public string? SelectedAssetLabel { get; set; }
    public List<AssetChip> SelectedAssetChips { get; set; } = [];
    public string? SelectedEmployeeLabel { get; set; }
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
/// Orders, and Work Orders assigned to the current user (or their group, for Inspection Orders)
/// into a single list, with Category identifying which one it actually is. Replaces what used to
/// be three separate pages (My Tasks / My Maintenance Orders / My Assigned Work Orders).</summary>
public sealed class MyWorkOrderRow
{
    /// <summary>"Inspection" | "Maintenance" | "WorkOrder" — routing bucket (drives which
    /// controller "View" links to) and the badge colour on the Dashboard / My Orders lists.</summary>
    public string Category { get; init; } = "";
    /// <summary>Human-readable form of Category, rendered as a badge on the Dashboard's recent-orders
    /// feed and the My Orders page. The Orders list shows OrderTypeLabel instead.</summary>
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
    /// <summary>Assignee display text (a name, "Group: X", or null) — only populated where the
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

/// <summary>One row of the unified Orders list. Projected straight out of SQL (no entity graphs,
/// no Includes) — the display-only fields below are filled in afterwards, since none of them can be
/// translated in the database.</summary>
public sealed class OrderListRow
{
    /// <summary>"Inspection" | "Maintenance" — which controller Details links to.</summary>
    public string Category { get; init; } = "";
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public int? OrderTypeId { get; init; }
    public string? OrderTypeName { get; init; }
    public string? OrderTypeNameAr { get; init; }
    public string OrderTypeColor { get; init; } = "#6c757d";
    /// <summary>Inspection orders only — how many assets the run covers.</summary>
    public int AssetCount { get; init; }
    /// <summary>Maintenance orders only.</summary>
    public string? AssetTag { get; init; }
    public string Status { get; init; } = "";
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? AssignedToUserName { get; init; }
    public string? AssignedToGroupName { get; init; }

    // Filled in by the controller after the query, in the request's language.
    public string OrderTypeLabel { get; set; } = "";
    public string? AssetLabel { get; set; }
    public string? AssignedToLabel { get; set; }
}

/// <summary>Turns a stored status/stage token into something readable — "PendingApproval" was
/// rendered raw on the Orders list. Splits camel case; the caller still runs it through Loc.</summary>
public static class StatusDisplay
{
    public static string Label(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "";
        var sb = new System.Text.StringBuilder(status.Length + 4);
        for (var i = 0; i < status.Length; i++)
        {
            if (i > 0 && char.IsUpper(status[i]) && !char.IsUpper(status[i - 1]) && status[i - 1] != ' ') sb.Append(' ');
            sb.Append(status[i]);
        }
        return sb.ToString();
    }
}
