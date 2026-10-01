using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PresentationSpace.Core;

public enum ShapeKind { Text, Rectangle, RoundRectangle, Ellipse, Triangle, Diamond, Line, Arrow, Image, Table, Chart }
public enum ParagraphAlignment { Left, Center, Right, Justify }
public enum VerticalAlignment { Top, Middle, Bottom }
public enum TransitionKind { None, Fade, Push, Wipe }
public enum AnimationKind { None, Appear, Fade, FlyIn }
public enum PlaceholderKind { None, Title, Subtitle, Body, Object, Footer, SlideNumber, Date }
public enum AlignKind { Left, Center, Right, Top, Middle, Bottom }

public readonly record struct PointF(float X, float Y);
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    [JsonIgnore] public float Right => X + Width;
    [JsonIgnore] public float Bottom => Y + Height;
    [JsonIgnore] public PointF Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(PointF p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public bool Intersects(RectF r) => X <= r.Right && Right >= r.X && Y <= r.Bottom && Bottom >= r.Y;
    public static RectF Between(PointF a, PointF b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

public sealed record TextStyle
{
    public string FontFamily { get; init; } = "Arial";
    public float FontSize { get; init; } = 28;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Bullets { get; init; }
    public string Color { get; init; } = "#243247";
    public ParagraphAlignment Alignment { get; init; }
    public VerticalAlignment VerticalAlignment { get; init; }
    public float LineSpacing { get; init; } = 1.15f;
    /// <summary>Absolute baseline advance in slide units; null uses LineSpacing. Small values deliberately permit overlapping lines.</summary>
    public float? LineSpacingPoints { get; init; }
    public float SpaceBefore { get; init; }
    public float SpaceAfter { get; init; }
    /// <summary>Null follows the legacy bullet indent (1.25 em), or zero for ordinary text.</summary>
    public float? ParagraphLeftMargin { get; init; }
    public float ParagraphRightMargin { get; init; }
    /// <summary>Relative first-line offset; for bullets, positions the marker relative to the text start.</summary>
    public float? ParagraphIndent { get; init; }
    /// <summary>Zero uses four space advances; otherwise an explicit tab interval in slide units.</summary>
    public float DefaultTabSize { get; init; }
    /// <summary>Ordered custom stops measured from the text body's left content edge. Empty uses regular intervals.</summary>
    public ImmutableArray<TextTabStop> TabStops { get; init; } = [];
}

/// <summary>A non-overlapping UTF-16 text range with explicit character formatting.</summary>
public sealed record TextRangeStyle(int Start, int Length, TextStyle Style);

public sealed record SlideShape
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Shape";
    public ShapeKind Kind { get; init; } = ShapeKind.Rectangle;
    public RectF Bounds { get; init; } = new(100, 100, 260, 140);
    public float Rotation { get; init; }
    public string Fill { get; init; } = "#D35230";
    public string Stroke { get; init; } = "#00000000";
    public float StrokeWidth { get; init; } = 1.5f;
    public float Opacity { get; init; } = 1;
    public string Text { get; init; } = "";
    public TextStyle TextStyle { get; init; } = new();
    public TextBoxSpec? TextBox { get; init; }
    public ImmutableArray<TextRangeStyle> TextRanges { get; init; } = [];
    public PlaceholderKind Placeholder { get; init; }
    public int PlaceholderIndex { get; init; }
    public string AlternativeText { get; init; } = "";
    public string? AssetId { get; init; }
    public PictureSpec? Picture { get; init; }
    public Guid? GroupId { get; init; }
    public bool Locked { get; init; }
    public bool Hidden { get; init; }
    public AnimationKind Animation { get; init; }
    public int AnimationOrder { get; init; }
    public float AnimationDuration { get; init; } = 0.5f;
    public int TableColumns { get; init; } = 3;
    public ImmutableArray<string> Cells { get; init; } = [];
    public ImmutableArray<float> Values { get; init; } = [];
    public ChartSpec? Chart { get; init; }
    public TableSpec? Table { get; init; }
    public ImmutableArray<string> Labels { get; init; } = [];
}

public sealed record SlideComment(Guid Id, string Author, string Text, DateTimeOffset Created, bool Resolved = false);
public sealed record PresentationAsset(string Id, string MimeType, string Base64);
public sealed record Slide
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Untitled slide";
    public string Background { get; init; } = "#FFFFFF";
    public ImmutableArray<SlideShape> Shapes { get; init; } = [];
    public string? LayoutName { get; init; }
    public string Notes { get; init; } = "";
    public bool Hidden { get; init; }
    public TransitionKind Transition { get; init; }
    public float TransitionDuration { get; init; } = 0.5f;
    public ImmutableArray<SlideComment> Comments { get; init; } = [];
}

