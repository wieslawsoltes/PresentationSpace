using System.Globalization;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private readonly record struct DrawingColor(double Red, double Green, double Blue, double Alpha = 1)
    {
        private static int Byte(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
        public string Rgb => $"#{Byte(Red):X2}{Byte(Green):X2}{Byte(Blue):X2}";
        public string Argb => Alpha >= 1 ? Rgb : $"#{(int)(Math.Clamp(Alpha, 0, 1) * 255):X2}" + Rgb[1..];
    }
    private sealed class DrawingColors
    {
        private readonly Dictionary<string, XElement> _scheme = [];
        private readonly Dictionary<string, string> _map = new() { ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2" };
        private readonly ICollection<string> _warnings;
        public DrawingColors(XElement? theme, XElement? master, XElement? layout, XElement slide, ICollection<string> warnings)
        {
            _warnings = warnings;
            foreach (var c in theme?.Element(A + "themeElements")?.Element(A + "clrScheme")?.Elements() ?? [])
                if (c.Elements().FirstOrDefault() is { } color) _scheme[c.Name.LocalName] = color;
            var masterMap = master?.Element(P + "clrMap");
            var slideOverride = slide.Element(P + "clrMapOvr");
            var layoutOverride = layout?.Element(P + "clrMapOvr");
            var map = slideOverride is not null ? slideOverride.Element(A + "overrideClrMapping") ?? masterMap :
                layoutOverride?.Element(A + "overrideClrMapping") ?? masterMap;
            foreach (var a in map?.Attributes().Where(x => !x.IsNamespaceDeclaration) ?? []) _map[a.Name.LocalName] = a.Value;
        }
        private static DrawingColor ParseRgb(string value)
        {
            if (value.StartsWith('#')) value = value[1..];
            if (value.Length is not (6 or 8) || !uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint color))
                throw new InvalidDataException("Invalid DrawingML RGB color.");
            return new(((color >> 16) & 255) / 255d, ((color >> 8) & 255) / 255d, (color & 255) / 255d, value.Length == 8 ? (color >> 24) / 255d : 1);
        }
        private static bool ColorNode(XElement e) => e.Name.Namespace == A && e.Name.LocalName is "srgbClr" or "schemeClr" or "sysClr" or "scrgbClr" or "hslClr" or "prstClr";
        public string Read(XElement? container, string fallback) => Resolve(container, fallback).Argb;
        public DrawingColor Resolve(XElement? container, string fallback, DrawingColor? placeholder = null, int depth = 0)
        {
            if (depth > 16) throw new InvalidDataException("Cyclic or excessively nested theme color references.");
            var baseline = ParseRgb(fallback);
            if (container is null) return baseline;
            if (container.Name == A + "noFill" || container.Element(A + "noFill") is not null) return new(0, 0, 0, 0);
            var fill = container.Element(A + "solidFill") ?? container;
            var node = ColorNode(fill) ? fill : fill.Elements().FirstOrDefault(ColorNode);
            if (node is null) return baseline;
            DrawingColor result;
            switch (node.Name.LocalName)
            {
                case "srgbClr": result = ParseRgb((string?)node.Attribute("val") ?? ""); break;
                case "sysClr":
                    if (node.Attribute("lastClr") is not { } last) { _warnings.Add("A system color without lastClr uses a fallback color."); result = baseline; }
                    else result = ParseRgb(last.Value);
                    break;
                case "schemeClr":
                    string name = (string?)node.Attribute("val") ?? "";
                    if (name == "phClr") result = placeholder ?? baseline;
                    else
                    {
                        string mapped = _map.GetValueOrDefault(name, name);
                        if (_scheme.TryGetValue(mapped, out var scheme)) result = Resolve(scheme, fallback, placeholder, depth + 1);
                        else result = mapped switch { "dk1" => new(0, 0, 0), "lt1" => new(1, 1, 1), "dk2" => ParseRgb("#243247"), "lt2" => ParseRgb("#F4F5F7"), _ => baseline };
                        if (!_scheme.ContainsKey(mapped)) _warnings.Add("A missing theme color uses a fallback color.");
                    }
                    break;
                default:
                    _warnings.Add("Unsupported DrawingML color spaces or preset colors use a fallback; RGB, theme and system lastClr colors are supported.");
                    result = baseline; break;
            }
            int count = 0;
            foreach (var transform in node.Elements())
            {
                if (++count > 64) throw new InvalidDataException("Too many DrawingML color transforms.");
                string name = transform.Name.LocalName;
                if (transform.Name.Namespace != A) { _warnings.Add("An extension color transform was not applied."); continue; }
                if (name == "extLst") continue;
                if (name is not ("alpha" or "alphaMod" or "alphaOff" or "lum" or "lumMod" or "lumOff" or "sat" or "satMod" or "satOff" or "tint" or "shade"))
                { _warnings.Add("Unsupported DrawingML color transforms were not applied."); continue; }
                double n = GradientNumber(transform, "val", name.EndsWith("Off", StringComparison.Ordinal) ? -1 : 0, name.EndsWith("Mod", StringComparison.Ordinal) ? 100 : 1);
                switch (name)
                {
                    case "alpha": result = result with { Alpha = n }; break;
                    case "alphaMod": result = result with { Alpha = Math.Clamp(result.Alpha * n, 0, 1) }; break;
                    case "alphaOff": result = result with { Alpha = Math.Clamp(result.Alpha + n, 0, 1) }; break;
                    case "tint": result = result with { Red = 1 - (1 - result.Red) * n, Green = 1 - (1 - result.Green) * n, Blue = 1 - (1 - result.Blue) * n }; break;
                    case "shade": result = result with { Red = result.Red * n, Green = result.Green * n, Blue = result.Blue * n }; break;
                    default: result = AdjustHsl(result, name, n); break;
                }
            }
            return result;
        }
        private static DrawingColor AdjustHsl(DrawingColor c, string name, double value)
        {
            double max = Math.Max(c.Red, Math.Max(c.Green, c.Blue)), min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
            double delta = max - min, lum = (max + min) / 2, hue = 0, sat = 0;
            if (delta > 1e-12)
            {
                sat = delta / (1 - Math.Abs(2 * lum - 1));
                hue = (max == c.Red ? (c.Green - c.Blue) / delta + (c.Green < c.Blue ? 6 : 0) :
                    max == c.Green ? (c.Blue - c.Red) / delta + 2 : (c.Red - c.Green) / delta + 4) / 6;
            }
            double Adjust(double n) => Math.Clamp(name.EndsWith("Mod", StringComparison.Ordinal) ? n * value : name.EndsWith("Off", StringComparison.Ordinal) ? n + value : value, 0, 1);
            if (name.StartsWith("lum", StringComparison.Ordinal)) lum = Adjust(lum); else sat = Adjust(sat);
            double chroma = (1 - Math.Abs(2 * lum - 1)) * sat, x = chroma * (1 - Math.Abs(hue * 6 % 2 - 1)), m = lum - chroma / 2;
            (double r, double g, double b) = (hue * 6) switch
            {
                < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x)
            };
            return new(r + m, g + m, b + m, c.Alpha);
        }
    }
}
