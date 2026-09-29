using System.Globalization;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    // DrawingML paragraph/body properties: see Microsoft Open XML documentation for
    // a:pPr (marL, marR, indent, defTabSz, spcBef, spcAft, lnSpc) and a:bodyPr insets/wrap.
    // Internal coordinates are 96-DPI slide units: one hundredth of a point = 1/75 unit.
    private static XElement SpacingPoints(float value) => new(A + "spcPts", V("val", (int)Math.Round(value * 75)));
    private static float TextNumber(XElement? element, string name, float fallback, float divisor, float min = 0, float max = 10000)
    {
        if (element?.Attribute(name) is not { } attribute) return fallback;
        if (!double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value / divisor < min || value / divisor > max)
            throw new InvalidDataException("Invalid DrawingML text layout property: " + name);
        return (float)(value / divisor);
    }
    private static TextStyle ReadParagraphStyle(XElement? p, TextStyle fallback)
    {
        if (p is null) return fallback;
        float Space(string name, float previous)
        {
            var node = p.Element(A + name);
            if (node?.Element(A + "spcPts") is { } points) return TextNumber(points, "val", previous, 75);
            // Percentage before/after is resolved to slide units against the paragraph font.
            // This is a bounded import approximation, not retained relative spacing.
            if (node?.Element(A + "spcPct") is { } percent)
                return Math.Clamp(TextNumber(percent, "val", 0, 100000, 0, 100) * fallback.FontSize, 0, 10000);
            return previous;
        }
        var line = p.Element(A + "lnSpc");
        var style = fallback with
        {
            Alignment = (string?)p.Attribute("algn") switch { "ctr" => ParagraphAlignment.Center, "r" => ParagraphAlignment.Right, "l" => ParagraphAlignment.Left, "just" => ParagraphAlignment.Justify, _ => fallback.Alignment },
            Bullets = p.Element(A + "buNone") is not null ? false : p.Element(A + "buChar") is not null || fallback.Bullets,
            LineSpacing = TextNumber(line?.Element(A + "spcPct"), "val", fallback.LineSpacing, 100000, .00001f, 10),
            LineSpacingPoints = line?.Element(A + "spcPts") is { } exact ? TextNumber(exact, "val", 1, 75, .01f) : line?.Element(A + "spcPct") is not null ? null : fallback.LineSpacingPoints,
            SpaceBefore = Space("spcBef", fallback.SpaceBefore), SpaceAfter = Space("spcAft", fallback.SpaceAfter),
            ParagraphLeftMargin = p.Attribute("marL") is null ? fallback.ParagraphLeftMargin : TextNumber(p, "marL", 0, Emu),
            ParagraphRightMargin = TextNumber(p, "marR", fallback.ParagraphRightMargin, Emu),
            ParagraphIndent = p.Attribute("indent") is null ? fallback.ParagraphIndent : TextNumber(p, "indent", 0, Emu, -10000),
            DefaultTabSize = TextNumber(p, "defTabSz", fallback.DefaultTabSize, Emu)
        };
        TextFlow.ValidateStyle(style); return style;
    }
    private static SlideShape ReadTextBox(SlideShape shape, ICollection<string> warnings, params XElement?[] hierarchy)
    {
        if (hierarchy.All(x => x is null)) return shape;
        XElement? Source(string attribute) => hierarchy.FirstOrDefault(x => x?.Attribute(attribute) is not null);
        // Per-attribute fallback follows shape -> layout -> master; absent insets use DrawingML defaults.
        var box = new TextBoxSpec
        {
            MarginLeft = TextNumber(Source("lIns"), "lIns", 9.6f, Emu), MarginRight = TextNumber(Source("rIns"), "rIns", 9.6f, Emu),
            MarginTop = TextNumber(Source("tIns"), "tIns", 4.8f, Emu), MarginBottom = TextNumber(Source("bIns"), "bIns", 4.8f, Emu),
            Wrap = (string?)Source("wrap")?.Attribute("wrap") switch { null or "square" => true, "none" => false, _ => throw new InvalidDataException("Unsupported text wrapping value.") }
        };
        var vertical = (string?)Source("vert")?.Attribute("vert");
        if (vertical is not null and not "horz") warnings.Add("Vertical or rotated text-body flow is not supported; the text remains horizontal.");
        if (TextNumber(Source("numCol"), "numCol", 1, 1, 1, 16) > 1) warnings.Add("Multi-column text is imported into a single text column.");
        if (hierarchy.Any(x => x?.Element(A + "normAutofit") is not null || x?.Element(A + "spAutoFit") is not null))
            warnings.Add("Persistent Office text autofit is not retained; use the explicit text fitting commands after import.");
        var style = shape.TextStyle with { VerticalAlignment = (string?)Source("anchor")?.Attribute("anchor") switch
            { "ctr" => VerticalAlignment.Middle, "b" => VerticalAlignment.Bottom, _ => shape.TextStyle.VerticalAlignment } };
        return shape with { TextBox = box, TextStyle = style };
    }
}
