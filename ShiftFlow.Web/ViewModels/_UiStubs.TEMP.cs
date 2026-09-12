namespace ShiftFlow.Web.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// TEMPORARY — delete this whole file when the shared-UI branch is merged in.
// It exists only so the ui/ui-orders branch compiles on its own while the shared
// layer (_PageHeader breadcrumbs, _EmptyState partial) is still being built on a
// parallel branch. See NOTES-orders.md for the exact post-merge cleanup steps.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// TEMP: stands in for the <c>Breadcrumbs</c> property being added to
/// <see cref="PageHeaderModel"/> on the shared branch. PageHeaderModel isn't partial and lives in
/// a file this branch doesn't own, so the property can't simply be added from here — a subclass is
/// the only way to compile against the API without touching ViewModels.cs.
/// Post-merge: delete this class and change every <c>new PageHeaderWithBreadcrumbs</c> in the
/// order views to <c>new PageHeaderModel</c> (or <c>PageHeaderWithActions</c> where Actions is set).
/// The trailing item's Url is null — it is the current page.
/// </summary>
public class PageHeaderWithBreadcrumbs : PageHeaderWithActions
{
    public List<(string Label, string? Url)>? Breadcrumbs { get; set; }
}

/// <summary>
/// TEMP: model for the shared <c>_EmptyState</c> partial being added on the shared branch.
/// Matches the agreed API exactly, so post-merge only this duplicate declaration is deleted —
/// no view changes needed. NOTE: the views below already reference the partial by name, so the
/// partial itself must exist before an empty list renders.
/// </summary>
public class EmptyStateModel
{
    /// <summary>Bootstrap Icons class, e.g. "bi-clipboard-check".</summary>
    public string Icon { get; set; } = "bi-inbox";
    public string Title { get; set; } = string.Empty;
    public string? Text { get; set; }
    /// <summary>Optional call-to-action link; both Url and Label must be set for it to render.</summary>
    public string? ActionUrl { get; set; }
    public string? ActionLabel { get; set; }
}
