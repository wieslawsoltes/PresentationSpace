using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private static XElement ChartValue(string tag, object value) => new(C + tag, V("val", value));
    private static string ColumnName(int index)
    {
        string value = ""; for (index++; index > 0; index = (index - 1) / 26) value = (char)('A' + (index - 1) % 26) + value;
        return value;
    }
    private static XElement ChartData(string tag, string formula, IEnumerable<string?> values, bool numeric)
    {
        var points = values.ToArray();
        var cache = new XElement(C + (numeric ? "numCache" : "strCache"));
        if (numeric) cache.Add(new XElement(C + "formatCode", "General"));
        cache.Add(ChartValue("ptCount", points.Length));
        for (int i = 0; i < points.Length; i++) if (points[i] is { } value) cache.Add(new XElement(C + "pt", V("idx", i), new XElement(C + "v", value)));
        return new(C + tag, new XElement(C + (numeric ? "numRef" : "strRef"), new XElement(C + "f", formula), cache));
    }

    private static XElement NativeChart(ZipArchive zip, XElement types, SlideShape shape, int id, int slide, List<XElement> slideRelationships)
    {
        var spec = ChartModel.Get(shape); ChartModel.Validate(spec);
        bool circular = ChartModel.IsCircular(spec.Kind), bar = spec.Kind == ChartKind.Bar;
        string stem = $"chart{slide}_{id}", chartPath = $"ppt/charts/{stem}.xml", workbookPath = $"ppt/embeddings/{stem}.xlsx", rid = "rIdChart" + id;
        slideRelationships.Add(Relation(rid, "chart", $"../charts/{stem}.xml"));
        string tag = spec.Kind switch { ChartKind.Line => "lineChart", ChartKind.Area => "areaChart", ChartKind.Pie => "pieChart", ChartKind.Doughnut => "doughnutChart", _ => "barChart" };
        string grouping = spec.Grouping switch { ChartGrouping.Stacked => "stacked", ChartGrouping.PercentStacked => "percentStacked", _ => spec.Kind is ChartKind.Line or ChartKind.Area ? "standard" : "clustered" };
        var plotType = new XElement(C + tag);
        if (spec.Kind is ChartKind.Column or ChartKind.Bar) plotType.Add(ChartValue("barDir", bar ? "bar" : "col"));
        if (!circular) plotType.Add(ChartValue("grouping", grouping));
        plotType.Add(ChartValue("varyColors", circular ? 1 : 0));
        for (int i = 0; i < spec.Series.Length; i++)
        {
            var data = spec.Series[i]; string column = ColumnName(i + 1);
            var series = new XElement(C + "ser", ChartValue("idx", i), ChartValue("order", i), ChartData("tx", $"Data!${column}$1", [data.Name], false));
            series.Add(new XElement(C + "spPr", spec.Kind == ChartKind.Line ? new XElement(A + "ln", V("w", E(2.5f)), Fill(data.Color, shape.Opacity)) : Fill(data.Color, shape.Opacity)));
            if (spec.Kind == ChartKind.Line) series.Add(new XElement(C + "marker", ChartValue("symbol", "circle"), ChartValue("size", 5)));
            if (circular) for (int point = 0; point < spec.Categories.Length; point++) series.Add(new XElement(C + "dPt", ChartValue("idx", point), new XElement(C + "spPr", Fill(ChartModel.PointColor(point), shape.Opacity))));
            series.Add(ChartData("cat", $"Data!$A$2:$A${spec.Categories.Length + 1}", spec.Categories, false),
                ChartData("val", $"Data!${column}$2:${column}${spec.Categories.Length + 1}", data.Values.Select(v => v?.ToString("R", CultureInfo.InvariantCulture)), true));
            if (spec.Kind == ChartKind.Line) series.Add(ChartValue("smooth", 0));
            plotType.Add(series);
        }
        plotType.Add(new XElement(C + "dLbls", ChartValue("showLegendKey", 0), ChartValue("showVal", spec.ShowValues && !circular ? 1 : 0), ChartValue("showCatName", 0), ChartValue("showSerName", 0), ChartValue("showPercent", spec.ShowValues && circular ? 1 : 0), ChartValue("showBubbleSize", 0)));
        if (spec.Kind is ChartKind.Column or ChartKind.Bar) plotType.Add(ChartValue("gapWidth", 150), ChartValue("overlap", spec.Grouping == ChartGrouping.Clustered ? 0 : 100));
        if (circular) plotType.Add(ChartValue("firstSliceAng", 0));
        if (spec.Kind == ChartKind.Doughnut) plotType.Add(ChartValue("holeSize", spec.HoleSize));
        if (!circular) plotType.Add(ChartValue("axId", 1), ChartValue("axId", 2));
        var plot = new XElement(C + "plotArea", new XElement(C + "layout"), plotType);
        if (!circular)
        {
            plot.Add(new XElement(C + "catAx", ChartValue("axId", 1), new XElement(C + "scaling", ChartValue("orientation", bar ? "maxMin" : "minMax")), ChartValue("delete", 0), ChartValue("axPos", bar ? "l" : "b"), ChartValue("majorTickMark", "none"), ChartValue("minorTickMark", "none"), ChartValue("tickLblPos", "nextTo"), ChartValue("crossAx", 2), ChartValue("crosses", "autoZero"), ChartValue("auto", 1), ChartValue("lblAlgn", "ctr"), ChartValue("lblOffset", 100)),
                new XElement(C + "valAx", ChartValue("axId", 2), new XElement(C + "scaling", ChartValue("orientation", "minMax")), ChartValue("delete", 0), ChartValue("axPos", bar ? "b" : "l"), new XElement(C + "majorGridlines"), new XElement(C + "numFmt", V("formatCode", spec.Grouping == ChartGrouping.PercentStacked ? "0%" : "General"), V("sourceLinked", 0)), ChartValue("majorTickMark", "none"), ChartValue("minorTickMark", "none"), ChartValue("tickLblPos", "nextTo"), ChartValue("crossAx", 1), ChartValue("crosses", bar ? "max" : "autoZero"), ChartValue("crossBetween", "between")));
        }
        var chart = new XElement(C + "chart");
        if (!string.IsNullOrEmpty(spec.Title)) chart.Add(new XElement(C + "title", new XElement(C + "tx", new XElement(C + "rich", new XElement(A + "bodyPr"), new XElement(A + "lstStyle"), new XElement(A + "p", new XElement(A + "r", new XElement(A + "rPr", V("lang", "en-US"), V("sz", 1800)), new XElement(A + "t", spec.Title))))), ChartValue("overlay", 0)));
        chart.Add(ChartValue("autoTitleDeleted", string.IsNullOrEmpty(spec.Title) ? 1 : 0), plot);
        if (spec.ShowLegend) chart.Add(new XElement(C + "legend", ChartValue("legendPos", "b"), ChartValue("overlay", 0)));
        chart.Add(ChartValue("plotVisOnly", 1), ChartValue("dispBlanksAs", spec.Blanks switch { ChartBlankMode.Zero => "zero", ChartBlankMode.Span => "span", _ => "gap" }), ChartValue("showDLblsOverMax", 0));
        var root = new XElement(C + "chartSpace", new XAttribute(XNamespace.Xmlns + "c", C), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R), ChartValue("date1904", 0), ChartValue("lang", "en-US"), ChartValue("roundedCorners", 0), chart, new XElement(C + "spPr", Fill(spec.Background, shape.Opacity)), new XElement(C + "externalData", new XAttribute(R + "id", "rIdWorkbook"), ChartValue("autoUpdate", 0)));
        WriteXml(zip, chartPath, root);
        WriteXml(zip, $"ppt/charts/_rels/{stem}.xml.rels", Relations([Relation("rIdWorkbook", "package", $"../embeddings/{stem}.xlsx")]));
        using (var stream = zip.CreateEntry(workbookPath).Open()) stream.Write(ChartWorkbook(spec));
        types.Add(new XElement(CT + "Override", V("PartName", "/" + chartPath), V("ContentType", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")), new XElement(CT + "Override", V("PartName", "/" + workbookPath), V("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")));
        return GraphicFrame(shape, id, new XElement(C + "chart", new XAttribute(R + "id", rid)), C.NamespaceName);
    }

    private static byte[] ChartWorkbook(ChartSpec spec)
    {
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var types = new XElement(CT + "Types", new XElement(CT + "Default", V("Extension", "rels"), V("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(CT + "Default", V("Extension", "xml"), V("ContentType", "application/xml")), new XElement(CT + "Override", V("PartName", "/xl/workbook.xml"), V("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")), new XElement(CT + "Override", V("PartName", "/xl/worksheets/sheet1.xml"), V("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
            WriteXml(zip, "[Content_Types].xml", types); WriteXml(zip, "_rels/.rels", Relations([Relation("rId1", "officeDocument", "xl/workbook.xml")]));
            WriteXml(zip, "xl/workbook.xml", new XElement(s + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(s + "sheets", new XElement(s + "sheet", V("name", "Data"), V("sheetId", 1), new XAttribute(R + "id", "rId1")))));
            WriteXml(zip, "xl/_rels/workbook.xml.rels", Relations([Relation("rId1", "worksheet", "worksheets/sheet1.xml")]));
            XElement TextCell(string address, string text) => new(s + "c", V("r", address), V("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            var header = new XElement(s + "row", V("r", 1), TextCell("A1", "Category"));
            for (int col = 0; col < spec.Series.Length; col++) header.Add(TextCell(ColumnName(col + 1) + "1", spec.Series[col].Name));
            var data = new XElement(s + "sheetData", header);
            for (int row = 0; row < spec.Categories.Length; row++)
            {
                var element = new XElement(s + "row", V("r", row + 2), TextCell("A" + (row + 2), spec.Categories[row]));
                for (int col = 0; col < spec.Series.Length; col++) if (spec.Series[col].Values[row] is { } number) element.Add(new XElement(s + "c", V("r", ColumnName(col + 1) + (row + 2)), V("t", "n"), new XElement(s + "v", number.ToString("R", CultureInfo.InvariantCulture))));
                data.Add(element);
            }
            WriteXml(zip, "xl/worksheets/sheet1.xml", new XElement(s + "worksheet", data));
        }
        return output.ToArray();
    }

    private sealed record ChartCache(int Count, Dictionary<int, string> Points);
    private static int ChartInteger(XElement? element, string attribute, int fallback, int maximum)
    {
        if (element?.Attribute(attribute) is not { } a) return fallback;
        if (!int.TryParse(a.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0 || value > maximum) throw new InvalidDataException("Invalid or oversized chart integer.");
        return value;
    }
    private static XElement? CacheElement(XElement? data, bool numeric) => data?.Element(C + (numeric ? "numLit" : "strLit")) ?? data?.Element(C + (numeric ? "numRef" : "strRef"))?.Element(C + (numeric ? "numCache" : "strCache"));
    private static ChartCache ReadChartCache(XElement cache)
    {
        int declared = ChartInteger(cache.Element(C + "ptCount"), "val", -1, ChartModel.MaxCategories);
        var points = new Dictionary<int, string>(); int highest = -1;
        foreach (var point in cache.Elements(C + "pt"))
        {
            int index = ChartInteger(point, "idx", -1, ChartModel.MaxCategories - 1);
            if (index < 0 || (declared >= 0 && index >= declared) || !points.TryAdd(index, point.Element(C + "v")?.Value ?? "")) throw new InvalidDataException("Invalid or duplicate chart point index.");
            highest = Math.Max(highest, index);
        }
        return new(declared < 0 ? highest + 1 : declared, points);
    }

    private static SlideShape? ReadNativeChart(SlideShape shape, XElement root, HashSet<string> warnings, Func<XElement?, string, string> color)
    {
        var chart = root.Element(C + "chart"); var plot = chart?.Element(C + "plotArea");
        var types = plot?.Elements().Where(e => e.Name.Namespace == C && e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)).ToArray() ?? [];
        if (types.Length != 1 || types[0].Name.LocalName is not ("barChart" or "lineChart" or "areaChart" or "pieChart" or "doughnutChart"))
        { warnings.Add("An unsupported chart type or combination chart was omitted; no partial series were imported."); return null; }
        var type = types[0]; string? direction = (string?)type.Element(C + "barDir")?.Attribute("val");
        var kind = type.Name.LocalName switch { "lineChart" => ChartKind.Line, "areaChart" => ChartKind.Area, "pieChart" => ChartKind.Pie, "doughnutChart" => ChartKind.Doughnut, _ => direction == "bar" ? ChartKind.Bar : ChartKind.Column };
        if (type.Name.LocalName == "barChart" && direction is not ("bar" or "col")) { warnings.Add("An unsupported chart direction was omitted."); return null; }
        string? groupingName = (string?)type.Element(C + "grouping")?.Attribute("val");
        var grouping = groupingName switch { "stacked" => ChartGrouping.Stacked, "percentStacked" => ChartGrouping.PercentStacked, _ => ChartGrouping.Clustered };
        if (groupingName is not (null or "standard" or "clustered" or "stacked" or "percentStacked") || (grouping != ChartGrouping.Clustered && kind is not (ChartKind.Column or ChartKind.Bar)))
        { warnings.Add("An unsupported chart grouping was omitted."); return null; }
        var nodes = type.Elements(C + "ser").Take(ChartModel.MaxSeries + 1).ToArray();
        if (nodes.Length > ChartModel.MaxSeries) throw new InvalidDataException("Chart exceeds the 32-series limit.");
        if (nodes.Length == 0 || (ChartModel.IsCircular(kind) && nodes.Length != 1)) { warnings.Add("An unsupported chart series count was omitted."); return null; }
        var indices = new HashSet<int>(); var orders = new HashSet<int>();
        for (int i = 0; i < nodes.Length; i++)
        {
            int index = ChartInteger(nodes[i].Element(C + "idx"), "val", i, int.MaxValue), order = ChartInteger(nodes[i].Element(C + "order"), "val", i, int.MaxValue);
            if (!indices.Add(index) || !orders.Add(order)) throw new InvalidDataException("Duplicate chart series index or order.");
        }
        nodes = nodes.OrderBy(n => ChartInteger(n.Element(C + "order"), "val", 0, int.MaxValue)).ToArray();
        var values = new List<ChartCache>(); var categories = new Dictionary<int, string>(); int count = 0;
        foreach (var node in nodes)
        {
            var cache = CacheElement(node.Element(C + "val"), true);
            if (cache is null) { warnings.Add("A chart without cached values was omitted. External workbooks are never loaded."); return null; }
            var numbers = ReadChartCache(cache); values.Add(numbers); count = Math.Max(count, numbers.Count);
            var categoryData = node.Element(C + "cat");
            if (categoryData?.Element(C + "multiLvlStrRef") is not null) { warnings.Add("An unsupported chart with multi-level categories was omitted."); return null; }
            var labels = CacheElement(categoryData, false) ?? CacheElement(categoryData, true);
            if (labels is not null)
            {
                var cat = ReadChartCache(labels); count = Math.Max(count, cat.Count);
                foreach (var (index, label) in cat.Points)
                {
                    if (categories.TryGetValue(index, out var previous) && previous != label) { warnings.Add("An unsupported chart with conflicting series categories was omitted."); return null; }
                    categories[index] = label;
                }
            }
        }
        if (count == 0) { warnings.Add("An empty native chart was omitted."); return null; }
        if ((long)nodes.Length * count > ChartModel.MaxValues) throw new InvalidDataException("Chart exceeds the 100,000-value limit.");
        var series = ImmutableArray.CreateBuilder<ChartSeries>();
        for (int i = 0; i < nodes.Length; i++)
        {
            var numbers = new double?[count];
            foreach (var (index, value) in values[i].Points)
            {
                if (string.IsNullOrEmpty(value)) continue;
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || Math.Abs(number) > 1e30) throw new InvalidDataException("Invalid numeric chart value.");
                if (number < 0 && (ChartModel.IsCircular(kind) || grouping == ChartGrouping.PercentStacked)) { warnings.Add("An unsupported chart with negative circular/percentage values was omitted."); return null; }
                numbers[index] = number;
            }
            var tx = nodes[i].Element(C + "tx"); var nameCache = CacheElement(tx, false);
            string name = tx?.Element(C + "v")?.Value ?? (nameCache is null ? null : ReadChartCache(nameCache).Points.GetValueOrDefault(0)) ?? $"Series {i + 1}";
            var properties = nodes[i].Element(C + "spPr");
            series.Add(new() { Name = name, Color = color(kind == ChartKind.Line ? properties?.Element(A + "ln") : properties, ChartModel.PointColor(i)), Values = numbers.ToImmutableArray() });
        }
        bool Flag(XElement? parent, string name) => (string?)parent?.Element(C + name)?.Attribute("val") is "1" or "true";
        var title = chart?.Element(C + "title")?.Element(C + "tx");
        var titleCache = CacheElement(title, false);
        string titleText = title?.Element(C + "rich") is { } rich ? string.Join('\n', rich.Elements(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value)))) : titleCache is null ? "" : ReadChartCache(titleCache).Points.GetValueOrDefault(0, "");
        string? blank = (string?)chart?.Element(C + "dispBlanksAs")?.Attribute("val");
        var spec = new ChartSpec
        {
            Kind = kind, Grouping = grouping, Title = titleText, Background = color(root.Element(C + "spPr"), "#FFFFFF"), ShowLegend = chart?.Element(C + "legend") is not null,
            ShowValues = Flag(type.Element(C + "dLbls"), "showVal") || Flag(type.Element(C + "dLbls"), "showPercent"),
            HoleSize = ChartInteger(type.Element(C + "holeSize"), "val", 55, 90),
            Blanks = blank switch { "zero" => ChartBlankMode.Zero, "span" => ChartBlankMode.Span, _ => ChartBlankMode.Gap },
            Categories = Enumerable.Range(0, count).Select(i => categories.GetValueOrDefault(i, (i + 1).ToString(CultureInfo.InvariantCulture))).ToImmutableArray(), Series = series.ToImmutable()
        };
        warnings.Add("Native chart data, type and basic options are preserved; advanced axis, point and label formatting is simplified.");
        return ChartModel.Apply(shape, spec);
    }
}
