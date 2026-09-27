using System.Collections.Immutable;

namespace PresentationSpace.Core;

/// <summary>Maps placeholder content without removing unmatched content or custom artwork.</summary>
public static class SlideLayoutEngine
{
    public static readonly ImmutableArray<string> Layouts = ["Blank", "Title slide", "Title and content", "Two content", "Title only"];

    public static Slide Apply(Slide slide, string layout, float width, float height)
    {
        if (!Layouts.Contains(layout)) throw new ArgumentException("Unknown slide layout.", nameof(layout));
        var source = RecognizeLegacyPlaceholders(slide, width, height);
        var targets = SlideFactory.Create(layout, width, height).Shapes;
        var available = source.Where(s => s.Placeholder != PlaceholderKind.None && !s.Locked).ToList();
        var replacements = new Dictionary<Guid, SlideShape>();
        var additions = new List<SlideShape>();
        foreach (var target in targets)
        {
            var match = available.FirstOrDefault(s => s.Placeholder == target.Placeholder && s.PlaceholderIndex == target.PlaceholderIndex)
                ?? available.FirstOrDefault(s => s.Placeholder == target.Placeholder)
                ?? available.FirstOrDefault(s => IsContent(s.Placeholder) && IsContent(target.Placeholder));
            if (match is null) { additions.Add(target); continue; }
            available.Remove(match);
            replacements[match.Id] = match with
            {
                Bounds = target.Bounds,
                Placeholder = target.Placeholder,
                PlaceholderIndex = target.PlaceholderIndex
            };
        }
        // Preserve identity, z-order, text, styles, notes, comments, animations and every unmatched shape.
        // An unmatched placeholder retains its role so returning from Blank can remap it later.
        return slide with
        {
            LayoutName = layout,
            Shapes = source.Select(s => replacements.GetValueOrDefault(s.Id, s)).Concat(additions).ToImmutableArray()
        };
    }

    private static bool IsContent(PlaceholderKind kind) => kind is PlaceholderKind.Body or PlaceholderKind.Subtitle or PlaceholderKind.Object;

    private static ImmutableArray<SlideShape> RecognizeLegacyPlaceholders(Slide slide, float width, float height)
    {
        if (slide.Shapes.Any(s => s.Placeholder != PlaceholderKind.None)) return slide.Shapes;
        string? name = slide.LayoutName ?? (Layouts.Contains(slide.Name) ? slide.Name : null);
        if (name is null) return slide.Shapes; // Never guess from text or swallow a user's text box.
        var template = SlideFactory.Create(name, width, height);
        return slide.Shapes.Select(shape =>
        {
            var match = template.Shapes.FirstOrDefault(t => t.Kind == shape.Kind && t.Bounds == shape.Bounds);
            return match is null ? shape : shape with { Placeholder = match.Placeholder, PlaceholderIndex = match.PlaceholderIndex };
        }).ToImmutableArray();
    }

    public static void ApplyLayout(this EditorSession session, string layout) => session.EditSlide("Slide layout", slide => Apply(slide, layout, session.Document.Width, session.Document.Height));
}
