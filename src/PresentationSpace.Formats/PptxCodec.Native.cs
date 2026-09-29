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
        float padding = shape.Kind == ShapeKind.Text ? 3 : 12;
        var body = new XElement(P + "txBody", new XElement(A + "bodyPr", V("wrap", "square"), V("lIns", E(padding)), V("rIns", E(padding)),
            V("tIns", E(padding)), V("bIns", E(padding)), V("anchor", shape.TextStyle.VerticalAlignment switch { VerticalAlignment.Middle => "ctr", VerticalAlignment.Bottom => "b", _ => "t" })), new XElement(A + "lstStyle"));
        XElement Paragraph(int offset)
        {
            var style = RichText.StyleAt(shape, offset);
            var defaults = RunProperties(style); defaults.Name = A + "defRPr";
            var properties = new XElement(A + "pPr", V("algn", style.Alignment switch { ParagraphAlignment.Center => "ctr", ParagraphAlignment.Right => "r", _ => "l" }),
                new XElement(A + "lnSpc", new XElement(A + "spcPct", V("val", (int)(style.LineSpacing * 100000)))),
                style.Bullets ? new XElement(A + "buChar", V("char", "•")) : new XElement(A + "buNone"), defaults);
            if (style.Bullets) { properties.Add(V("marL", E(style.FontSize * 1.25f))); properties.Add(V("indent", -E(style.FontSize * 1.25f))); }
            return new XElement(A + "p", properties);
        }
        var paragraph = Paragraph(0);
        void Runs(int start, int length)
        {
            foreach (var range in RichText.Segments(shape, start, length))
                paragraph.Add(new XElement(A + "r", RunProperties(range.Style), new XElement(A + "t", shape.Text.Substring(range.Start, range.Length))));
        }
        void Finish(int offset)
        {
            var end = RunProperties(RichText.StyleAt(shape, offset)); end.Name = A + "endParaRPr";
            paragraph.Add(end); body.Add(paragraph);
        }
        int start = 0;
        foreach (var token in TextFlow.Tokenize(shape.Text))
        {
            if (token.Kind is not (TextTokenKind.ParagraphBreak or TextTokenKind.LineBreak)) continue;
            Runs(start, token.Start - start);
            if (token.Kind == TextTokenKind.LineBreak)
                paragraph.Add(new XElement(A + "br", RunProperties(RichText.StyleAt(shape, token.Start))));
            else { Finish(token.Start); paragraph = Paragraph(token.Start + token.Length); }
            start = token.Start + token.Length;
        }
        Runs(start, shape.Text.Length - start); Finish(shape.Text.Length);
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
        bool firstParagraph = true;
        void Append(string value, TextStyle style)
        {
            if (value.Length == 0) return;
            if (text.Length > TextFlow.MaximumTextLength - value.Length) throw new InvalidDataException("Text exceeds one million UTF-16 code units.");
            if (style != shape.TextStyle)
            {
                if (ranges.Count > 0 && ranges[^1].Start + ranges[^1].Length == text.Length && ranges[^1].Style == style)
                    ranges[^1] = ranges[^1] with { Length = ranges[^1].Length + value.Length };
                else ranges.Add(new(text.Length, value.Length, style));
            }
            text.Append(value);
        }
        foreach (var paragraph in body.Elements(A + "p"))
        {
            if (!firstParagraph) Append("\n", shape.TextStyle);
            firstParagraph = false;
            var p = paragraph.Element(A + "pPr");
            var baseStyle = ReadRunStyle(p?.Element(A + "defRPr"), shape.TextStyle, color) with
            {
                Alignment = (string?)p?.Attribute("algn") switch { "ctr" => ParagraphAlignment.Center, "r" => ParagraphAlignment.Right, "l" => ParagraphAlignment.Left, _ => shape.TextStyle.Alignment },
                Bullets = p?.Element(A + "buNone") is not null ? false : p?.Element(A + "buChar") is not null || shape.TextStyle.Bullets,
                LineSpacing = Math.Clamp(Number(p?.Element(A + "lnSpc")?.Element(A + "spcPct"), "val", shape.TextStyle.LineSpacing * 100000) / 100000, .1f, 10)
            };
            foreach (var run in paragraph.Elements())
            {
                if (run.Name == A + "br") { Append("\v", ReadRunStyle(run.Element(A + "rPr"), baseStyle, color)); continue; }
                if (run.Name != A + "r" && run.Name != A + "fld") continue;
                Append(run.Element(A + "t")?.Value ?? "", ReadRunStyle(run.Element(A + "rPr"), baseStyle, color));
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

}