public sealed record PresentationDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string Title { get; init; } = "Presentation";
    public float Width { get; init; } = 1280;
    public float Height { get; init; } = 720;
    public string Theme { get; init; } = "Office";
    public ImmutableArray<Slide> Slides { get; init; } = [new()];
    public ImmutableDictionary<string, PresentationAsset> Assets { get; init; } = ImmutableDictionary<string, PresentationAsset>.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PresentationDocument))]
[JsonSerializable(typeof(SlideShape[]))]
public partial class PresentationJsonContext : JsonSerializerContext;

public static class DocumentSerializer
{
    public const int MaxFileBytes = 64 * 1024 * 1024;
    public static string Serialize(PresentationDocument document)
    {
        return JsonSerializer.Serialize(document with { SchemaVersion = RequiredSchema(document) }, PresentationJsonContext.Default.PresentationDocument);
    }
    private static int RequiredSchema(PresentationDocument document)
    {
        int version = document.SchemaVersion;
        foreach (var slide in document.Slides) foreach (var shape in slide.Shapes)
        {
            if (shape.Picture is not null) version = Math.Max(version, 7);
            if (shape.Chart is not null) version = Math.Max(version, 2);
            if (shape.Table is { } table)
            {
                version = Math.Max(version, table.FirstColumn || table.LastColumn || table.BandedColumns ? 4 : 3);
                if (TextBoxModel.HasParagraphLayout(table.TextStyle) || table.Cells.Any(c =>
                    c.TextStyle is not null && TextBoxModel.HasParagraphLayout(c.TextStyle) || c.TextRanges.Any(r => TextBoxModel.HasParagraphLayout(r.Style)))) version = Math.Max(version, 5);
            }
            if (shape.TextBox is not null || TextBoxModel.HasParagraphLayout(shape.TextStyle) || shape.TextRanges.Any(r => TextBoxModel.HasParagraphLayout(r.Style))) version = Math.Max(version, 5);
            if (!shape.TextStyle.TabStops.IsDefaultOrEmpty || shape.TextRanges.Any(r => !r.Style.TabStops.IsDefaultOrEmpty) ||
                shape.Table is { } tabTable && (!tabTable.TextStyle.TabStops.IsDefaultOrEmpty || tabTable.Cells.Any(c =>
                    c.TextStyle is not null && !c.TextStyle.TabStops.IsDefaultOrEmpty || c.TextRanges.Any(r => !r.Style.TabStops.IsDefaultOrEmpty))))
                version = Math.Max(version, 6);
        }
        return version;
    }

