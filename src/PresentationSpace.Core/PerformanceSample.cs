using System.Collections.Immutable;
namespace PresentationSpace.Core;

public static class PerformanceSample
{
    public static PresentationDocument Create(int slideCount = 1000)
    {
        if (slideCount < 1 || slideCount > 2000) throw new ArgumentOutOfRangeException(nameof(slideCount));
        return new() { Title = $"Performance sample — {slideCount} slides", Slides = Enumerable.Range(0, slideCount).Select(i => new Slide
        {
            Name = $"Slide {i + 1}", LayoutName = "Blank", Shapes = [
                SlideFactory.Text($"Slide {i + 1}", 70, 60, 1000, 100, 52),
                SlideFactory.Text("A large deck with independent immutable slide content.", 70, 190, 1080, 100, 30),
                new SlideShape { Kind = ShapeKind.Rectangle, Bounds = new(70, 360, 300, 180), Fill = "#D35230" },
                new SlideShape { Kind = ShapeKind.Ellipse, Bounds = new(450, 360, 300, 180), Fill = "#2476A8" },
                SlideFactory.Text("Navigate with Home, End, Page Up and Page Down in the filmstrip or sorter.", 70, 600, 1100, 60, 22)
            ]
        }).ToImmutableArray() };
    }
}
