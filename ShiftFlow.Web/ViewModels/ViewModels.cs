using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Web.Localization;
namespace ShiftFlow.Web.ViewModels;

/// <summary>
/// [Required] never fires on a non-nullable int/value type (0 is a "valid" value as far as
/// DataAnnotations is concerned), so a dropdown left at its unselected "—" option sails through
/// ModelState.IsValid and crashes later on the database's FK constraint instead. Every ViewModel
/// with an int FK picker should implement IValidatableObject and call CheckSelected for each one.
/// </summary>
internal static class ValidationHelper
{
    public static Func<string, string> Localizer(ValidationContext ctx)
    {
        var loc = (ILanguageService?)ctx.GetService(typeof(ILanguageService));
        return key => loc?.T(key) ?? key;
    }

    public static IEnumerable<ValidationResult> CheckSelected(int value, string memberName, string fieldLabelKey, Func<string, string> t)
    {
        if (value <= 0)
            yield return new ValidationResult(string.Format(t("Please select a {0}."), t(fieldLabelKey)), new[] { memberName });
    }
}
public class LoginViewModel{[Required,EmailAddress]public string Email{get;set;}=string.Empty;[Required,DataType(DataType.Password)]public string Password{get;set;}=string.Empty;[Display(Name="Remember me")]public bool RememberMe{get;set;}}

public class KpiCardModel
{
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-clipboard-data";
    public string? Subtitle { get; set; }
    /// <summary>One of: primary, emerald, amber, violet, red, blue, indigo, orange</summary>
    public string Color { get; set; } = "primary";
}

public class PageHeaderModel
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    /// <summary>Pre-rendered HTML for right-aligned action buttons (caller builds this with a small inline string or a child partial).</summary>
    public string? ActionsHtml { get; set; }
}

public class ZoneViewModel : IValidatableObject
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int LocationCategoryId { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var t = ValidationHelper.Localizer(validationContext);
        foreach (var r in ValidationHelper.CheckSelected(LocationCategoryId, nameof(LocationCategoryId), "Location Category", t)) yield return r;
    }
}

public class ContractViewModel : IValidatableObject
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    [Required] public string ContractType { get; set; } = "Warranty";
    public string? ContractNumber { get; set; }
    [Required] public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EndDate { get; set; }
    public decimal? Cost { get; set; }
    public string? Notes { get; set; }
    public List<int>? AssetIds { get; set; }
    /// <summary>Recurrence cadence, required only when ContractType == "Preventive Maintenance".</summary>
    public string? PmCadence { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        string T(string key) => ValidationHelper.Localizer(validationContext)(key);

        foreach (var r in ValidationHelper.CheckSelected(VendorId, nameof(VendorId), "Vendor", T)) yield return r;

        if (ContractType == "Preventive Maintenance")
        {
            if (EndDate == null)
                yield return new ValidationResult(T("End Date is required for Preventive Maintenance contracts."), new[] { nameof(EndDate) });
            else if (EndDate <= StartDate)
                yield return new ValidationResult(T("End Date must be after Start Date."), new[] { nameof(EndDate) });

            if (string.IsNullOrWhiteSpace(PmCadence) || !Contract.PmCadences.Contains(PmCadence))
                yield return new ValidationResult(T("A cadence must be selected for Preventive Maintenance contracts."), new[] { nameof(PmCadence) });
        }
    }
}

public class AssetChip
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class AssetMultiPickerModel
{
    /// <summary>Hidden input name, repeated once per selected asset — binds straight to a List&lt;int&gt; on the target view model.</summary>
    public string Name { get; set; } = string.Empty;
    public List<AssetChip> Selected { get; set; } = new();
    public string? Placeholder { get; set; }
    /// <summary>Top-level categories. When set, renders a "add all assets in category" control above the search box.</summary>
    public List<AssetCategory>? Categories { get; set; }
    /// <summary>The 3 fixed location categories. When set, renders a Category→Zone
    /// "add all assets at this location" control above the search box.</summary>
    public List<LocationCategory>? LocationCategories { get; set; }
}

