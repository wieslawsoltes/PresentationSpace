namespace PresentationSpace.Core;

/// <summary>Original, editable demonstration of margins, hanging indents, spacing, justification and no-wrap.</summary>
public static class ParagraphLayoutSample
{
    public static PresentationDocument Create()
    {
        var heading = SlideFactory.Text("Text that fits the story", 64, 50, 1150, 90, 52) with { Name = "Layout headline" };
        var body = SlideFactory.Text("Paragraph layout is part of the presentation, not just the editor. Adjustable insets reserve space around your words. Justified lines share the available width without stretching the final line.\nEach paragraph retains its own spacing and indentation while mixed character styles stay intact.",
            64, 175, 552, 355, 25) with { Name = "Justified paragraphs", Fill = "#F1F4F8", TextBox = new() { MarginLeft = 24, MarginRight = 24, MarginTop = 18, MarginBottom = 18 },
            TextStyle = new() { FontSize = 25, Alignment = ParagraphAlignment.Justify, SpaceAfter = 18 } };
        body = RichText.Format(body, 0, 16, s => s with { Bold = true, Color = "#B84925" });
        var bullets = SlideFactory.Text("Make room for important details and keep wrapped lines aligned with the text, not with the marker.\nSpace between ideas helps your audience follow the sequence.\nSoft breaks stay in the same item.\vThis line continues without another bullet.",
            665, 175, 551, 355, 23) with { Name = "Hanging bullets", TextStyle = new() { FontSize = 23, Bullets = true, SpaceAfter = 16, ParagraphLeftMargin = 36, ParagraphIndent = -24 },
            TextBox = new() { MarginLeft = 18, MarginRight = 18, MarginTop = 18, MarginBottom = 18 } };
        var strip = SlideFactory.Text("NO WRAP  ·  One deliberate line, clipped inside its own inset content area rather than over neighboring artwork.",
            64, 560, 1152, 85, 28) with { Name = "No-wrap example", Fill = "#F7EAE5", TextBox = TextBoxSpec.Uniform(18) with { Wrap = false } };
        return new() { Title = "Paragraph layout", Slides = [new() { Name = "Paragraph layout · margins and spacing", Shapes = [heading, body, bullets, strip] }] };
    }
}
