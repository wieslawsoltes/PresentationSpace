using System.Collections.Immutable;

namespace PresentationSpace.Core;

public static class GradientSample
{
    public static GradientFill Preset(string name) => name switch
    {
        "Ocean" => new() { Angle = 45, Stops = [new(0, "#092E4C"), new(.55f, "#157F9C"), new(1, "#78C6B4")] },
        "Sunset" => new() { Angle = 30, Stops = [new(0, "#7E2862"), new(.5f, "#D35230"), new(1, "#F9C773")] },
        "Violet" => new() { Angle = 135, Stops = [new(0, "#352467"), new(.5f, "#7956AF"), new(1, "#D9BEED")] },
        "Fade" => new() { Stops = [new(0, "#D35230"), new(1, "#D35230", 0)] },
        _ => throw new ArgumentException("Unknown gradient preset.", nameof(name))
    };
    public static PresentationDocument Create()
    {
        var shapes = ImmutableArray.CreateBuilder<SlideShape>();
        shapes.Add(SlideFactory.Text("PRESENTATIONSPACE  /  NATIVE GRADIENTS", 66, 42, 1100, 32, 15, "#B1DADD", true));
        shapes.Add(SlideFactory.Text("Color with depth.", 62, 88, 1130, 96, 64, "#FFFFFF", true));
        shapes.Add(SlideFactory.Text("Editable stops. Precise geometry. One shared rendering engine.", 66, 193, 1120, 50, 24, "#CEE5E8"));
        string[] names = ["Ocean", "Sunset", "Violet"];
        for (int i = 0; i < names.Length; i++)
        {
            float x = 70 + i * 395;
            shapes.Add(new() { Name = names[i] + " gradient card", Kind = i == 2 ? ShapeKind.Ellipse : ShapeKind.RoundRectangle,
                Bounds = new(x, 290, 350, 185), FillGradient = Preset(names[i]), Stroke = "#79FFFFFF", StrokeWidth = 1.5f });
            shapes.Add(SlideFactory.Text(names[i].ToUpperInvariant(), x + 20, 341, 310, 58, 29, "#FFFFFF", true) with { TextStyle = new() { FontSize = 29, Bold = true, Color = "#FFFFFF", Alignment = ParagraphAlignment.Center } });
            shapes.Add(SlideFactory.Text(new[] { "Three stops · scaled angle", "Warm spectrum · 30°", "Elliptical fill · 135°" }[i], x, 490, 355, 38, 17, "#CCE3E7"));
        }
        var table = TableModel.Create(1, 3) with { HeaderRow = false, BandedRows = false };
        table = table with { Cells = table.Cells.Select((c, i) => c with { Text = new[] { "SOLID → TRANSPARENT", "EDITABLE TABLE CELLS", "NATIVE DRAWINGML" }[i],
            FillGradient = i == 0 ? Preset("Fade") : Preset("Ocean") with { Angle = 90 },
            TextStyle = new() { FontSize = 15, Bold = true, Color = "#FFFFFF", VerticalAlignment = VerticalAlignment.Middle, Alignment = ParagraphAlignment.Center },
            Left = new() { Width = 0 }, Right = new() { Width = 0 }, Top = new() { Width = 0 }, Bottom = new() { Width = 0 } }).ToImmutableArray() };
        shapes.Add(TableModel.Apply(new() { Name = "Gradient cells", Bounds = new(70, 565, 1140, 65) }, table));
        shapes.Add(SlideFactory.Text("Shape, slide and cell fills remain editable in exported PPTX files.", 70, 650, 1120, 28, 15, "#A5C7D2"));
        return new() { Title = "Color with depth", Slides = [new() { Name = "Gradient fills · color with depth", Background = "#072338", BackgroundGradient = new() {
            Angle = 90, Stops = [new(0, "#071D32"), new(1, "#174B59")] }, Shapes = shapes.ToImmutable() }] };
    }
}
