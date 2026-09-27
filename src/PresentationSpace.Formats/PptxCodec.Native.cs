using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private const int MaxChartPoints = 10000;
    private static XElement Placeholder(SlideShape shape) => new(P + "ph", V("type", shape.Placeholder switch
    {
        PlaceholderKind.Title => "title", PlaceholderKind.Subtitle => "subTitle", PlaceholderKind.Body => "body",
        PlaceholderKind.Footer => "ftr", PlaceholderKind.SlideNumber => "sldNum", PlaceholderKind.Date => "dt", _ => "obj"
    }), V("idx", shape.PlaceholderIndex));

    private static PlaceholderKind ReadPlaceholder(XElement? placeholder) => placeholder is null ? PlaceholderKind.None : (string?)placeholder.Attribute("type") switch
    {
        "title" or "ctrTitle" => PlaceholderKind.Title, "subTitle" => PlaceholderKind.Subtitle, "body" => PlaceholderKind.Body,
        "ftr" => PlaceholderKind.Footer, "sldNum" => PlaceholderKind.SlideNumber, "dt" => PlaceholderKind.Date, _ => PlaceholderKind.Object
    };

    private static XElement RunProperties(TextStyle style) => new(A + "rPr", V("lang", "en-US"),
        V("sz", Math.Clamp((int)Math.Round(style.FontSize * 75), 100, 400000)), V("b", style.Bold ? 1 : 0),
        V("i", style.Italic ? 1 : 0), V("u", style.Underline ? "sng" : "none"), Fill(style.Color),
        new XElement(A + "latin", V("typeface", style.FontFamily)));

    private static XElement RichTextBody(SlideShape shape)
    {
        var body = new XElement(P + "txBody", new XElement(A + "bodyPr", V("wrap", "square"), V("lIns", E(3)), V("rIns", E(3)),
            V("tIns", E(3)), V("bIns", E(3)), V("anchor", shape.TextStyle.VerticalAlignment switch { VerticalAlignment.Middle => "ctr", VerticalAlignment.Bottom => "b", _ => "t" })), new XElement(A + "lstStyle"));
        int start = 0;
        foreach (var line in shape.Text.Split('\n'))
        {
            var style = RichText.StyleAt(shape, start);
            var paragraph = new XElement(A + "p", new XElement(A + "pPr", V("algn", style.Alignment switch { ParagraphAlignment.Center => "ctr", ParagraphAlignment.Right => "r", _ => "l" }),
                new XElement(A + "lnSpc", new XElement(A + "spcPct", V("val", (int)(style.LineSpacing * 100000)))),
                style.Bullets ? new XElement(A + "buChar", V("char", "•")) : new XElement(A + "buNone")));
            foreach (var range in RichText.Segments(shape, start, line.Length))
                paragraph.Add(new XElement(A + "r", RunProperties(range.Style), new XElement(A + "t", shape.Text.Substring(range.Start, range.Length).Replace("\r", ""))));
            paragraph.Add(new XElement(A + "endParaRPr", V("lang", "en-US")));
            body.Add(paragraph); start += line.Length + 1;
        }
        return body;
    }

    private static TextStyle ReadRunStyle(XElement? node, TextStyle fallback, Func<XElement?, string, string> color)
    {
        if (node is null) return fallback;
        bool Flag(string name, bool previous) => node.Attribute(name) is null ? previous : (string?)node.Attribute(name) is "1" or "true";
        return fallback with
        {
            FontSize = Math.Clamp(Number(node, "sz", fallback.FontSize * 75) / 75, 1, 2048),
            FontFamily = (string?)node.Element(A + "latin")?.Attribute("typeface") ?? fallback.FontFamily,
            Bold = Flag("b", fallback.Bold), Italic = Flag("i", fallback.Italic),
            Underline = node.Attribute("u") is null ? fallback.Underline : (string?)node.Attribute("u") is not "none",
            Color = color(node, fallback.Color)
        };
    }

    private static SlideShape ReadRichText(SlideShape shape, XElement? body, Func<XElement?, string, string> color)
    {
        if (body is null) return shape;
        var text = new System.Text.StringBuilder();
        var ranges = ImmutableArray.CreateBuilder<TextRangeStyle>();
        foreach (var paragraph in body.Elements(A + "p"))
        {
            if (text.Length > 0 || paragraph != body.Elements(A + "p").First()) text.Append('\n');
            var p = paragraph.Element(A + "pPr");
            var baseStyle = ReadRunStyle(p?.Element(A + "defRPr"), shape.TextStyle, color) with
            {
                Alignment = (string?)p?.Attribute("algn") switch { "ctr" => ParagraphAlignment.Center, "r" => ParagraphAlignment.Right, "l" => ParagraphAlignment.Left, _ => shape.TextStyle.Alignment },
                Bullets = p?.Element(A + "buNone") is not null ? false : p?.Element(A + "buChar") is not null || shape.TextStyle.Bullets,
                LineSpacing = Math.Clamp(Number(p?.Element(A + "lnSpc")?.Element(A + "spcPct"), "val", shape.TextStyle.LineSpacing * 100000) / 100000, .1f, 10)
            };
            foreach (var run in paragraph.Elements())
            {
                if (run.Name == A + "br") { text.Append('\n'); continue; }
                if (run.Name != A + "r" && run.Name != A + "fld") continue;
                string value = run.Element(A + "t")?.Value ?? "";
                if (value.Length == 0) continue;
                var style = ReadRunStyle(run.Element(A + "rPr"), baseStyle, color);
                if (style != shape.TextStyle) ranges.Add(new(text.Length, value.Length, style));
                text.Append(value);
            }
        }
        return shape with { Text = text.ToString(), TextRanges = ranges.ToImmutable() };
    }

    private static XElement GraphicFrame(SlideShape shape, int id, XElement data, string uri)
    {
        var transform = Transform(shape); transform.Name = P + "xfrm";
        return new(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr", new XElement(P + "cNvPr", V("id", id), V("name", shape.Name), V("descr", shape.AlternativeText), V("hidden", shape.Hidden ? 1 : 0)),
                new XElement(P + "cNvGraphicFramePr"), new XElement(P + "nvPr")), transform,
            new XElement(A + "graphic", new XElement(A + "graphicData", V("uri", uri), data)));
    }

    private static XElement NativeTable(SlideShape shape, int id)
    {
        int columns = shape.TableColumns, rows = Math.Max(1, (shape.Cells.Length + columns - 1) / columns);
        float rowHeight = shape.Bounds.Height / rows;
        var table = new XElement(A + "tbl", new XElement(A + "tblPr", V("firstRow", 1), V("bandRow", 1)),
            new XElement(A + "tblGrid", Enumerable.Range(0, columns).Select(_ => new XElement(A + "gridCol", V("w", E(shape.Bounds.Width / columns))))));
        for (int row = 0; row < rows; row++)
        {
            var tr = new XElement(A + "tr", V("h", E(rowHeight)));
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                var style = shape.TextStyle with { FontSize = Math.Min(shape.TextStyle.FontSize, Math.Max(9, rowHeight * .35f)), Color = row == 0 ? "#FFFFFF" : shape.TextStyle.Color, Bold = row == 0, VerticalAlignment = VerticalAlignment.Middle };
                var body = TextBody(index < shape.Cells.Length ? shape.Cells[index] : "", style); body.Name = A + "txBody";
                var properties = new XElement(A + "tcPr", V("marL", E(10)), V("marR", E(10)), V("marT", E(3)), V("marB", E(3)), V("anchor", "ctr"));
                foreach (string edge in new[] { "lnL", "lnR", "lnT", "lnB" }) properties.Add(new XElement(A + edge, V("w", E(1)), Fill("#D8DEE8")));
                properties.Add(Fill(row == 0 ? shape.Fill : row % 2 == 0 ? "#F1F4F8" : "#FFFFFF", shape.Opacity));
                tr.Add(new XElement(A + "tc", body, properties));
            }
            table.Add(tr);
        }
        return GraphicFrame(shape, id, table, A.NamespaceName + "/table");
    }

    private static XElement ChartData(string tag, string formula, IEnumerable<string> values, bool numeric)
    {
        var points = values.ToArray();
        var cache = new XElement(C + (numeric ? "numCache" : "strCache"));
        if (numeric) cache.Add(new XElement(C + "formatCode", "General"));
        cache.Add(new XElement(C + "ptCount", V("val", points.Length)), points.Select((value, i) => new XElement(C + "pt", V("idx", i), new XElement(C + "v", value))));
        return new(C + tag, new XElement(C + (numeric ? "numRef" : "strRef"), new XElement(C + "f", formula), cache));
    }

    private static XElement NativeChart(ZipArchive zip, XElement types, SlideShape shape, int id, int slide, List<XElement> slideRelationships)
    {
        var values = shape.Values.IsEmpty ? new float[] { 42, 68, 54, 89 } : shape.Values.ToArray();
        if (values.Length > MaxChartPoints) throw new InvalidDataException("Charts are limited to 10,000 points.");
        var labels = values.Select((_, i) => i < shape.Labels.Length ? shape.Labels[i] : (i + 1).ToString(CultureInfo.InvariantCulture)).ToArray();
        string stem = $"chart{slide}_{id}", chartPath = $"ppt/charts/{stem}.xml", workbookPath = $"ppt/embeddings/{stem}.xlsx", rid = "rIdChart" + id;
        slideRelationships.Add(Relation(rid, "chart", $"../charts/{stem}.xml"));
        var series = new XElement(C + "ser", new XElement(C + "idx", V("val", 0)), new XElement(C + "order", V("val", 0)),
            ChartData("tx", "Data!$B$1", [shape.Name], false), new XElement(C + "spPr", Fill(shape.Fill, shape.Opacity)),
            ChartData("cat", $"Data!$A$2:$A${values.Length + 1}", labels, false),
            ChartData("val", $"Data!$B$2:$B${values.Length + 1}", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)), true));
        XElement Value(string tag, object value) => new(C + tag, V("val", value));
        var categoryAxis = new XElement(C + "catAx", Value("axId", 1), new XElement(C + "scaling", Value("orientation", "minMax")), Value("delete", 0), Value("axPos", "b"), Value("majorTickMark", "none"), Value("minorTickMark", "none"), Value("tickLblPos", "nextTo"), Value("crossAx", 2), Value("crosses", "autoZero"), Value("auto", 1), Value("lblAlgn", "ctr"), Value("lblOffset", 100));
        var valueAxis = new XElement(C + "valAx", Value("axId", 2), new XElement(C + "scaling", Value("orientation", "minMax")), Value("delete", 0), Value("axPos", "l"), new XElement(C + "majorGridlines"), new XElement(C + "numFmt", V("formatCode", "General"), V("sourceLinked", 0)), Value("majorTickMark", "none"), Value("minorTickMark", "none"), Value("tickLblPos", "nextTo"), Value("crossAx", 1), Value("crosses", "autoZero"), Value("crossBetween", "between"));
        var chart = new XElement(C + "chartSpace", new XAttribute(XNamespace.Xmlns + "c", C), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R),
            Value("date1904", 0), Value("lang", "en-US"), Value("roundedCorners", 0), new XElement(C + "chart", Value("autoTitleDeleted", 1),
                new XElement(C + "plotArea", new XElement(C + "layout"), new XElement(C + "barChart", Value("barDir", "col"), Value("grouping", "clustered"), Value("varyColors", 0), series, Value("gapWidth", 150), Value("overlap", 0), Value("axId", 1), Value("axId", 2)), categoryAxis, valueAxis), Value("plotVisOnly", 1), Value("dispBlanksAs", "gap"), Value("showDLblsOverMax", 0)),
            new XElement(C + "externalData", new XAttribute(R + "id", "rIdWorkbook"), Value("autoUpdate", 0)));
        WriteXml(zip, chartPath, chart);
        WriteXml(zip, $"ppt/charts/_rels/{stem}.xml.rels", Relations([Relation("rIdWorkbook", "package", $"../embeddings/{stem}.xlsx")]));
        using (var stream = zip.CreateEntry(workbookPath).Open()) { var bytes = ChartWorkbook(labels, values, shape.Name); stream.Write(bytes); }
        types.Add(new XElement(CT + "Override", V("PartName", "/" + chartPath), V("ContentType", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")),
            new XElement(CT + "Override", V("PartName", "/" + workbookPath), V("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")));
        return GraphicFrame(shape, id, new XElement(C + "chart", new XAttribute(R + "id", rid)), C.NamespaceName);
    }

    private static byte[] ChartWorkbook(string[] labels, float[] values, string name)
    {
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var types = new XElement(CT + "Types", new XElement(CT + "Default", V("Extension", "rels"), V("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(CT + "Default", V("Extension", "xml"), V("ContentType", "application/xml")),
                new XElement(CT + "Override", V("PartName", "/xl/workbook.xml"), V("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(CT + "Override", V("PartName", "/xl/worksheets/sheet1.xml"), V("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
            WriteXml(zip, "[Content_Types].xml", types);
            WriteXml(zip, "_rels/.rels", Relations([Relation("rId1", "officeDocument", "xl/workbook.xml")]));
            WriteXml(zip, "xl/workbook.xml", new XElement(s + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(s + "sheets", new XElement(s + "sheet", V("name", "Data"), V("sheetId", 1), new XAttribute(R + "id", "rId1")))));
            WriteXml(zip, "xl/_rels/workbook.xml.rels", Relations([Relation("rId1", "worksheet", "worksheets/sheet1.xml")]));
            XElement TextCell(string address, string text) => new(s + "c", V("r", address), V("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            var data = new XElement(s + "sheetData", new XElement(s + "row", V("r", 1), TextCell("A1", "Category"), TextCell("B1", name)));
            for (int i = 0; i < values.Length; i++) data.Add(new XElement(s + "row", V("r", i + 2), TextCell("A" + (i + 2), labels[i]), new XElement(s + "c", V("r", "B" + (i + 2)), V("t", "n"), new XElement(s + "v", values[i].ToString("R", CultureInfo.InvariantCulture)))));
            WriteXml(zip, "xl/worksheets/sheet1.xml", new XElement(s + "worksheet", data));
        }
        return output.ToArray();
    }

    private static SlideShape? ReadNativeChart(SlideShape shape, XElement root, HashSet<string> warnings, Func<XElement?, string, string> color)
    {
        var plot = root.Element(C + "chart")?.Element(C + "plotArea"); var bars = plot?.Element(C + "barChart");
        var series = bars?.Elements(C + "ser").ToArray();
        if (bars is null || series?.Length != 1 || (string?)bars.Element(C + "barDir")?.Attribute("val") != "col" ||
            (string?)bars.Element(C + "grouping")?.Attribute("val") is "stacked" or "percentStacked" || plot!.Elements().Count(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)) != 1)
        { warnings.Add("Only single-series, unstacked native column charts are currently imported; an unsupported chart was omitted."); return null; }
        var ser = series[0];
        XElement? Cache(XElement? data, bool numeric) => data?.Element(C + (numeric ? "numLit" : "strLit")) ?? data?.Descendants(C + (numeric ? "numCache" : "strCache")).FirstOrDefault();
        var numberCache = Cache(ser.Element(C + "val"), true);
        if (numberCache is null) { warnings.Add("A chart without cached values was omitted. External workbooks are never loaded."); return null; }
        Dictionary<int, string> Points(XElement? cache)
        {
            var points = new Dictionary<int, string>();
            if (cache is null) return points;
            if (Number(cache.Element(C + "ptCount"), "val") > MaxChartPoints) throw new InvalidDataException("Chart cache exceeds the 10,000-point limit.");
            foreach (var point in cache.Elements(C + "pt"))
            {
                float raw = Number(point, "idx", -1);
                if (raw < 0 || raw >= MaxChartPoints || raw != MathF.Truncate(raw) || points.Count >= MaxChartPoints) throw new InvalidDataException("Invalid chart point index.");
                if (!points.TryAdd((int)raw, point.Element(C + "v")?.Value ?? "")) throw new InvalidDataException("Duplicate chart point index.");
            }
            return points;
        }
        var numbers = Points(numberCache); if (numbers.Count == 0) { warnings.Add("An empty native chart was omitted."); return null; }
        int count = Math.Max(numbers.Keys.Max() + 1, (int)Number(numberCache.Element(C + "ptCount"), "val"));
        var category = ser.Element(C + "cat"); var labels = Points(Cache(category, false) ?? Cache(category, true));
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (!numbers.TryGetValue(i, out string? value)) { warnings.Add("Missing chart points are represented as zero."); continue; }
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i])) throw new InvalidDataException("Invalid numeric chart value.");
        }
        warnings.Add("Native column-chart data is preserved; unsupported chart formatting and axis options are simplified.");
        return shape with { Kind = ShapeKind.Chart, Values = values.ToImmutableArray(), Labels = Enumerable.Range(0, count).Select(i => labels.GetValueOrDefault(i, (i + 1).ToString(CultureInfo.InvariantCulture))).ToImmutableArray(), Fill = color(ser.Element(C + "spPr"), shape.Fill) };
    }
}
