using System.ComponentModel.DataAnnotations;

namespace ShiftFlow.Web.ViewModels;

public class InspectionOrderCreateVm : IValidatableObject
{
    public string? Description { get; set; }
    [Required] public int OrderTypeId { get; set; }

    /// <summary>"User" or "Team"</summary>
    [Required] public string AssigneeType { get; set; } = "Team";
    public string? AssignedToUserId { get; set; }
    public int? AssignedToTeamId { get; set; }

    public List<int>? AssetIds { get; set; }

    public DateTime? DueDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var t = ValidationHelper.Localizer(validationContext);

        if (AssigneeType == "Team")
        {
            if (!AssignedToTeamId.HasValue || AssignedToTeamId <= 0)
                yield return new ValidationResult(string.Format(t("Please select a {0}."), t("Team")), new[] { nameof(AssignedToTeamId) });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(AssignedToUserId))
                yield return new ValidationResult(string.Format(t("Please select a {0}."), t("Employee")), new[] { nameof(AssignedToUserId) });
        }

        if (AssetIds == null || AssetIds.Count == 0)
            yield return new ValidationResult(t("Select at least one asset."), new[] { nameof(AssetIds) });
    }
}

/// <summary>Union of InspectionOrderCreateVm's and MaintenanceOrderCreateVm's fields, bound by the
/// unified Views/Orders/Create.cshtml form. Only the subset relevant to the selected OrderTypeId's
/// IsDirectFix flag is actually used at submit time — OrdersController.Create (POST) projects this
/// into whichever of the two existing VMs matches and re-validates that one; this VM itself carries
/// no IValidatableObject logic of its own. Property names are kept identical to both source VMs so
/// the re-validated ModelState keys line up with this view's asp-validation-for attributes.</summary>
public class OrderCreateVm
{
    [Required] public int OrderTypeId { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }

    // Inspection-style fields
    public string AssigneeType { get; set; } = "Team";
    public int? AssignedToTeamId { get; set; }
    public List<int>? AssetIds { get; set; }

    // Maintenance-style fields
    public int AssetId { get; set; }

    // Shared - the SAME hidden input name is used by both _EmployeePicker instances on the Create
    // page; only the active one is enabled at submit time, so exactly one value ever posts here
    // regardless of which field-set was showing.
    public string? AssignedToUserId { get; set; }
}

/// <summary>Row shape for the Profile / My Metrics "recent orders" list — unrelated to the
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
    /// <summary>"Inspection" | "Maintenance" | "WorkOrder" — drives the category filter and badge; matches the category query param.</summary>
    public string Category { get; init; } = "";
    public string CategoryLabel { get; init; } = "";
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