public class SparePartViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    [MaxLength(100)] public string? Sku { get; set; }
    [Range(0, 9_999_999_999.99)] public decimal? UnitCost { get; set; }
    // Create only - Edit's form doesn't expose this field; stock changes go through AdjustStock instead.
    [Range(0, int.MaxValue)] public int StockQuantity { get; set; }
    [Range(0, int.MaxValue)] public int? ReorderThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public List<int>? AssetIds { get; set; }
}

/// <summary>Renders the shared spare-part picker (compatible-parts <select> + quantity, replacing
/// free-text part entry) on a fix-report form for a specific asset.</summary>
public class SparePartPickerModel
{
    public int AssetId { get; set; }
    /// <summary>DOM id for the rows container — must be unique per form on a page (e.g. "employeeFixPartsList").</summary>
    public string ContainerId { get; set; } = string.Empty;
}

/// <summary>One row of a spare part's recent-usage history on its Details page, combining
/// WorkOrderParts and MaintenanceOrderParts into one common shape.</summary>
public class SparePartUsageRow
{
    public string WorkOrderNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime UsedDate { get; set; }
}

/// <summary>One raw usage record (WorkOrderPart or MaintenanceOrderPart with a SparePartId set),
/// projected to a common shape so both entities can be combined in-memory for analytics grouping —
/// EF can't UNION two different entity types in one LINQ query.</summary>
public class PartUsageRow
{
    public int SparePartId { get; set; }
    public int AssetId { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitCostAtUsage { get; set; }
    public DateTime UsedDate { get; set; }
}

public class SparePartUsageSummary
{
    public int SparePartId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public int UsageCount { get; set; }
    public decimal TotalCost { get; set; }
}

public class AssetUsageSummary
{
    public int AssetId { get; set; }
    public string AssetLabel { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public decimal TotalCost { get; set; }
    public int DistinctParts { get; set; }
}

public class CategoryUsageSummary
{
    public int CategoryId { get; set; }
    public string CategoryLabel { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public decimal TotalCost { get; set; }
}

public class MonthlyCostRow
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Cost { get; set; }
}

public class UserAssetScopeViewModel : IValidatableObject
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = string.Empty;
    public int? ZoneId { get; set; }
    public int? LocationCategoryId { get; set; }
    public int? CategoryId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var t = ValidationHelper.Localizer(validationContext);
        if (ZoneId == null && LocationCategoryId == null && CategoryId == null)
            yield return new ValidationResult(t("Pick at least one of Zone, Location Type, or Category."), new[] { nameof(ZoneId) });
    }
}

public class ReportActionViewModel : IValidatableObject
{
    public int AssetId { get; set; }
    public int ActionTypeId { get; set; }
    public int CauseId { get; set; }
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var t = ValidationHelper.Localizer(validationContext);
        foreach (var r in ValidationHelper.CheckSelected(AssetId, nameof(AssetId), "Asset", t)) yield return r;
        foreach (var r in ValidationHelper.CheckSelected(ActionTypeId, nameof(ActionTypeId), "Type", t)) yield return r;
        foreach (var r in ValidationHelper.CheckSelected(CauseId, nameof(CauseId), "Cause", t)) yield return r;
    }
}

public class AssetActionTypeViewModel : IValidatableObject
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ValidationHelper.CheckSelected(CategoryId, nameof(CategoryId), "Category", ValidationHelper.Localizer(validationContext));
}

public class AssetActionCauseViewModel : IValidatableObject
{
    public int Id { get; set; }
    public int ActionTypeId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ValidationHelper.CheckSelected(ActionTypeId, nameof(ActionTypeId), "Type", ValidationHelper.Localizer(validationContext));
}

public class AssetCategoryViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int? ParentCategoryId { get; set; }
}

