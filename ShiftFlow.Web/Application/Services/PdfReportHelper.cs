using ArabicRt;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace ShiftFlow.Application.Services;

/// <summary>
/// Shared iText7 building blocks so every PDF export (Dashboard, Assets, Contracts, Work Orders)
/// reads as an extension of the app itself — same pale canvas, white cards, muted-gray table
/// headers and primary blue as site.css's :root tokens — instead of a generic black-and-white
/// report each export used to hand-roll on its own. Colors below are the same HSL tokens from
/// site.css's :root block, converted to RGB once here.
/// </summary>
public static class PdfReportHelper
{
    public static readonly DeviceRgb PageBackground = new(235, 243, 244); // --background: 187 29% 94%
    public static readonly DeviceRgb CardBackground = new(255, 255, 255); // --card: 0 0% 100%
    public static readonly DeviceRgb Foreground = new(30, 41, 59); // --foreground: 215 28% 17%
    public static readonly DeviceRgb Primary = new(3, 105, 161); // --primary: 201 96% 32% (#0369A1)
    public static readonly DeviceRgb Success = new(25, 134, 83); // --success: 152 69% 31%
    public static readonly DeviceRgb Warning = new(245, 158, 11); // --warning: 38 92% 50%
    public static readonly DeviceRgb Danger = new(220, 38, 38); // --destructive: 354 70% 50%
    public static readonly DeviceRgb Info = new(14, 165, 233); // --chart-1: 199 89% 48% (#0EA5E9)
    public static readonly DeviceRgb Violet = new(124, 58, 237); // --chart-5: 262 52% 47%
    public static readonly DeviceRgb BorderColor = new(226, 232, 240); // --border: 220 13% 91%
    public static readonly DeviceRgb MutedBackground = new(243, 244, 246); // --muted: 220 14% 96%
    public static readonly DeviceRgb MutedText = new(107, 114, 128); // --muted-foreground: 220 9% 46%

    private static byte[]? _arabicFontBytes;

    /// <summary>iText's default Helvetica has no Arabic glyphs — Arabic text through it doesn't
    /// error, it just silently renders as nothing (confirmed live: a Dashboard export's "Group: "
    /// prefix vanished entirely under Arabic, leaving a bare ": Field Team A"). Call
    /// <c>doc.SetFont(GetFont(loc.IsRTL))</c> right after creating the Document so every element
    /// added afterward inherits a font that can actually draw the language in use.
    ///
    /// A PdfFont returned by PdfFontFactory.CreateFont becomes bound to whichever PdfDocument
    /// first flushes it — reusing the same PdfFont instance across a second, separate
    /// PdfDocument throws "Pdf indirect object belongs to other PDF document" (confirmed live:
    /// worked on the first export after app start, 500'd on every export after that). Only the
    /// font's raw bytes are safe to cache across requests; a fresh PdfFont is created from them
    /// every call.</summary>
    public static PdfFont GetFont(bool rtl)
    {
        if (!rtl) return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        _arabicFontBytes ??= File.ReadAllBytes(
            System.IO.Path.Combine(AppContext.BaseDirectory, "App_Data", "fonts", "Cairo-Variable.ttf"));
        return PdfFontFactory.CreateFont(_arabicFontBytes, PdfEncodings.IDENTITY_H);
    }

    /// <summary>Fills every page with the app's own pale canvas color instead of PDF's default
    /// white, so the white "card" sections added below actually read as cards sitting on a
    /// surface — the same relationship the live pages have. Call once per document, right after
    /// creating the PdfDocument.</summary>
    public static void ApplyPageBackground(PdfDocument pdf)
    {
        pdf.AddEventHandler(PdfDocumentEvent.START_PAGE, new PageBackgroundHandler(PageBackground));
    }

