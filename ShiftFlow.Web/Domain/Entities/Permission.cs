using System;

namespace ShiftFlow.Domain.Entities;

public class Permission
{
    public int Id { get; set; }

    /// <summary>
    /// Dot-notation name, e.g. "Task.Create". Must be unique across the catalog.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Logical grouping shown in the admin UI, e.g. "Tasks", "Reports".
    /// </summary>
    public string Category { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
