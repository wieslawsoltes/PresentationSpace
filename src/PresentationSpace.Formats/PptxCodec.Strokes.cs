using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private static readonly string[] DashNames = ["solid", "dot", "dash", "lgDash", "dashDot", "lgDashDot", "lgDashDotDot", "sysDash", "sysDot", "sysDashDot", "sysDashDotDot"];
    private static readonly string[] EndNames = ["none", "triangle", "stealth", "diamond", "oval", "arrow"];
    private static readonly string[] EndSizes = ["sm", "med", "lg"];
    private static XElement NativeOutline(SlideShape shape)
    {
        var style = StrokeModel.Resolve(shape); StrokeModel.Validate(style);
        XElement End(string name, LineEnd end) => new(A + name, V("type", EndNames[(int)end.Kind]), V("w", EndSizes[(int)end.Width]), V("len", EndSizes[(int)end.Length]));
        var dash = style.CustomDashes.IsDefaultOrEmpty ? new XElement(A + "prstDash", V("val", DashNames[(int)style.Dash])) :
            new XElement(A + "custDash", style.CustomDashes.Select(p =>
            {
                long d = (long)Math.Round(p.Dash * 100000d), s = (long)Math.Round(p.Gap * 100000d);
                if (d + s == 0) throw new InvalidDataException("A dash pair is smaller than DrawingML percentage precision.");
                return new XElement(A + "ds", V("d", d), V("sp", s));
            }));
        return new(A + "ln", V("w", E(shape.StrokeWidth)), V("cap", style.Cap switch { StrokeCap.Flat => "flat", StrokeCap.Round => "rnd", _ => "sq" }),
            V("cmpd", "sng"), V("algn", "ctr"), shape.StrokeWidth == 0 ? new XElement(A + "noFill") : Fill(shape.Stroke, style.Gradient, shape.Opacity), dash,
            style.Join == StrokeJoin.Miter ? new XElement(A + "miter", V("lim", (int)Math.Round(style.MiterLimit * 100000d))) : new XElement(A + (style.Join == StrokeJoin.Bevel ? "bevel" : "round")),
            End("headEnd", style.Begin), End("tailEnd", style.End));
    }
    private static int ReadStrokeEnum(XElement element, string name, string[] choices, int fallback)
    {
        var raw = (string?)element.Attribute(name); if (raw is null) return fallback;
        int index = Array.IndexOf(choices, raw);
        return index >= 0 ? index : throw new InvalidDataException("Invalid DrawingML line value: " + name + "=" + raw);
    }
    private static SlideShape ReadStroke(SlideShape shape, XElement node, XElement? layout, XElement? master, XElement? theme, DrawingColors colors, ICollection<string> warnings)
    {
        if (!StrokeModel.Supports(shape)) return shape;
        var value = new StrokeSettings("#00000000", 0, new() { Cap = StrokeCap.Square, Join = StrokeJoin.Miter });
        bool found = false;
        void Apply(XElement outline, DrawingColor? placeholder = null)
        {
            found = true;
            var style = value.Style; string color = value.Color; float width = value.Width;
            if (outline.Attribute("w") is { } rawWidth)
            {
                if (!long.TryParse(rawWidth.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu) || emu < 0 || emu > 1000L * 9525)
                    throw new InvalidDataException("Invalid or unsupported DrawingML outline width.");
                width = (float)(emu / 9525d);
            }
            if (FillElement(outline) is { } fill)
            {
                var resolved = ReadFill(fill, colors, color, warnings, placeholder);
                color = resolved.Color; style = style with { Gradient = resolved.Gradient };
            }
            if (outline.Attribute("cap") is not null) style = style with { Cap = (StrokeCap)ReadStrokeEnum(outline, "cap", ["flat", "rnd", "sq"], 2) };
            if (outline.Element(A + "prstDash") is { } dash)
                style = style with { Dash = (StrokeDash)ReadStrokeEnum(dash, "val", DashNames, 0), CustomDashes = [] };
            if (outline.Element(A + "custDash") is { } custom)
            {
                var elements = custom.Elements(A + "ds").Take(StrokeModel.MaximumDashSegments + 1).ToArray();
                if (elements.Length is 0 or > StrokeModel.MaximumDashSegments) throw new InvalidDataException("Custom outline dashes require 1–32 pairs.");
                style = style with { Dash = StrokeDash.Solid, CustomDashes = elements.Select(e => new StrokeDashSegment(
                    (float)GradientNumber(e, "d", 0, 1000), (float)GradientNumber(e, "sp", 0, 1000))).ToImmutableArray() };
            }
            if (outline.Element(A + "round") is not null) style = style with { Join = StrokeJoin.Round };
            else if (outline.Element(A + "bevel") is not null) style = style with { Join = StrokeJoin.Bevel };
            else if (outline.Element(A + "miter") is { } miter)
                style = style with { Join = StrokeJoin.Miter, MiterLimit = miter.Attribute("lim") is null ? 4 : (float)GradientNumber(miter, "lim", 1, 100) };
            LineEnd ReadEnd(XElement element) => new()
            {
                Kind = (LineEndKind)ReadStrokeEnum(element, "type", EndNames, 0), Width = (LineEndSize)ReadStrokeEnum(element, "w", EndSizes, 1), Length = (LineEndSize)ReadStrokeEnum(element, "len", EndSizes, 1)
            };
            if (outline.Element(A + "headEnd") is { } begin) style = style with { Begin = ReadEnd(begin) };
            if (outline.Element(A + "tailEnd") is { } end) style = style with { End = ReadEnd(end) };
            if ((string?)outline.Attribute("cmpd") is not (null or "sng")) warnings.Add("Compound outlines are simplified to a single centered stroke.");
            if ((string?)outline.Attribute("algn") is not (null or "ctr")) warnings.Add("Inset outline alignment is simplified to a centered stroke.");
            value = new(color, width, style); StrokeModel.Validate(value);
        }
        // Resolve per-property overrides from the lowest to highest precedence;
        // sparse direct outlines retain inherited widths, fills and dash settings.
        foreach (var owner in new[] { master, layout, node })
        {
            if (owner?.Element(P + "style")?.Element(A + "lnRef") is { } reference)
            {
                if (!uint.TryParse((string?)reference.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out uint index)) throw new InvalidDataException("Invalid outline style reference.");
                if (index == 0) { value = value with { Color = "#00000000", Style = value.Style with { Gradient = null } }; found = true; }
                else
                {
                    var list = theme?.Element(A + "themeElements")?.Element(A + "fmtScheme")?.Element(A + "lnStyleLst");
                    var outline = index <= 64 ? list?.Elements(A + "ln").ElementAtOrDefault((int)index - 1) : null;
                    if (outline is null) warnings.Add("A missing outline style reference uses its inherited fallback.");
                    else Apply(outline, colors.Resolve(reference, "#000000"));
                }
            }
            if (owner?.Element(P + "spPr")?.Element(A + "ln") is { } direct) Apply(direct);
        }
        if (!found) return shape;
        return shape with { Stroke = value.Color, StrokeWidth = value.Width, Outline = value.Style,
            Kind = StrokeModel.IsLine(shape) && (value.Style.Begin.Kind != LineEndKind.None || value.Style.End.Kind != LineEndKind.None) ? ShapeKind.Arrow : shape.Kind };
    }
    private static RectF ReadLineBounds(XElement? off, XElement? ext, float tx, float ty, float sx, float sy)
    {
        float Coordinate(XElement? element, string name, long fallback, bool positive)
        {
            var raw = (string?)element?.Attribute(name);
            if (!long.TryParse(raw ?? fallback.ToString(CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ||
                (positive && v < 0) || v < -952500000L || v > 952500000L) throw new InvalidDataException("Invalid DrawingML line coordinate.");
            return (float)(v / 9525d);
        }
        return new(tx + Coordinate(off, "x", 0, false) * sx, ty + Coordinate(off, "y", 0, false) * sy,
            Coordinate(ext, "cx", 0, true) * sx, Coordinate(ext, "cy", 0, true) * sy);
    }
}
