using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private const string TableGraphicDataUri = "http://schemas.openxmlformats.org/drawingml/2006/table";
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
        return GraphicFrame(shape, id, table, TableGraphicDataUri);
    }

}
