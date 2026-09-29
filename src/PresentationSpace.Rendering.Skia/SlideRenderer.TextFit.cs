using PresentationSpace.Core;

namespace PresentationSpace.Rendering.Skia;

public sealed record TextFitResult(SlideShape Shape, float Scale, bool Fits);

public sealed partial class SlideRenderer
{
    /// <summary>Explicit, non-destructive shrink-to-fit; preserves all mixed-run size ratios. Not a persistent auto-fit mode.</summary>
    public TextFitResult FitTextToShape(SlideShape shape, float minimumFontSize = 8)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!float.IsFinite(minimumFontSize) || minimumFontSize < 1 || minimumFontSize > 2048) throw new ArgumentOutOfRangeException(nameof(minimumFontSize));
        ValidateTextShape(shape);
        var content = TextBoxModel.ContentBounds(shape);
        float width = content.Width, height = content.Height;
        if (width <= 0 || height <= 0) return new(shape, 1, false);
        bool Fits(SlideShape candidate)
        {
            var layout = TextLayout.Measure(candidate.Text, candidate.TextStyle, width, candidate.TextRanges, TextBoxModel.Resolve(candidate).Wrap);
            return layout.Width <= width + .001f && layout.Height <= height + .001f;
        }
        if (Fits(shape)) return new(shape, 1, true);
        float smallest = shape.TextRanges.Aggregate(shape.TextStyle.FontSize, (value, range) => Math.Min(value, range.Style.FontSize));
        float low = Math.Min(1, minimumFontSize / smallest), high = 1;
        var best = DocumentLayout.ScaleText(shape, low);
        if (!Fits(best)) return new(shape, 1, false);
        for (int i = 0; i < 14; i++)
        {
            float scale = (low + high) / 2;
            var candidate = DocumentLayout.ScaleText(shape, scale);
            if (Fits(candidate)) { low = scale; best = candidate; } else high = scale;
        }
        return new(best, low, true);
    }

    /// <summary>Changes height only; pins the original rotated top edge in slide coordinates.</summary>
    public SlideShape FitShapeToText(SlideShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape); ValidateTextShape(shape);
        float padding = shape.Kind == ShapeKind.Text ? 3 : 12;
        if (TextBoxModel.ContentBounds(shape).Width <= 0) throw new InvalidOperationException("Text margins leave no horizontal content area.");
        float height = Math.Max(1, MeasureRichTextHeight(shape, shape.Bounds.Width, padding));
        if (!float.IsFinite(height) || height > 100000) throw new InvalidOperationException("Fitted text exceeds the maximum shape height.");
        float delta = (height - shape.Bounds.Height) / 2, angle = shape.Rotation * MathF.PI / 180;
        var bounds = shape.Bounds with { X = shape.Bounds.X - MathF.Sin(angle) * delta, Y = shape.Bounds.Y + (MathF.Cos(angle) - 1) * delta, Height = height };
        if (Math.Abs(bounds.X) > 100000 || Math.Abs(bounds.Y) > 100000) throw new InvalidOperationException("Fitted shape lies outside the document geometry limits.");
        return shape with { Bounds = bounds };
    }
    private static void ValidateTextShape(SlideShape shape)
    {
        if (shape.Kind is ShapeKind.Image or ShapeKind.Chart or ShapeKind.Table) throw new InvalidOperationException("Select a text box or a text-bearing shape. Tables have a separate row auto-fit command.");
        if (!float.IsFinite(shape.Bounds.Width) || !float.IsFinite(shape.Bounds.Height) || !float.IsFinite(shape.Bounds.X) || !float.IsFinite(shape.Bounds.Y) ||
            !float.IsFinite(shape.Rotation) || shape.Bounds.Width <= 0 || shape.Bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(shape));
    }
}
