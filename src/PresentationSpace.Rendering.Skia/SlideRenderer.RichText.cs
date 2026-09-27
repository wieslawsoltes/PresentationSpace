using System.Globalization;
using System.Text.RegularExpressions;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public sealed partial class SlideRenderer
{
    private sealed record TextPiece(string Text, TextStyle Style, float Width);
    private sealed record TextLine(List<TextPiece> Pieces, TextStyle Style, float Width, float Ascent, float Height);

    private List<TextLine> BuildRichTextLines(SlideShape shape, float width)
    {
        var fonts = new Dictionary<TextStyle, SKFont>();
        SKFont Font(TextStyle style)
        {
            if (!fonts.TryGetValue(style, out var font)) fonts[style] = font = new SKFont(Face(style), style.FontSize) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
            return font;
        }
        var lines = new List<TextLine>();
        try
        {
            int offset = 0;
            foreach (string paragraph in shape.Text.Split('\n'))
            {
                var paragraphStyle = RichText.StyleAt(shape, offset);
                var pieces = new List<TextPiece>(); float used = 0;
                void Finish()
                {
                    var styles = pieces.Count == 0 ? new[] { paragraphStyle } : pieces.Select(p => p.Style).ToArray();
                    float ascent = styles.Max(s => -Font(s).Metrics.Ascent);
                    float descent = styles.Max(s => Font(s).Metrics.Descent);
                    float height = Math.Max(ascent + descent, styles.Max(s => s.FontSize * s.LineSpacing));
                    lines.Add(new(pieces, paragraphStyle, used, ascent, height)); pieces = []; used = 0;
                }
                void Add(TextPiece piece) { pieces.Add(piece); used += piece.Width; }
                if (paragraphStyle.Bullets) Add(new("• ", paragraphStyle, Font(paragraphStyle).MeasureText("• ")));
                foreach (Match token in Regex.Matches(paragraph, @"\s+|\S+"))
                {
                    var word = RichText.Segments(shape, offset + token.Index, token.Length).Select(range =>
                    {
                        string text = shape.Text.Substring(range.Start, range.Length).Replace("\r", "").Replace("\t", "    ");
                        return new TextPiece(text, range.Style, Font(range.Style).MeasureText(text));
                    }).ToArray();
                    float wordWidth = word.Sum(piece => piece.Width);
                    if (used > 0 && used + wordWidth > width)
                    {
                        Finish();
                        if (string.IsNullOrWhiteSpace(token.Value)) continue;
                    }
                    foreach (var piece in word)
                    {
                        if (piece.Width <= width) { if (used > 0 && used + piece.Width > width) Finish(); Add(piece); continue; }
                        var elements = StringInfo.GetTextElementEnumerator(piece.Text);
                        while (elements.MoveNext())
                        {
                            string text = elements.GetTextElement(); float measured = Font(piece.Style).MeasureText(text);
                            if (used > 0 && used + measured > width) Finish();
                            Add(new(text, piece.Style, measured));
                        }
                    }
                }
                Finish(); offset += paragraph.Length + 1;
            }
            return lines;
        }
        finally { foreach (var font in fonts.Values) font.Dispose(); }
    }

    /// <summary>Measures the same mixed-style line layout used by drawing; does not mutate the shape.</summary>
    public float MeasureRichTextHeight(SlideShape shape, float width, float padding = 0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!float.IsFinite(width) || width <= 0 || !float.IsFinite(padding) || padding < 0) throw new ArgumentOutOfRangeException(nameof(width));
        return BuildRichTextLines(shape, Math.Max(1, width - 2 * padding)).Sum(line => line.Height) + 2 * padding;
    }

    /// <summary>Draws mixed-style text using the same layout and font metrics as row auto-fit.</summary>
    public void DrawRichText(SKCanvas canvas, SlideShape shape, float padding = 3)
    {
        var bounds = shape.Bounds; float width = Math.Max(1, bounds.Width - padding * 2);
        var lines = BuildRichTextLines(shape, width);
        var fonts = new Dictionary<TextStyle, SKFont>();
        SKFont Font(TextStyle style)
        {
            if (!fonts.TryGetValue(style, out var font)) fonts[style] = font = new SKFont(Face(style), style.FontSize) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
            return font;
        }
        float total = lines.Sum(line => line.Height);
        float available = Math.Max(1, bounds.Height - padding * 2);
        float y = bounds.Y + padding + (shape.TextStyle.VerticalAlignment switch { Core.VerticalAlignment.Middle => Math.Max(0, (available - total) / 2), Core.VerticalAlignment.Bottom => Math.Max(0, available - total), _ => 0 });
        try
        {
            canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
                using var paint = new SKPaint { IsAntialias = true };
                foreach (var line in lines)
                {
                    float x = bounds.X + padding + (line.Style.Alignment switch { ParagraphAlignment.Center => (width - line.Width) / 2, ParagraphAlignment.Right => width - line.Width, _ => 0 });
                    float baseline = y + line.Ascent;
                    foreach (var piece in line.Pieces)
                    {
                        paint.Color = Color(piece.Style.Color, SKColors.Black);
                        canvas.DrawText(piece.Text, x, baseline, SKTextAlign.Left, Font(piece.Style), paint);
                        if (piece.Style.Underline) canvas.DrawRect(x, baseline + Math.Max(2, piece.Style.FontSize * .09f), piece.Width, Math.Max(1, piece.Style.FontSize * .045f), paint);
                        x += piece.Width;
                    }
                    y += line.Height; if (y >= bounds.Bottom) break;
                }
            }
            finally { canvas.Restore(); }
        }
        finally { foreach (var font in fonts.Values) font.Dispose(); }
    }
}
