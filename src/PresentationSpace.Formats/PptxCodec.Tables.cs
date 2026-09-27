using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private static XElement NativeTable(SlideShape shape, int id)
    {
        var table = TableModel.Get(shape); var layout = new TableLayout(table, shape.Bounds);
        var xml = new XElement(A + "tbl", new XElement(A + "tblPr", V("firstRow", table.HeaderRow ? 1 : 0), V("bandRow", table.BandedRows ? 1 : 0), V("lastRow", table.TotalRow ? 1 : 0)),
            new XElement(A + "tblGrid", Enumerable.Range(0, table.ColumnCount).Select(c => new XElement(A + "gridCol", V("w", Math.Max(1, E(layout.X[c + 1] - layout.X[c])))))));
        for (int r = 0; r < table.RowCount; r++)
        {
            var row = new XElement(A + "tr", V("h", Math.Max(1, E(layout.Y[r + 1] - layout.Y[r]))));
            for (int c = 0; c < table.ColumnCount; c++)
            {
                var cell = layout.Owner(r, c); bool origin = r == cell.Row && c == cell.Column;
                var body = RichTextBody(TableModel.TextShape(table, origin ? cell : cell with { Text = "", TextRanges = [] })); body.Name = A + "txBody";
                foreach (string attribute in new[] { "lIns", "rIns", "tIns", "bIns", "anchor" }) body.Element(A + "bodyPr")!.SetAttributeValue(attribute, null);
                var style = TableModel.Style(table, cell);
                foreach (var paragraph in body.Elements(A + "p"))
                {
                    var run = RunProperties(style); run.Name = A + "defRPr"; paragraph.Element(A + "pPr")!.Add(run);
                }
                var properties = new XElement(A + "tcPr", V("marL", E(cell.MarginLeft)), V("marR", E(cell.MarginRight)), V("marT", E(cell.MarginTop)), V("marB", E(cell.MarginBottom)),
                    V("anchor", style.VerticalAlignment switch { VerticalAlignment.Middle => "ctr", VerticalAlignment.Bottom => "b", _ => "t" }));
                properties.Add(TableLine("lnL", cell.Left, shape.Opacity), TableLine("lnR", cell.Right, shape.Opacity), TableLine("lnT", cell.Top, shape.Opacity), TableLine("lnB", cell.Bottom, shape.Opacity), Fill(TableModel.Fill(table, cell), shape.Opacity));
                var tc = new XElement(A + "tc", body, properties);
                // DrawingML retains every physical grid cell, including covered continuations.
                if (r == cell.Row && cell.RowSpan > 1) tc.SetAttributeValue("rowSpan", cell.RowSpan);
                if (c == cell.Column && cell.ColumnSpan > 1) tc.SetAttributeValue("gridSpan", cell.ColumnSpan);
                if (r > cell.Row) tc.SetAttributeValue("vMerge", 1);
                if (c > cell.Column) tc.SetAttributeValue("hMerge", 1);
                row.Add(tc);
            }
            xml.Add(row);
        }
        return GraphicFrame(shape, id, xml, TableGraphicDataUri);
    }
    private static XElement TableLine(string name, TableBorder border, float opacity) => new(A + name, V("w", E(border.Width)),
        border.Width == 0 ? new XElement(A + "noFill") : Fill(border.Color, opacity),
        new XElement(A + "prstDash", V("val", border.Dash switch { TableBorderDash.Dash => "dash", TableBorderDash.Dot => "dot", _ => "solid" })));

    private static SlideShape ReadNativeTable(SlideShape shape, XElement xml, List<string> warnings, Func<XElement?, string, string> color)
    {
        var columns = xml.Element(A + "tblGrid")?.Elements(A + "gridCol").Take(TableModel.MaxColumns + 1).ToArray() ?? [];
        var rows = xml.Elements(A + "tr").Take(TableModel.MaxRows + 1).ToArray();
        if (columns.Length is < 1 or > TableModel.MaxColumns || rows.Length is < 1 or > TableModel.MaxRows) throw new InvalidDataException("Table grid exceeds 100 rows or columns, or is empty.");
        float Size(XElement node, string name)
        {
            if (!double.TryParse((string?)node.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value <= 0 || value / Emu > 100000) throw new InvalidDataException("Invalid DrawingML table track size.");
            return Math.Max(.01f, (float)(value / Emu));
        }
        int Span(XElement tc, string name, int maximum)
        {
            var attr = tc.Attribute(name); if (attr is null) return 1;
            if (!int.TryParse(attr.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1 || value > maximum) throw new InvalidDataException("Invalid DrawingML table span.");
            return value;
        }
        bool Flag(XElement node, string name)
        {
            var v = (string?)node.Attribute(name); return v switch { null or "0" or "false" => false, "1" or "true" => true, _ => throw new InvalidDataException("Invalid table merge flag.") };
        }
        var cells = ImmutableArray.CreateBuilder<TableCell>(); var owners = new TableCell?[rows.Length * columns.Length];
        long textLength = 0; var props = xml.Element(A + "tblPr");
        var table = new TableSpec { ColumnWidths = columns.Select(c => Size(c, "w")).ToImmutableArray(), RowHeights = rows.Select(r => Size(r, "h")).ToImmutableArray(),
            HeaderRow = props is not null && Flag(props, "firstRow"), BandedRows = props is not null && Flag(props, "bandRow"), TotalRow = props is not null && Flag(props, "lastRow") };
        bool unsupported = xml.Descendants().Any(e => e.Name == A + "gradFill" || e.Name == A + "blipFill" || e.Name == A + "pattFill" || e.Name == A + "lnTlToBr" || e.Name == A + "lnBlToTr" || e.Name == A + "cell3D");
        for (int r = 0; r < rows.Length; r++)
        {
            var physical = rows[r].Elements(A + "tc").Take(columns.Length + 1).ToArray();
            if (physical.Length != columns.Length) throw new InvalidDataException("Each DrawingML table row must match its grid column count.");
            for (int c = 0; c < columns.Length; c++)
            {
                var tc = physical[c]; bool hMerge = Flag(tc, "hMerge"), vMerge = Flag(tc, "vMerge"); int rs = Span(tc, "rowSpan", rows.Length), cs = Span(tc, "gridSpan", columns.Length);
                if (hMerge || vMerge)
                {
                    var owner = owners[r * columns.Length + c];
                    if (owner is null || hMerge != (c > owner.Column) || vMerge != (r > owner.Row) || (rs != 1 && (r != owner.Row || rs != owner.RowSpan)) || (cs != 1 && (c != owner.Column || cs != owner.ColumnSpan))) throw new InvalidDataException("Orphaned or inconsistent merged table continuation.");
                    if (tc.Descendants(A + "t").Any(t => t.Value.Length > 0)) warnings.Add("Hidden text in a covered table cell is not retained; the visible merge-origin text is preserved.");
                    continue;
                }
                if (r > rows.Length - rs || c > columns.Length - cs) throw new InvalidDataException("Table merge extends outside the grid.");
                var p = tc.Element(A + "tcPr"); var body = tc.Element(A + "txBody");
                var baseStyle = new TextStyle { FontSize = 20, VerticalAlignment = (string?)p?.Attribute("anchor") switch { "ctr" => VerticalAlignment.Middle, "b" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top } };
                baseStyle = ReadRunStyle(body?.Element(A + "p")?.Element(A + "pPr")?.Element(A + "defRPr") ?? body?.Element(A + "p")?.Element(A + "endParaRPr"), baseStyle, color);
                var content = ReadRichText(new SlideShape { TextStyle = baseStyle }, body, color);
                if ((textLength += content.Text.Length) > TableModel.MaxTextLength) throw new InvalidDataException("Table text exceeds the input limit.");
                float Margin(string name, float fallback)
                {
                    if (p?.Attribute(name) is null) return fallback;
                    double value = double.TryParse((string?)p.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n / Emu : double.NaN;
                    if (!double.IsFinite(value) || value < 0 || value > 1000) throw new InvalidDataException("Invalid table margin."); return (float)value;
                }
                TableBorder Border(string name)
                {
                    var line = p?.Element(A + name); if (line is null) return new();
                    string dash = (string?)line.Element(A + "prstDash")?.Attribute("val") ?? "solid";
                    if (dash is not ("solid" or "dash" or "dot" or "sysDot")) unsupported = true;
                    return new() { Color = color(line, "#D8DEE8"), Width = line.Element(A + "noFill") is not null ? 0 : Number(line, "w", Emu) / Emu,
                        Dash = dash switch { "dash" => TableBorderDash.Dash, "dot" or "sysDot" => TableBorderDash.Dot, _ => TableBorderDash.Solid } };
                }
                var cell = new TableCell { Row = r, Column = c, RowSpan = rs, ColumnSpan = cs, Text = content.Text, TextStyle = baseStyle, TextRanges = content.TextRanges,
                    Fill = color(p, "#FFFFFF"), Left = Border("lnL"), Right = Border("lnR"), Top = Border("lnT"), Bottom = Border("lnB"),
                    MarginLeft = Margin("marL", 9.6f), MarginRight = Margin("marR", 9.6f), MarginTop = Margin("marT", 4.8f), MarginBottom = Margin("marB", 4.8f) };
                for (int rr = r; rr < r + rs; rr++) for (int cc = c; cc < c + cs; cc++)
                { int i = rr * columns.Length + cc; if (owners[i] is not null) throw new InvalidDataException("Overlapping DrawingML table merges."); owners[i] = cell; }
                cells.Add(cell);
            }
        }
        if (props?.Element(A + "tableStyleId") is not null) warnings.Add("Referenced Office table-style effects are not fully resolved; explicit cell formatting is retained.");
        if (unsupported) warnings.Add("Table picture/gradient/pattern fills, diagonal/compound borders or 3D effects are simplified.");
        table = table with { Cells = cells.ToImmutable() }; TableModel.Validate(table);
        return TableModel.Apply(shape with { Text = "", TextRanges = [], TextStyle = table.TextStyle }, table);
    }
}
