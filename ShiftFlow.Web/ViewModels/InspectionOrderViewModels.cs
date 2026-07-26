using System.ComponentModel.DataAnnotations;

namespace ShiftFlow.Web.ViewModels;

public class InspectionOrderCreateVm : IValidatableObject
{
    [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"User" or "Team"</summary>
    [Required] public string AssigneeType { get; set; } = "User";
    public string? AssignedToUserId { get; set; }
    public int? AssignedToTeamId { get; set; }

    public int? ZoneId { get; set; }
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

        if (!ZoneId.HasValue && (AssetIds == null || AssetIds.Count == 0))
            yield return new ValidationResult(t("Select a zone or at least one asset to inspect."), new[] { nameof(AssetIds) });
    }
}

/// <summary>Row shape for My Tasks / My Orders lists and PDFs.</summary>
public sealed class InspectionOrderRow
{
    public int OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime? DueDate { get; init; }
    public string AssignedContext { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public int TotalAssets { get; init; }
    public int CheckedAssets { get; init; }
}