public class AssetViewModel : IValidatableObject
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string AssetTag { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int CategoryId { get; set; }
    public int ZoneId { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Sku { get; set; }
    [Required] public string Status { get; set; } = "Working";
    public string? AssignedToUserId { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var t = ValidationHelper.Localizer(validationContext);
        foreach (var r in ValidationHelper.CheckSelected(CategoryId, nameof(CategoryId), "Category", t)) yield return r;
        foreach (var r in ValidationHelper.CheckSelected(ZoneId, nameof(ZoneId), "Zone", t)) yield return r;
    }
}

public class VendorViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    [EmailAddress] public string? Email { get; set; }
    public string? Specialization { get; set; }
    [Required] public string Status { get; set; } = "Active";
}

public class WorkOrderViewModel : IValidatableObject
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    [Required] public string Priority { get; set; } = "Medium";
    [MaxLength(4000)] public string? Description { get; set; }
    [MaxLength(4000)] public string? Notes { get; set; }
    public string? AssignedToUserId { get; set; }
    public bool RequiresVendorResponse { get; set; } = false;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ValidationHelper.CheckSelected(AssetId, nameof(AssetId), "Asset", ValidationHelper.Localizer(validationContext));
}

public class MaintenanceOrderCreateVm : IValidatableObject
{
    public int AssetId { get; set; }
    public int OrderTypeId { get; set; }
    public string? AssignedToUserId { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var t = ValidationHelper.Localizer(validationContext);
        foreach (var r in ValidationHelper.CheckSelected(AssetId, nameof(AssetId), "Asset", t)) yield return r;
        if (string.IsNullOrWhiteSpace(AssignedToUserId))
            yield return new ValidationResult(string.Format(t("Please select a {0}."), t("Employee")), new[] { nameof(AssignedToUserId) });
    }
}

public class MaintenanceOrderCompleteVm
{
    public string? FixDescription { get; set; }
    public decimal? Cost { get; set; }
    public DateTime? CompletedDate { get; set; }
    public List<int>? SparePartIds { get; set; }
    public List<int>? PartQuantities { get; set; }
}

public class WorkOrderBlockReasonViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MaintenanceActionTypeViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;
}

public class OrderTypeViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    [Required, MaxLength(10)] public string Prefix { get; set; } = "INS";
    public bool TracksDefectOutcome { get; set; }
    public bool RequiresVendor { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class VendorFixViewModel
{
    [Required(ErrorMessage = "Describe what was done before submitting the fix."), MaxLength(4000)]
    public string? Description { get; set; }
    public decimal? Cost { get; set; }
    public DateTime? CompletionDate { get; set; }
    public List<int>? SparePartIds { get; set; }
    public List<int>? PartQuantities { get; set; }
    public List<IFormFile>? Files { get; set; }
}

public class EmployeePickerModel
{
    public string Name { get; set; } = string.Empty;   // hidden input name (carries user id)
    public string? Role { get; set; }                   // optional role filter for the search
    public string? SelectedId { get; set; }
    public string? SelectedLabel { get; set; }
    public string? Placeholder { get; set; }
    public string? CssClass { get; set; }
}

/// <summary>Reusable searchable single-asset picker (typeahead by asset tag / name), mirroring
/// EmployeePickerModel's shape — the hidden input carries the selected asset id under Name.</summary>
public class AssetSinglePickerModel
{
    public string Name { get; set; } = string.Empty;
    public int? SelectedId { get; set; }
    public string? SelectedLabel { get; set; }
    public string? Placeholder { get; set; }
    public string? CssClass { get; set; }
}

/// <summary>Reusable Location Category → searchable Zone picker: pick one of the 3 fixed categories,
/// then immediately search/scroll that category's zones — no further cascading levels. The hidden
/// input carries the selected zone id under Name, mirroring EmployeePickerModel's shape.</summary>
public class ZoneComboboxModel
{
    public string Name { get; set; } = string.Empty;
    public List<LocationCategory> LocationCategories { get; set; } = new();
    public int? SelectedLocationCategoryId { get; set; }
    public int? SelectedZoneId { get; set; }
    public string? SelectedZoneLabel { get; set; }
    public string? Placeholder { get; set; }
    public string? CssClass { get; set; }
}

public class UserCreateViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "Engineer";
    [Required, MaxLength(50)] public string EmployeeNumber { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Phone { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)] public string CurrentPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), MinLength(12)] public string NewPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = string.Empty;
}