    public static PresentationDocument Deserialize(string json)
    {
        if (json.Length > MaxFileBytes) throw new InvalidDataException("Presentation exceeds the 64 MB input limit.");
        var document = JsonSerializer.Deserialize(json, PresentationJsonContext.Default.PresentationDocument) ?? throw new InvalidDataException("Empty presentation.");
        Validate(document);
        return document;
    }
    public static void Validate(PresentationDocument d)
    {
        if (d.SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7)) throw new InvalidDataException($"Unsupported document version {d.SchemaVersion}.");
        if (!float.IsFinite(d.Width) || !float.IsFinite(d.Height) || d.Width < 1 || d.Height < 1 || d.Width > 16384 || d.Height > 16384) throw new InvalidDataException("Invalid slide dimensions.");
        if (d.Slides.IsDefaultOrEmpty || d.Slides.Length > 2000) throw new InvalidDataException("A presentation must contain 1–2,000 slides.");
        var ids = new HashSet<Guid>();
        int total = 0;
        foreach (var slide in d.Slides)
        {
            if (slide is null || !ids.Add(slide.Id) || slide.Shapes.IsDefault || slide.Comments.IsDefault) throw new InvalidDataException("Invalid or duplicate slide.");
            if (!float.IsFinite(slide.TransitionDuration) || slide.TransitionDuration < 0 || slide.TransitionDuration > 60) throw new InvalidDataException("Invalid transition duration.");
            foreach (var s in slide.Shapes)
            {
                if (++total > 20000 || s is null || !ids.Add(s.Id)) throw new InvalidDataException("Too many shapes or duplicate identifiers.");
                var b = s.Bounds;
                if (!float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height) || b.Width <= 0 || b.Height <= 0 || Math.Abs(b.X) > 100000 || Math.Abs(b.Y) > 100000 || b.Width > 100000 || b.Height > 100000) throw new InvalidDataException("Invalid shape geometry.");
                if (s.TextStyle is null || !float.IsFinite(s.TextStyle.FontSize) || s.TextStyle.FontSize < 1 || s.TextStyle.FontSize > 2048 || !float.IsFinite(s.Rotation) || !float.IsFinite(s.Opacity) || s.Opacity < 0 || s.Opacity > 1 || !float.IsFinite(s.StrokeWidth) || s.StrokeWidth < 0 || s.StrokeWidth > 1000 || !float.IsFinite(s.TextStyle.LineSpacing) || s.TextStyle.LineSpacing <= 0 || s.TextStyle.LineSpacing > 10) throw new InvalidDataException("Invalid shape styling.");
                if (s.Text is null || s.Text.Length > TextFlow.MaximumTextLength || s.TextRanges.IsDefault || !Enum.IsDefined(s.Placeholder) || s.PlaceholderIndex < 0) throw new InvalidDataException("Invalid text or placeholder.");
                if (s.Picture is { } picture)
                {
                    if (s.Kind != ShapeKind.Image) throw new InvalidDataException("Picture properties require an image shape.");
                    PictureModel.Validate(picture);
                }
                TextFlow.ValidateStyle(s.TextStyle);
                if (s.TextBox is { } textBox) TextBoxModel.Validate(textBox);
                int rangeEnd = 0;
                foreach (var range in s.TextRanges)
                {
                    if (range is null || range.Style is null || range.Start < rangeEnd || range.Length <= 0 || range.Start > s.Text.Length - range.Length || !RichText.IsBoundary(s.Text, range.Start) || !RichText.IsBoundary(s.Text, range.Start + range.Length)) throw new InvalidDataException("Invalid rich-text range.");
                    var style = range.Style;
                    TextFlow.ValidateStyle(style);
                    if (!float.IsFinite(style.FontSize) || style.FontSize < 1 || style.FontSize > 2048 || !float.IsFinite(style.LineSpacing) || style.LineSpacing <= 0 || style.LineSpacing > 10) throw new InvalidDataException("Invalid rich-text style.");
                    rangeEnd = range.Start + range.Length;
                }
                if (s.Cells.IsDefault || s.Values.IsDefault || s.Labels.IsDefault || s.TableColumns < 1 || s.TableColumns > 100 || s.Values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid table or chart data.");
                if (s.Chart is { } chart) ChartModel.Validate(chart);
                if (s.Table is { } table) TableModel.Validate(table);
                if (s.Values.Length > ChartModel.MaxCategories) throw new InvalidDataException("Too many legacy chart values.");
                if (s.AssetId is { } asset && !d.Assets.ContainsKey(asset)) throw new InvalidDataException("Missing image asset.");
            }
        }
        foreach (var (key, asset) in d.Assets)
        {
            if (asset is null || key != asset.Id || asset.Base64.Length > 32 * 1024 * 1024) throw new InvalidDataException("Invalid or oversized asset.");
            try { _ = Convert.FromBase64String(asset.Base64); } catch (FormatException e) { throw new InvalidDataException("Invalid image encoding.", e); }
        }
    }
}