    private class PageBackgroundHandler(DeviceRgb color) : IEventHandler
    {
        public void HandleEvent(Event @event)
        {
            var docEvent = (PdfDocumentEvent)@event;
            var page = docEvent.GetPage();
            var size = page.GetPageSize();
            var canvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), docEvent.GetDocument());
            canvas.SaveState().SetFillColor(color)
                .Rectangle(size.GetLeft(), size.GetBottom(), size.GetWidth(), size.GetHeight())
                .Fill().RestoreState();
        }
    }

    /// <summary>Wraps a section in a white rounded card matching .card.border-0.shadow-sm —
    /// iText has no box-shadow, so a hairline border stands in for it.</summary>
    private static Div Card() =>
        new Div().SetBackgroundColor(CardBackground).SetBorder(new SolidBorder(BorderColor, 0.75f))
            .SetBorderRadius(new BorderRadius(8)).SetPadding(14).SetMarginBottom(14);

    /// <summary>iText7's free/core engine draws each Unicode codepoint's isolated glyph form and
    /// never reorders for RTL bidi — real Arabic contextual shaping (letters joining into their
    /// initial/medial/final forms) and bidi reordering are gated behind iText's paid pdfCalligraph
    /// add-on, which this app doesn't have. Without it, Arabic text renders as visibly broken
    /// disconnected letters (confirmed live). ArabicRt.Arabic.Fix pre-shapes and reorders the
    /// string into presentation-form glyphs in visual order before iText ever sees it — safe to
    /// call unconditionally, it's documented as a no-op on non-Arabic/already-shaped text, so
    /// every string flowing through this class is routed through it rather than threading an
    /// `rtl` flag through every method and call site.</summary>
    public static string Shape(string? text) => string.IsNullOrEmpty(text) ? text ?? "" : Arabic.Fix(text);

    public static void AddHeader(Document doc, string title, string? subtitle = null)
    {
        doc.Add(new Paragraph(Shape(title)).SetFontColor(Foreground).SetBold().SetFontSize(22).SetMarginBottom(2));
        var sub = subtitle ?? DateTime.Today.ToString("dddd, dd MMMM yyyy");
        doc.Add(new Paragraph(Shape(sub)).SetFontColor(MutedText).SetFontSize(10).SetMarginBottom(6));
        // Thin primary-blue rule under the title — the same accent color used for links, active
        // nav items and icons throughout the app, standing in for a literal logo/branding mark.
        doc.Add(new Div().SetHeight(3).SetWidth(UnitValue.CreatePointValue(64)).SetBackgroundColor(Primary).SetMarginBottom(16));
    }

    /// <summary>A row of KPI cards matching the live app's KPI cards: white card, small
    /// muted-uppercase label, big bold colored value, and a small color-tinted square standing in
    /// for the page's icon chip — an actual Bootstrap Icons glyph isn't available inside a PDF
    /// font, and the chip previously had nothing drawn inside it at all, so every export showed a
    /// row of blank pastel squares that read as broken/missing icons rather than a deliberate
    /// design choice (reported live). A bold single-letter monogram in the same font as the rest
    /// of the document (so it still shapes correctly for an Arabic label) needs no icon font and
    /// reads as an intentional icon stand-in instead of a missing asset.</summary>
    public static void AddKpiRow(Document doc, params (string Label, string Value, DeviceRgb Color)[] kpis)
    {
        if (kpis.Length == 0) return;
        var table = new Table(kpis.Length, true).UseAllAvailableWidth().SetMarginBottom(14);
        foreach (var (label, value, color) in kpis)
        {
            var card = Card().SetPadding(10).SetMarginBottom(0);
            var header = new Table(new float[] { 5f, 1f }).UseAllAvailableWidth();
            var textCell = new Cell().SetBorder(Border.NO_BORDER).SetPadding(0);
            textCell.Add(new Paragraph(Shape(label.ToUpperInvariant())).SetFontSize(7).SetFontColor(MutedText).SetBold().SetMargin(0));
            textCell.Add(new Paragraph(Shape(value)).SetFontSize(17).SetBold().SetFontColor(color).SetMarginTop(2).SetMarginBottom(0));
            header.AddCell(textCell);
            var chip = new Div().SetBackgroundColor(color).SetOpacity(0.12f).SetWidth(UnitValue.CreatePointValue(22)).SetHeight(UnitValue.CreatePointValue(22)).SetBorderRadius(new BorderRadius(6));
            var monogram = Shape(label.Trim().Length > 0 ? label.Trim().Substring(0, 1).ToUpperInvariant() : "•");
            chip.Add(new Paragraph(monogram).SetFontSize(11).SetBold().SetFontColor(color).SetMargin(0)
                .SetTextAlignment(TextAlignment.CENTER).SetMultipliedLeading(1f).SetPaddingTop(3));
            header.AddCell(new Cell().Add(chip).SetBorder(Border.NO_BORDER).SetPadding(0).SetVerticalAlignment(VerticalAlignment.TOP).SetTextAlignment(TextAlignment.RIGHT));
            card.Add(header);
            table.AddCell(new Cell().Add(card).SetBorder(Border.NO_BORDER).SetPadding(0).SetPaddingRight(6).SetPaddingBottom(6));
        }
        doc.Add(table);
    }

    /// <summary>
    /// Horizontal bar chart drawn as a label/proportional-bar/value table inside a white card —
    /// no external charting or image-rendering dependency needed, and it stays crisp at any PDF
    /// zoom level.
    /// </summary>
    public static void AddBarChart(Document doc, string title, IEnumerable<(string Label, int Value)> data, DeviceRgb barColor)
    {
        var rows = data.ToList();
        if (rows.Count == 0) return;
        var card = Card();
        card.Add(new Paragraph(Shape(title)).SetBold().SetFontColor(Foreground).SetFontSize(12).SetMarginBottom(8));
        var max = Math.Max(1, rows.Max(r => r.Value));
        // A percent-width Div nested inside another Div inside a table cell doesn't reliably
        // resolve against its immediate parent in iText7's layout pass — it renders as a sliver
        // regardless of the intended percentage. Both the track and the bar get an explicit
        // point width instead (comfortably under the ~2f:6f:1f column split's rendered width on
        // an A4 page), which lays out deterministically every time.
        const float trackWidthPt = 290f;
        var table = new Table(new float[] { 2f, 6f, 1f }).UseAllAvailableWidth();
        foreach (var (label, value) in rows)
        {
            table.AddCell(new Cell().Add(new Paragraph(Shape(label)).SetFontSize(9).SetFontColor(Foreground))
                .SetBorder(Border.NO_BORDER).SetVerticalAlignment(VerticalAlignment.MIDDLE));

            var barWidthPt = Math.Max(value * trackWidthPt / max, value > 0 ? 6f : 0f);
            var track = new Div().SetBackgroundColor(MutedBackground).SetHeight(14).SetWidth(UnitValue.CreatePointValue(trackWidthPt)).SetBorderRadius(new BorderRadius(3));
            track.Add(new Div().SetBackgroundColor(barColor).SetHeight(14).SetWidth(UnitValue.CreatePointValue(barWidthPt)).SetBorderRadius(new BorderRadius(3)));
            table.AddCell(new Cell().Add(track).SetBorder(Border.NO_BORDER).SetPadding(2).SetVerticalAlignment(VerticalAlignment.MIDDLE));

            table.AddCell(new Cell().Add(new Paragraph(value.ToString()).SetFontSize(9).SetBold().SetFontColor(Foreground))
                .SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetVerticalAlignment(VerticalAlignment.MIDDLE));
        }
        card.Add(table);
        doc.Add(card);
    }

    /// <summary>Table header styled after .table thead th: muted background, muted-gray
    /// uppercase text — not a solid brand-color band, matching every table in the live app.</summary>
    public static Table StyledTable(float[] columnWidths, string[] headers, float fontSize = 9)
    {
        // Fixed layout: iText7's default AUTO layout recomputes column widths from cell content
        // as a table paginates across multiple pages, and for a wide multi-column table that
        // recomputation silently dropped several columns' text entirely on every page past the
        // first (confirmed live on the 8-column Asset Register export) — fixed layout commits to
        // the given relative widths up front instead of re-deriving them mid-table.
        var table = new Table(columnWidths).UseAllAvailableWidth().SetFixedLayout().SetFontSize(fontSize)
            .SetBackgroundColor(CardBackground).SetBorder(new SolidBorder(BorderColor, 0.75f)).SetBorderRadius(new BorderRadius(8));
        foreach (var h in headers)
            table.AddHeaderCell(new Cell().Add(new Paragraph(Shape(h.ToUpperInvariant())).SetBold().SetFontColor(MutedText).SetFontSize(fontSize - 1))
                .SetBackgroundColor(MutedBackground).SetPadding(6).SetBorder(Border.NO_BORDER));
        return table;
    }

    /// <summary>Adds one zebra-striped row of plain-text cells to a table built by StyledTable.</summary>
    public static void AddRow(Table table, int rowIndex, float fontSize, params string[] cells)
    {
        foreach (var c in cells)
        {
            var cell = new Cell().Add(new Paragraph(Shape(c)).SetFontSize(fontSize).SetFontColor(Foreground)).SetPadding(5).SetBorder(Border.NO_BORDER);
            if (rowIndex % 2 == 1) cell.SetBackgroundColor(MutedBackground);
            table.AddCell(cell);
        }
    }
}
