using iText.Kernel.Colors;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace ShiftFlow.Application.Services;

/// <summary>
/// Shared iText7 building blocks so every PDF export (Dashboard, Assets, Contracts, Work Orders)
/// shares one visual language — banner header, KPI cards, horizontal bar charts, zebra-striped
/// tables — instead of each export hand-rolling its own plain black-and-white data dump.
/// </summary>
public static class PdfReportHelper
{
    public static readonly DeviceRgb Navy = new(30, 41, 59);
    public static readonly DeviceRgb Primary = new(37, 99, 235);
    public static readonly DeviceRgb Success = new(22, 163, 74);
    public static readonly DeviceRgb Warning = new(217, 119, 6);
    public static readonly DeviceRgb Danger = new(220, 38, 38);
    public static readonly DeviceRgb Info = new(8, 145, 178);
    public static readonly DeviceRgb Violet = new(124, 58, 237);
    public static readonly DeviceRgb LightGray = new(243, 244, 246);
    public static readonly DeviceRgb MutedText = new(107, 114, 128);

    public static void AddHeader(Document doc, string title, string? subtitle = null)
    {
        var banner = new Div().SetBackgroundColor(Navy).SetPadding(16).SetMarginBottom(18);
        banner.Add(new Paragraph(title).SetFontColor(ColorConstants.WHITE).SetBold().SetFontSize(20).SetMargin(0));
        var sub = subtitle ?? DateTime.Today.ToString("dddd, dd MMMM yyyy");
        banner.Add(new Paragraph(sub).SetFontColor(new DeviceRgb(203, 213, 225)).SetFontSize(10).SetMarginTop(4).SetMarginBottom(0));
        doc.Add(banner);
    }

    /// <summary>A row of colored KPI cards, e.g. AddKpiRow(doc, ("Total Assets", "142", Primary), ...).</summary>
    public static void AddKpiRow(Document doc, params (string Label, string Value, DeviceRgb Color)[] kpis)
    {
        if (kpis.Length == 0) return;
        var table = new Table(kpis.Length, true).UseAllAvailableWidth().SetMarginBottom(20);
        foreach (var (label, value, color) in kpis)
        {
            var inner = new Div().SetBackgroundColor(LightGray).SetPadding(10).SetBorderLeft(new SolidBorder(color, 3));
            inner.Add(new Paragraph(label.ToUpperInvariant()).SetFontSize(7).SetFontColor(MutedText).SetBold().SetMargin(0));
            inner.Add(new Paragraph(value).SetFontSize(18).SetBold().SetFontColor(color).SetMarginTop(2).SetMarginBottom(0));
            table.AddCell(new Cell().Add(inner).SetBorder(Border.NO_BORDER).SetPadding(0).SetPaddingRight(6).SetPaddingBottom(6));
        }
        doc.Add(table);
    }

    /// <summary>
    /// Horizontal bar chart drawn as a label/proportional-bar/value table — no external
    /// charting or image-rendering dependency needed, and it stays crisp at any PDF zoom level.
    /// </summary>
    public static void AddBarChart(Document doc, string title, IEnumerable<(string Label, int Value)> data, DeviceRgb barColor)
    {
        var rows = data.ToList();
        if (rows.Count == 0) return;
        doc.Add(new Paragraph(title).SetBold().SetFontSize(12).SetMarginBottom(8).SetMarginTop(4));
        var max = Math.Max(1, rows.Max(r => r.Value));
        // A percent-width Div nested inside another Div inside a table cell doesn't reliably
        // resolve against its immediate parent in iText7's layout pass — it renders as a sliver
        // regardless of the intended percentage. Both the track and the bar get an explicit
        // point width instead (comfortably under the ~2f:6f:1f column split's rendered width on
        // an A4 page), which lays out deterministically every time.
        const float trackWidthPt = 300f;
        var table = new Table(new float[] { 2f, 6f, 1f }).UseAllAvailableWidth().SetMarginBottom(18);
        foreach (var (label, value) in rows)
        {
            table.AddCell(new Cell().Add(new Paragraph(label).SetFontSize(9))
                .SetBorder(Border.NO_BORDER).SetVerticalAlignment(VerticalAlignment.MIDDLE));

            var barWidthPt = Math.Max(value * trackWidthPt / max, value > 0 ? 6f : 0f);
            var track = new Div().SetBackgroundColor(LightGray).SetHeight(14).SetWidth(UnitValue.CreatePointValue(trackWidthPt));
            track.Add(new Div().SetBackgroundColor(barColor).SetHeight(14).SetWidth(UnitValue.CreatePointValue(barWidthPt)));
            table.AddCell(new Cell().Add(track).SetBorder(Border.NO_BORDER).SetPadding(2).SetVerticalAlignment(VerticalAlignment.MIDDLE));

            table.AddCell(new Cell().Add(new Paragraph(value.ToString()).SetFontSize(9).SetBold())
                .SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetVerticalAlignment(VerticalAlignment.MIDDLE));
        }
        doc.Add(table);
    }

    public static Table StyledTable(float[] columnWidths, string[] headers, float fontSize = 9)
    {
        // Fixed layout: iText7's default AUTO layout recomputes column widths from cell content
        // as a table paginates across multiple pages, and for a wide multi-column table that
        // recomputation silently dropped several columns' text entirely on every page past the
        // first (confirmed live on the 8-column Asset Register export) — fixed layout commits to
        // the given relative widths up front instead of re-deriving them mid-table.
        var table = new Table(columnWidths).UseAllAvailableWidth().SetFixedLayout().SetFontSize(fontSize);
        foreach (var h in headers)
            table.AddHeaderCell(new Cell().Add(new Paragraph(h).SetBold().SetFontColor(ColorConstants.WHITE).SetFontSize(fontSize))
                .SetBackgroundColor(Navy).SetPadding(6).SetBorder(Border.NO_BORDER));
        return table;
    }

    /// <summary>Adds one zebra-striped row of plain-text cells to a table built by StyledTable.</summary>
    public static void AddRow(Table table, int rowIndex, float fontSize, params string[] cells)
    {
        foreach (var c in cells)
        {
            var cell = new Cell().Add(new Paragraph(c ?? "").SetFontSize(fontSize)).SetPadding(5).SetBorder(Border.NO_BORDER);
            if (rowIndex % 2 == 1) cell.SetBackgroundColor(LightGray);
            table.AddCell(cell);
        }
    }
}
