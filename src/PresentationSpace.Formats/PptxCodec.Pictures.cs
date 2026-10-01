using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private sealed record ImagePart(string Path, RasterHeader Header);
    private sealed class PictureParts(ZipArchive zip, XElement types, ISet<string> warnings)
    {
        private readonly Dictionary<string, ImagePart> _byId = [];
        private readonly Dictionary<string, ImagePart> _byContent = [];
        public ImagePart Add(PresentationAsset asset)
        {
            if (_byId.TryGetValue(asset.Id, out var known)) return known;
            byte[] bytes = Convert.FromBase64String(asset.Base64);
            var header = RasterHeader.Read(bytes);
            if (header.MimeType != asset.MimeType) warnings.Add("Picture content types were corrected from the embedded raster signatures.");
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!_byContent.TryGetValue(hash, out var part))
            {
                string path = $"ppt/media/image{_byContent.Count + 1}.{header.Extension}";
                using (var stream = zip.CreateEntry(path).Open()) stream.Write(bytes);
                types.Add(new XElement(CT + "Override", V("PartName", "/" + path), V("ContentType", header.MimeType)));
                part = new(path, header); _byContent.Add(hash, part);
            }
            _byId.Add(asset.Id, part); return part;
        }
    }
    private static XElement NativePicture(SlideShape shape, int id, string relationship, RasterHeader header)
    {
        var picture = PictureModel.Resolve(shape);
        var frame = PictureModel.Frame(picture, shape.Bounds.Width, shape.Bounds.Height, header.Width, header.Height);
        var transform = Transform(shape);
        if (picture.FlipHorizontal) transform.SetAttributeValue("flipH", 1);
        if (picture.FlipVertical) transform.SetAttributeValue("flipV", 1);
        var blip = new XElement(A + "blip", new XAttribute(R + "embed", relationship));
        float opacity = picture.Opacity * shape.Opacity;
        if (opacity < 1) blip.Add(new XElement(A + "alphaModFix", V("amt", (int)Math.Round(opacity * 100000))));
        return new(P + "pic",
            new XElement(P + "nvPicPr", new XElement(P + "cNvPr", V("id", id), V("name", shape.Name), V("descr", shape.AlternativeText), V("hidden", shape.Hidden ? 1 : 0)),
                new XElement(P + "cNvPicPr", new XElement(A + "picLocks", V("noChangeAspect", 1))), new XElement(P + "nvPr")),
            new XElement(P + "blipFill", blip, PictureRectangle("srcRect", frame.Source),
                new XElement(A + "stretch", PictureRectangle("fillRect", frame.Destination))),
            new XElement(P + "spPr", transform, new XElement(A + "prstGeom", V("prst", picture.Mask == PictureMask.Ellipse ? "ellipse" : "rect"), new XElement(A + "avLst")),
                Fill(shape.Picture is null ? "#00000000" : shape.Fill, shape.FillGradient, shape.Opacity),
                new XElement(A + "ln", V("w", E(shape.StrokeWidth)), Fill(shape.Stroke, shape.Opacity))));
    }
    private static XElement PictureRectangle(string name, RectF rectangle)
    {
        var i = PictureModel.Insets(rectangle);
        int l = (int)Math.Round(i.Left * 100000d), t = (int)Math.Round(i.Top * 100000d),
            r = (int)Math.Round(i.Right * 100000d), b = (int)Math.Round(i.Bottom * 100000d);
        if (100000L - l - r <= 0 || 100000L - t - b <= 0)
            throw new InvalidDataException("This picture frame is smaller than the PPTX percentage precision. Enlarge its visible region before exporting.");
        return new(A + name, V("l", l), V("t", t), V("r", r), V("b", b));
    }
    private static bool PictureFlag(XElement? element, string name, bool fallback = false) => (string?)element?.Attribute(name) switch
    {
        null => fallback, "true" or "1" => true, "false" or "0" => false,
        _ => throw new InvalidDataException("Invalid picture flag: " + name)
    };
    private static float PicturePercentage(XElement? element, string name, float fallback = 0)
    {
        string? text = (string?)element?.Attribute(name); if (text is null) return fallback;
        bool percent = text.EndsWith('%');
        if (percent) text = text[..^1];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            throw new InvalidDataException("Invalid picture percentage: " + name);
        number /= percent ? 100 : 100000;
        if (number is < -10 or > 10) throw new InvalidDataException("Picture percentage exceeds the supported range.");
        return (float)number;
    }
    private static PictureInsets ReadPictureRectangle(XElement? rectangle)
    {
        var insets = new PictureInsets(PicturePercentage(rectangle, "l"), PicturePercentage(rectangle, "t"), PicturePercentage(rectangle, "r"), PicturePercentage(rectangle, "b"));
        PictureModel.ValidateInsets(insets); return insets;
    }
    private static SlideShape ReadPicture(SlideShape shape, XElement node, XElement? transform, ICollection<string> warnings)
    {
        var fill = node.Element(P + "blipFill"); var blip = fill?.Element(A + "blip");
        float opacity = 1;
        foreach (var effect in blip?.Elements() ?? [])
        {
            if (effect.Name == A + "alphaModFix")
            {
                float amount = PicturePercentage(effect, "amt", 1);
                if (amount < 0) throw new InvalidDataException("Negative picture opacity.");
                if (amount > 1) warnings.Add("Picture alpha amplification above 100% is simplified.");
                opacity *= Math.Min(1, amount);
            }
            else if (effect.Name != A + "extLst") warnings.Add("Picture recoloring, filters and other unsupported blip effects are not rendered.");
        }
        string preset = (string?)node.Element(P + "spPr")?.Element(A + "prstGeom")?.Attribute("prst") ?? "rect";
        if (preset is not ("rect" or "ellipse") || node.Element(P + "spPr")?.Element(A + "custGeom") is not null)
            warnings.Add("Picture masks other than rectangle and ellipse are approximated as rectangles.");
        if (fill?.Element(A + "tile") is not null) warnings.Add("Tiled picture fills are approximated using stretch; source and destination framing are retained where available.");
        if (!PictureFlag(fill, "rotWithShape", true)) warnings.Add("A picture fill that does not rotate with its shape is approximated by a rotating fill.");
        if (node.Element(P + "spPr")?.Element(A + "effectLst")?.HasElements == true || node.Element(P + "spPr")?.Element(A + "effectDag") is not null)
            warnings.Add("Picture shadows, reflections and other shape effects are not rendered.");
        var picture = new PictureSpec { Fit = PictureFit.Stretch, Source = ReadPictureRectangle(fill?.Element(A + "srcRect")),
            Destination = ReadPictureRectangle(fill?.Element(A + "stretch")?.Element(A + "fillRect")),
            Mask = preset == "ellipse" ? PictureMask.Ellipse : PictureMask.Rectangle,
            FlipHorizontal = PictureFlag(transform, "flipH"), FlipVertical = PictureFlag(transform, "flipV"), Opacity = opacity };
        PictureModel.Validate(picture); return shape with { Kind = ShapeKind.Image, Picture = picture };
    }
}
