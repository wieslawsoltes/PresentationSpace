using System.Collections.Immutable;

namespace PresentationSpace.Core;

/// <summary>Content-preserving presentation sizing. Geometry scales on each axis; type scales uniformly.</summary>
public static class DocumentLayout
{
    public static TextStyle ScaleStyle(TextStyle style, float factor)
    {
        ArgumentNullException.ThrowIfNull(style);
        float size = style.FontSize * factor;
        if (!float.IsFinite(factor) || factor <= 0 || !float.IsFinite(size) || size < 1 || size > 2048)
            throw new ArgumentOutOfRangeException(nameof(factor), "Scaled text must remain between 1 and 2048 slide units.");
        var result = factor == 1 ? style : style with { FontSize = size,
            LineSpacingPoints = style.LineSpacingPoints * factor, SpaceBefore = style.SpaceBefore * factor, SpaceAfter = style.SpaceAfter * factor,
            ParagraphLeftMargin = style.ParagraphLeftMargin * factor, ParagraphRightMargin = style.ParagraphRightMargin * factor,
            ParagraphIndent = style.ParagraphIndent * factor, DefaultTabSize = style.DefaultTabSize * factor };
        TextFlow.ValidateStyle(result); return result;
    }

    /// <summary>Preserves relative sizes in mixed runs instead of replacing every run with the base font size.</summary>
    public static SlideShape ScaleText(SlideShape shape, float factor)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var style = ScaleStyle(shape.TextStyle, factor);
        return factor == 1 ? shape : shape with { TextStyle = style, TextRanges = shape.TextRanges.Select(r => r with { Style = ScaleStyle(r.Style, factor) }).ToImmutableArray() };
    }

    public static PresentationDocument Resize(PresentationDocument document, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentSerializer.Validate(document);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width < 1 || height < 1 || width > 16384 || height > 16384)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (width == document.Width && height == document.Height) return document;
        float sx = width / document.Width, sy = height / document.Height, scale = Math.Min(sx, sy);
        TableBorder Border(TableBorder border) => border with { Width = border.Width * scale };
        SlideShape ResizeShape(SlideShape shape)
        {
            var next = ScaleText(shape, scale) with { Bounds = new(shape.Bounds.X * sx, shape.Bounds.Y * sy, shape.Bounds.Width * sx, shape.Bounds.Height * sy), StrokeWidth = shape.StrokeWidth * scale };
            if (shape.TextBox is { } box) next = next with { TextBox = box with {
                MarginLeft = box.MarginLeft * sx, MarginRight = box.MarginRight * sx,
                MarginTop = box.MarginTop * sy, MarginBottom = box.MarginBottom * sy } };
            if (shape.Table is not { } table) return next;
            var resized = table with { TextStyle = ScaleStyle(table.TextStyle, scale), Cells = table.Cells.Select(cell => cell with
            {
                TextStyle = cell.TextStyle is null ? null : ScaleStyle(cell.TextStyle, scale),
                TextRanges = cell.TextRanges.Select(r => r with { Style = ScaleStyle(r.Style, scale) }).ToImmutableArray(),
                MarginLeft = cell.MarginLeft * sx, MarginRight = cell.MarginRight * sx,
                MarginTop = cell.MarginTop * sy, MarginBottom = cell.MarginBottom * sy,
                Left = Border(cell.Left), Right = Border(cell.Right), Top = Border(cell.Top), Bottom = Border(cell.Bottom)
            }).ToImmutableArray() };
            return TableModel.Apply(next, resized);
        }
        var result = document with { Width = width, Height = height,
            Slides = document.Slides.Select(slide => slide with { Shapes = slide.Shapes.Select(ResizeShape).ToImmutableArray() }).ToImmutableArray() };
        // Fail atomically rather than producing a document the serializer cannot reopen.
        DocumentSerializer.Validate(result);
        return result;
    }
}
