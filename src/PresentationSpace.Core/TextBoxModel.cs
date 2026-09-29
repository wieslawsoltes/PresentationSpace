namespace PresentationSpace.Core;

/// <summary>Shape text insets in slide coordinates. Null on a shape preserves legacy padding.</summary>
public sealed record TextBoxSpec
{
    public float MarginLeft { get; init; } = 3;
    public float MarginRight { get; init; } = 3;
    public float MarginTop { get; init; } = 3;
    public float MarginBottom { get; init; } = 3;
    public bool Wrap { get; init; } = true;
    public static TextBoxSpec Uniform(float value) => new() { MarginLeft = value, MarginRight = value, MarginTop = value, MarginBottom = value };
}

public static class TextBoxModel
{
    private static readonly TextBoxSpec TextDefault = TextBoxSpec.Uniform(3), ShapeDefault = TextBoxSpec.Uniform(12);
    public static TextBoxSpec Resolve(SlideShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape.TextBox ?? (shape.Kind == ShapeKind.Text ? TextDefault : ShapeDefault);
    }
    /// <summary>Empty content rectangles are valid when margins consume the shape. No negative extents are returned.</summary>
    public static RectF ContentBounds(SlideShape shape)
    {
        var box = Resolve(shape); Validate(box);
        return Inset(shape.Bounds, box);
    }
    public static RectF Inset(RectF bounds, TextBoxSpec box) => new(bounds.X + box.MarginLeft, bounds.Y + box.MarginTop,
        Math.Max(0, bounds.Width - box.MarginLeft - box.MarginRight), Math.Max(0, bounds.Height - box.MarginTop - box.MarginBottom));
    public static void Validate(TextBoxSpec box)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!ValidMargin(box.MarginLeft) || !ValidMargin(box.MarginRight) || !ValidMargin(box.MarginTop) || !ValidMargin(box.MarginBottom))
            throw new InvalidDataException("Text margins must be finite and between 0 and 10,000 slide units.");
    }
    internal static bool ValidMargin(float value) => float.IsFinite(value) && value >= 0 && value <= 10000;
    public static bool HasParagraphLayout(TextStyle style) => style.Alignment == ParagraphAlignment.Justify || style.LineSpacingPoints is not null ||
        style.SpaceBefore != 0 || style.SpaceAfter != 0 || style.ParagraphLeftMargin is not null || style.ParagraphRightMargin != 0 ||
        style.ParagraphIndent is not null || style.DefaultTabSize != 0;
    public static float LeftMargin(TextStyle style) => style.ParagraphLeftMargin ?? (style.Bullets ? style.FontSize * 1.25f : 0);
    public static float FirstIndent(TextStyle style) => style.ParagraphIndent ?? (style.Bullets ? -style.FontSize * 1.25f : 0);
}
