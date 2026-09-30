using System.Collections.Immutable;

namespace PresentationSpace.Core;

/// <summary>Original, editable tab alignment example. Every row remains a single text shape.</summary>
public static class TabLayoutSample
{
    public static PresentationDocument Create()
    {
        var shapes = ImmutableArray.CreateBuilder<SlideShape>();
        shapes.Add(SlideFactory.Text("Precision tab alignment", 64, 40, 1150, 80, 48) with { TextStyle = new() { FontSize = 48, Bold = true, Color = "#17324D" } });
        shapes.Add(SlideFactory.Text("Measured text, not manual spaces. Custom stops remain editable in PowerPoint.", 68, 130, 1130, 60, 24));
        var stops = ImmutableArray.Create(new TextTabStop(340, TextTabAlignment.Center), new TextTabStop(680, TextTabAlignment.Decimal), new TextTabStop(1080, TextTabAlignment.Right));
        shapes.Add(SlideFactory.Text("ITEM\tSTATUS\tUNIT PRICE\tTOTAL", 76, 220, 1120, 40, 18) with {
            TextStyle = new() { FontSize = 18, Bold = true, Color = "#526477", TabStops = stops }, TextBox = TextBoxSpec.Uniform(0) with { Wrap = false } });
        var rows = new[] { "Design review\tApproved\t19.50\t$1,950.00", "Prototype\tIn progress\t125.00\t$8,125.00", "Production\tScheduled\t7.25\t$725.00", "Delivery\tConfirmed\t42\t$2,100.00" };
        for (int i = 0; i < rows.Length; i++)
        {
            var shape = SlideFactory.Text(rows[i], 76, 294 + i * 58, 1120, 48, 25) with {
                TextStyle = new() { FontSize = 25, TabStops = stops, Color = "#17324D" }, TextBox = TextBoxSpec.Uniform(0) };
            if (i == 1) shape = RichText.Format(shape, 0, "Prototype".Length, s => s with { Bold = true, Color = "#CB4C2B" });
            shapes.Add(shape);
        }
        shapes.Add(SlideFactory.Text("CENTERED STATUS", 300, 555, 240, 40, 16));
        shapes.Add(SlideFactory.Text("DECIMAL PRICE", 630, 555, 270, 40, 16));
        shapes.Add(SlideFactory.Text("RIGHT-ALIGNED TOTAL", 960, 555, 280, 40, 16));
        shapes.Add(SlideFactory.Text("Select a row → Shape Format → Text Box Layout → Custom tab stops", 76, 640, 1120, 34, 20));
        return new() { Title = "Custom tabs", Slides = [new() { Name = "Custom tabs · aligned fields", Shapes = shapes.ToImmutable() }] };
    }
}
