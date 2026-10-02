using System.Collections.Immutable;

namespace PresentationSpace.Core;

public static class StrokeSample
{
    public static PresentationDocument Create()
    {
        var shapes = ImmutableArray.CreateBuilder<SlideShape>();
        void Text(string text, float x, float y, float w, float h, float size, string color = "#243247") => shapes.Add(SlideFactory.Text(text, x, y, w, h, size) with { TextStyle = new() { FontSize = size, Color = color } });
        Text("LINES WITH INTENT", 56, 40, 900, 60, 42);
        Text("Editable outlines · native DrawingML · one shared renderer", 58, 112, 1160, 42, 21, "#566780");
        shapes.Add(new() { Kind = ShapeKind.RoundRectangle, Bounds = new(40, 175, 570, 466), Fill = "#F2F5FA" });
        shapes.Add(new() { Kind = ShapeKind.RoundRectangle, Bounds = new(640, 175, 600, 466), Fill = "#F2F5FA" });
        var types = new[] { LineEndKind.Triangle, LineEndKind.Stealth, LineEndKind.Diamond, LineEndKind.Oval, LineEndKind.OpenArrow };
        for (int i = 0; i < types.Length; i++)
        {
            float y = 245 + i * 74;
            Text(types[i].ToString(), 68, y - 35, 240, 34, 18);
            shapes.Add(StrokeModel.Line(new(82, y + 10), new(555, y + 10), true) with { Name = types[i] + " arrow", Stroke = "#246D94", StrokeWidth = 5,
                Outline = new() { Cap = StrokeCap.Flat, Dash = i % 2 == 0 ? StrokeDash.Dash : StrokeDash.Solid,
                    Begin = new() { Kind = LineEndKind.Oval, Width = LineEndSize.Small }, End = new() { Kind = types[i], Width = LineEndSize.Large, Length = LineEndSize.Large } } });
        }
        Text("GRADIENT + CUSTOM DASH", 674, 204, 540, 34, 18);
        shapes.Add(new() { Kind = ShapeKind.RoundRectangle, Name = "Gradient framed card", Bounds = new(685, 257, 223, 185), Fill = "#FFFFFF", StrokeWidth = 7,
            Outline = new() { Gradient = GradientSample.Preset("Violet"), Dash = StrokeDash.DashDot, Join = StrokeJoin.Round } });
        shapes.Add(new() { Kind = ShapeKind.Ellipse, Name = "Custom dashed ellipse", Bounds = new(965, 257, 223, 185), Fill = "#FFFFFF", StrokeWidth = 6,
            Outline = new() { Gradient = GradientSample.Preset("Ocean"), Cap = StrokeCap.Flat, CustomDashes = [new(6, 2), new(1, 2)] } });
        Text("ZERO-EXTENT & REVERSED LINES", 674, 475, 548, 34, 18);
        shapes.Add(StrokeModel.Line(new(715, 596), new(715, 530), true) with { Stroke = "#9C5C8E", StrokeWidth = 5 });
        shapes.Add(StrokeModel.Line(new(1185, 560), new(805, 560), true) with { StrokeWidth = 6,
            Outline = new() { Gradient = GradientSample.Preset("Sunset"), Begin = new() { Kind = LineEndKind.Diamond }, End = new() { Kind = LineEndKind.Triangle, Width = LineEndSize.Large } } });
        Text("Not flattened. Every line and frame remains editable.", 58, 662, 1150, 34, 18, "#566780");
        return new() { Title = "Outline layout sample", Slides = [new() { Name = "Outlines · dashes and arrows", Shapes = shapes.ToImmutable() }] };
    }
}
