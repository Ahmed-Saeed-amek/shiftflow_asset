// TEMPORARY — delete when the shared UI branch (ui/ui-shared) is merged.
// The assets/zones UI pass codes against two shared-layer additions that live on the shared
// branch: PageHeaderModel.Breadcrumbs and EmptyStateModel. They are stubbed here, matching the
// agreed API exactly, so this branch builds on its own. The merger deletes this file (and drops
// the `partial` keyword added to PageHeaderModel in ViewModels.cs, which exists only so this
// stub can attach Breadcrumbs without touching the shared-owned class body).
namespace ShiftFlow.Web.ViewModels;

public partial class PageHeaderModel
{
    /// <summary>Trail rendered above the title. Labels arrive already translated; the last item has a null Url.</summary>
    public List<(string Label, string? Url)>? Breadcrumbs { get; set; }
}

/// <summary>Shared "nothing here yet" block (_EmptyState partial). Text is already translated.</summary>
public class EmptyStateModel
{
    public string Icon { get; set; } = "bi-inbox";
    public string Title { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? ActionUrl { get; set; }
    public string? ActionLabel { get; set; }
}
