// TEMPORARY — delete on merge with the shared-UI branch.
//
// The admin UI pass (branch ui/ui-admin) codes against three shared-layer APIs that are being
// built in parallel and are not in this worktree yet:
//   * PageHeaderModel.Breadcrumbs
//   * EmptyStateModel (+ the Views/Shared/_EmptyState.cshtml partial, which this file cannot
//     provide — the shared branch owns Views/Shared and must supply it)
//   * ShiftFlow.Web.Services.RoleDisplay
// The shapes below match the agreed API exactly so the real implementations drop in unchanged.
// Whoever merges: delete this file, and drop `partial` from PageHeaderModel in ViewModels.cs
// if the real Breadcrumbs property lands directly on it.

using System.Text;

namespace ShiftFlow.Web.ViewModels
{
    public partial class PageHeaderModel
    {
        /// <summary>Trail shown above the title. Label is already translated; Url null = current page.</summary>
        public List<(string Label, string? Url)>? Breadcrumbs { get; set; }
    }

    /// <summary>Model for the shared _EmptyState partial. Text is already translated by the caller.</summary>
    public class EmptyStateModel
    {
        public string Icon { get; set; } = "bi-inbox";
        public string Title { get; set; } = string.Empty;
        public string? Text { get; set; }
        public string? ActionUrl { get; set; }
        public string? ActionLabel { get; set; }
    }
}

namespace ShiftFlow.Web.Services
{
    /// <summary>
    /// Display form of an Identity role name: the Arabic name when the UI is in Arabic, otherwise
    /// the PascalCase role name split into words ("OperationsManager" → "Operations Manager").
    /// </summary>
    public static class RoleDisplay
    {
        public static string Name(string? roleName, string? nameAr, string lang)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return string.Empty;
            if (lang == "ar" && !string.IsNullOrWhiteSpace(nameAr)) return nameAr!;
            return Humanize(roleName!);
        }

        private static string Humanize(string value)
        {
            var sb = new StringBuilder(value.Length + 4);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(value[i - 1]) &&
                    (!char.IsUpper(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
