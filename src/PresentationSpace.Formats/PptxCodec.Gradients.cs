using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private readonly record struct DrawingFill(string Color, GradientFill? Gradient);
    private static XElement Fill(string color, GradientFill? gradient, float opacity = 1)
    {
        if (gradient is null) return Fill(color, opacity);
        GradientModel.Validate(gradient);
        return new(A + "gradFill", V("rotWithShape", gradient.RotateWithShape ? 1 : 0),
            new XElement(A + "gsLst", gradient.Stops.Select(s => new XElement(A + "gs", V("pos", (int)Math.Round(s.Offset * 100000d)),
                new XElement(A + "srgbClr", V("val", s.Color[1..].ToUpperInvariant()),
                    new XElement(A + "alpha", V("val", (int)Math.Round(s.Opacity * opacity * 100000d))))))),
            new XElement(A + "lin", V("ang", (int)Math.Round(gradient.Angle * 60000d) % 21600000), V("scaled", gradient.Scaled ? 1 : 0)));
    }
    private static double GradientNumber(XElement element, string attribute, double minimum, double maximum)
    {
        string raw = (string?)element.Attribute(attribute) ?? throw new InvalidDataException("Missing DrawingML gradient/color value: " + attribute);
        bool percent = raw.EndsWith('%');
        if (!double.TryParse(percent ? raw[..^1] : raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))
            throw new InvalidDataException("Invalid DrawingML gradient/color percentage.");
        n /= percent ? 100 : 100000;
        if (n < minimum || n > maximum) throw new InvalidDataException("DrawingML gradient/color value exceeds supported limits.");
        return n;
    }
    private static bool GradientFlag(XElement? e, string attribute, bool fallback) => (string?)e?.Attribute(attribute) switch
    {
        null => fallback, "1" or "true" => true, "0" or "false" => false, _ => throw new InvalidDataException("Invalid gradient flag: " + attribute)
    };
    private static XElement? FillElement(XElement? properties) => properties?.Elements().FirstOrDefault(e => e.Name.Namespace == A &&
        e.Name.LocalName is "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill");
    private static DrawingFill ReadFill(XElement fill, DrawingColors colors, string fallback, ICollection<string> warnings, DrawingColor? placeholder = null)
    {
        if (fill.Name == A + "solidFill" || fill.Name == A + "noFill") return new(colors.Resolve(fill, fallback, placeholder).Argb, null);
        if (fill.Name != A + "gradFill") { warnings.Add("Picture/pattern/group fills on ordinary shapes or backgrounds are not yet rendered."); return new(fallback, null); }
        var list = fill.Element(A + "gsLst") ?? throw new InvalidDataException("Gradient has no stop list.");
        var nodes = list.Elements(A + "gs").Take(GradientModel.MaximumStops + 1).ToArray();
        if (nodes.Length is < 2 or > GradientModel.MaximumStops) throw new InvalidDataException("Gradient requires 2–64 stops.");
        var stops = ImmutableArray.CreateBuilder<GradientStop>(nodes.Length);
        foreach (var node in nodes)
        {
            if (!node.Elements().Any(e => e.Name.Namespace == A && e.Name.LocalName is "srgbClr" or "schemeClr" or "sysClr" or "hslClr" or "scrgbClr" or "prstClr"))
                throw new InvalidDataException("Gradient stop has no color.");
            var color = colors.Resolve(node, fallback, placeholder);
            stops.Add(new((float)GradientNumber(node, "pos", 0, 1), color.Rgb, (float)color.Alpha));
        }
        float angle = 0; var linear = fill.Element(A + "lin");
        if (linear?.Attribute("ang") is { } a)
        {
            if (!int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n is < 0 or >= 21600000)
                throw new InvalidDataException("Invalid DrawingML gradient angle.");
            angle = n / 60000f;
            if (angle >= 360) angle = MathF.BitDecrement(360);
        }
        var gradient = new GradientFill { Stops = stops.ToImmutable(), Angle = angle, Scaled = GradientFlag(linear, "scaled", false), RotateWithShape = GradientFlag(fill, "rotWithShape", true) };
        GradientModel.Validate(gradient);
        // Tiled/path fills require a different geometry model, not an arbitrary linear approximation.
        bool tile = false;
        if (fill.Element(A + "tileRect") is { } tileRect)
            foreach (var name in new[] { "l", "t", "r", "b" })
                if (tileRect.Attribute(name) is not null && GradientNumber(tileRect, name, -10, 10) != 0) tile = true;
        if (fill.Element(A + "path") is not null || tile || (string?)fill.Attribute("flip") is not (null or "none"))
        {
            warnings.Add("Path/radial or tiled gradients are simplified to their first stop; supported linear gradients remain editable.");
            var first = gradient.Stops[0];
            return new(new DrawingColor(Convert.ToInt32(first.Color.Substring(1, 2), 16) / 255d, Convert.ToInt32(first.Color.Substring(3, 2), 16) / 255d,
                Convert.ToInt32(first.Color.Substring(5, 2), 16) / 255d, first.Opacity).Argb, null);
        }
        return new(stops[0].Color, gradient);
    }
    private static DrawingFill? ReadFillReference(XElement? reference, XElement? theme, DrawingColors colors, string fallback, ICollection<string> warnings)
    {
        if (reference is null) return null;
        if (!uint.TryParse((string?)reference.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out uint index))
            throw new InvalidDataException("Invalid DrawingML fill reference.");
        if (index is 0 or 1000) return new("#00000000", null);
        var scheme = theme?.Element(A + "themeElements")?.Element(A + "fmtScheme");
        var list = scheme?.Element(A + (index >= 1001 ? "bgFillStyleLst" : "fillStyleLst"));
        uint offset = index >= 1001 ? index - 1001 : index - 1;
        var fill = offset < 64 ? list?.Elements().ElementAtOrDefault((int)offset) : null;
        if (fill is null) { warnings.Add("A missing or out-of-range theme fill reference uses a fallback color."); return new(fallback, null); }
        return ReadFill(fill, colors, fallback, warnings, colors.Resolve(reference, fallback));
    }
    private static DrawingFill ShapeFill(XElement shape, XElement? layout, XElement? master, XElement? theme, DrawingColors colors, string fallback, ICollection<string> warnings)
    {
        foreach (var node in new[] { shape, layout, master })
        {
            if (FillElement(node?.Element(P + "spPr")) is { } fill) return ReadFill(fill, colors, fallback, warnings);
            if (ReadFillReference(node?.Element(P + "style")?.Element(A + "fillRef"), theme, colors, fallback, warnings) is { } value) return value;
        }
        return new(fallback, null);
    }
    private static DrawingFill BackgroundFill(XElement slide, XElement? layout, XElement? master, XElement? theme, DrawingColors colors, ICollection<string> warnings)
    {
        foreach (var owner in new[] { slide, layout, master })
        {
            var background = owner?.Element(P + "cSld")?.Element(P + "bg");
            if (FillElement(background?.Element(P + "bgPr")) is { } fill) return ReadFill(fill, colors, "#FFFFFF", warnings);
            if (ReadFillReference(background?.Element(P + "bgRef"), theme, colors, "#FFFFFF", warnings) is { } referenced) return referenced;
        }
        return new("#FFFFFF", null);
    }
}
