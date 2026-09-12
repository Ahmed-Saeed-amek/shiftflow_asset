using Microsoft.AspNetCore.Html;

namespace ShiftFlow.Web.ViewModels;

/// <summary>
/// Safer alternative to <see cref="PageHeaderModel.ActionsHtml"/>: the action buttons are an
/// already-rendered <see cref="IHtmlContent"/> (e.g. the result of a child partial) instead of a
/// hand-concatenated HTML string, so nothing has to be @Html.Raw'd back out.
/// <code>
/// @{ var actions = await Html.PartialAsync("_ExportButtons", new ExportButtonsModel { ExcelUrl = Url.Action("ExportExcel") }); }
/// &lt;partial name="_PageHeader" model='new PageHeaderWithActions { Title = Loc.T("Assets"), Actions = actions }' /&gt;
/// </code>
/// _PageHeader accepts either shape; ActionsHtml stays supported for existing callers.
/// </summary>
public class PageHeaderWithActions : PageHeaderModel
{
    public IHtmlContent? Actions { get; set; }
}

/// <summary>Model for the shared _ExportButtons partial (the Excel/PDF pair most list pages carry).</summary>
public class ExportButtonsModel
{
    /// <summary>Link for the Excel export; omit to hide the button.</summary>
    public string? ExcelUrl { get; set; }
    /// <summary>Link for the PDF export; omit to hide the button.</summary>
    public string? PdfUrl { get; set; }
    /// <summary>Bootstrap size modifier applied to both buttons.</summary>
    public string SizeCssClass { get; set; } = "btn-sm";
}
