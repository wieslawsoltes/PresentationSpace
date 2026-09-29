namespace PresentationSpace.Core;

/// <summary>Original, editable two-slide typography sample; no external assets or bundled font files.</summary>
public static class TypographySample
{
    public static PresentationDocument Create()
    {
        SlideShape Text(string text, float x, float y, float width, float height, float size, string color = "#25334A", bool bold = false) =>
            SlideFactory.Text(text, x, y, width, height, size) with { TextStyle = new() { FontFamily = "Arial", FontSize = size, Color = color, Bold = bold } };
        SlideShape Panel(float x, float y, float w, float h) => new() { Kind = ShapeKind.RoundRectangle, Bounds = new(x, y, w, h), Fill = "#F2F5F9", Stroke = "#DDE4EC", StrokeWidth = 1 };
        var mixed = Text("One baseline. Many voices.", 72, 222, 1090, 85, 34);
        mixed = RichText.Format(mixed, 14, 4, style => style with { FontSize = 54, Bold = true, Color = "#CB4C2B" });
        var bullets = Text("A wrapped bullet keeps the continuation aligned with its text instead of the marker.\vA soft line break stays inside the same item.\nA new paragraph starts a new bullet.", 104, 348, 485, 230, 23) with { TextStyle = new() { FontSize = 23, Bullets = true } };
        var first = new Slide
        {
            Name = "Typography · shared baselines", Background = "#FFFFFF", LayoutName = "Blank",
            Notes = "All visible text is editable. The same HarfBuzz glyph advances and line layout feed the canvas, thumbnails, slide show, PNG and PDF. Fonts must be available in the host. Mixed bidirectional paragraphs and full Office typography are not claimed.",
            Shapes = [
                Text("PRESENTATIONSPACE  /  TYPOGRAPHY", 72, 40, 1080, 36, 18, "#CB4C2B", true),
                Text("Typography that holds together", 72, 100, 1140, 98, 50, bold: true), mixed,
                Panel(72, 324, 545, 300), Panel(641, 324, 567, 300), bullets,
                Text("SPACE, TABS & SHAPING", 672, 348, 490, 42, 18, "#66758A", true),
                Text("AVATAR  ·  office  ·  efficient\nCafe\u0301     naïve     résumé\nName\tRole\tTeam\nA non-breaking pair: 10\u00A0kg", 672, 406, 490, 190, 25),
                Text("01  /  Shared line metrics, preserved whitespace, grapheme-safe wrapping", 72, 661, 1120, 30, 16, "#66758A")
            ]
        };
        var table = TableModel.Create(4, 3);
        string[] values = ["Content", "Measurement", "Presentation", "Mixed styles", "Shared baseline", "Canvas & thumbnails", "Wrapped text", "Measured row fit", "Slide show", "Shaped glyphs", "Retained layouts", "PNG & vector PDF"];
        for (int row = 0; row < 4; row++) for (int col = 0; col < 3; col++) table = TableModel.SetText(table, row, col, values[row * 3 + col]);
        table = TableModel.ApplyStyle(table, TableStylePreset.Blue) with { TextStyle = new() { FontSize = 24 }, HeaderRow = true };
        var second = new Slide
        {
            Name = "Typography · text and tables", Background = "#FFFFFF", LayoutName = "Blank",
            Notes = "Select the lower text box, then use Shrink text to fit or Resize shape to text. Both are explicit undoable commands, not a persistent Office auto-fit mode. Table row auto-fit uses the same layout measurements.",
            Shapes = [
                Text("PRESENTATIONSPACE  /  CONSISTENT LAYOUT", 72, 40, 1080, 36, 18, "#285EA8", true),
                Text("Text & tables. One layout engine.", 72, 102, 1140, 90, 48, bold: true),
                TableModel.Apply(new SlideShape { Name = "Shared text metrics table", Bounds = new(72, 226, 1136, 280) }, table),
                Text("Select this paragraph and try the text-fit commands. Mixed styles keep their relative font sizes when the presentation is resized.", 72, 546, 1136, 85, 28),
                Text("02  /  Explicit fitting, editable native tables, reusable core libraries", 72, 661, 1120, 30, 16, "#66758A")
            ]
        };
        return new() { Title = "Typography and layout", Slides = [first, second] };
    }
}
