namespace ShiftFlow.Domain.Entities;

/// <summary>Admin-managed canned text an employee picks from instead of typing a free-text field -
/// one shared catalog reused across every field converted to "pick from a list" (Orders' Description,
/// Report Action's Notes, and future ones), distinguished by FieldKey. Optionally scoped narrower
/// within that field (OrderTypeId for "OrderDescription", CategoryId for "ReportActionNotes") -
/// null means it applies to every value of that scope. Only ever one of OrderTypeId/CategoryId is
/// set, whichever FieldKey's scope column that is.</summary>
public class TextPreset
{
    public int Id { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public int? OrderTypeId { get; set; } public virtual OrderType? OrderType { get; set; }
    public int? CategoryId { get; set; } public virtual AssetCategory? Category { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? TextAr { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>The set of FieldKey values currently wired up to a picker somewhere in the app -
    /// keeps the admin page's "Field" dropdown and each field's scope-type in one place.</summary>
    public static class Fields
    {
        public const string OrderDescription = "OrderDescription";
        public const string ReportActionNotes = "ReportActionNotes";
        public static readonly string[] All = [OrderDescription, ReportActionNotes];
    }
}
