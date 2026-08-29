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

/// <summary>Row shape for My Tasks / My Orders lists and PDFs.</summary>
public sealed class InspectionOrderRow
{
    public int OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public string OrderTypeName { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime? DueDate { get; init; }
    public string AssignedContext { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public int TotalAssets { get; init; }
    public int CheckedAssets { get; init; }
}

/// <summary>One calendar-month row on the employee's unified History page.</summary>
public sealed class MyHistoryMonthRow
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int InspectionCount { get; init; }
    public int MaintenanceCount { get; init; }
}
