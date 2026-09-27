using System.Collections.Immutable;
namespace PresentationSpace.Core;

public static class SlideFactory
{
    public static SlideShape Text(string text, float x, float y, float w, float h, float size = 28, string color = "#243247", bool bold = false) => new() { Kind = ShapeKind.Text, Name = text.Split('\n')[0], Text = text, Bounds = new(x,y,w,h), Fill = "#00000000", TextStyle = new() { FontSize = size, Color = color, Bold = bold } };
    public static Slide Create(string layout, float w = 1280, float h = 720)
    {
        var title = Text("Click to add title",w * .07f,h * .08f,w * .86f,h * .17f,44);
        var slide = new Slide { Name = layout, LayoutName = layout, Shapes = layout switch {
            "Blank" => [],
            "Title slide" => [Text("Click to add title",w*.09f,h*.32f,w*.82f,h*.2f,56),Text("Click to add subtitle",w*.09f,h*.55f,w*.82f,h*.15f,28)],
            "Title only" => [title],
            "Two content" => [title,Text("Click to add text",w*.07f,h*.3f,w*.4f,h*.58f),Text("Click to add text",w*.53f,h*.3f,w*.4f,h*.58f)],
            _ => [title,Text("Click to add text",w*.07f,h*.3f,w*.86f,h*.58f)] } };
        return slide with { Shapes = slide.Shapes.Select((shape, index) => shape with
        {
            Placeholder = index == 0 ? PlaceholderKind.Title : layout == "Title slide" ? PlaceholderKind.Subtitle : PlaceholderKind.Body,
            PlaceholderIndex = index
        }).ToImmutableArray() };
    }
    public static PresentationDocument Welcome()
    {
        var a = new Slide { Name = "A clearer way to present", Background = "#F9F7F4", Notes = "Welcome to PresentationSpace. Double-click text to edit it. Drag objects on the slide; use the ribbon to insert and format. Press F5 to present.", Shapes = [
            new() { Kind = ShapeKind.Rectangle, Name = "Accent", Bounds = new(74,91,64,6), Fill = "#D35230" },
            Text("PRESENTATIONSPACE",74,120,710,50,20,"#9B4837",true),
            Text("A clearer way\nto present.",70,200,770,235,80,"#233148",true),
            Text("Your ideas. Beautifully brought together.",76,467,770,60,28,"#627084"),
            Text("PRODUCT OVERVIEW  /  SEPTEMBER 2026",76,632,800,32,16,"#7C8492"),
            new() { Kind = ShapeKind.Ellipse, Name = "Terracotta circle", Bounds = new(925,96,240,240), Fill = "#D35230" },
            new() { Kind = ShapeKind.RoundRectangle, Name = "Peach tile", Bounds = new(873,365,298,218), Fill = "#EBC7B7", Rotation = -12 },
            new() { Kind = ShapeKind.Ellipse, Name = "Navy circle", Bounds = new(805,313,176,176), Fill = "#243247" }
        ] };
        var b = new Slide { Name = "From an idea to a great story", Shapes = [Text("From an idea to a great story",74,58,1120,92,46,"#243247",true),Text("A familiar workspace. A reusable engine. Your presentation.",76,163,1100,60,24,"#677487")] };
        var blocks = b.Shapes.ToBuilder();
        string[] headings = ["Create", "Refine", "Present"], bodies = ["Build slides with text, shapes, pictures, charts and tables.","Arrange every detail with alignment, typography and themes.","Share a PowerPoint file or deliver your story in slide show."];
        for (int i = 0; i < 3; i++) { float x = 76 + i * 389; blocks.Add(new() { Kind = ShapeKind.RoundRectangle, Bounds = new(x,283,353,326), Fill = i == 1 ? "#FFF2EB" : "#F4F5F7", Name = headings[i] + " card" }); blocks.Add(Text("0"+(i+1),x+24,310,300,50,25,"#D35230",true)); blocks.Add(Text(headings[i],x+24,377,300,64,36,"#243247",true)); blocks.Add(Text(bodies[i],x+24,455,300,126,23,"#627084")); }
        b = b with { Shapes = blocks.ToImmutable() };
        var c = new Slide { Name = "Make progress visible", Shapes = [Text("Make progress visible",74,62,1100,90,46,"#243247",true),Text("Shape your data into a story people remember.",76,166,1100,55,24,"#677487"),new() { Kind = ShapeKind.Chart, Name = "Quarterly progress", Bounds = new(100,276,725,355), Fill = "#D35230", Values = [42,68,54,89], Labels = ["Q1","Q2","Q3","Q4"] },Text("89%",897,334,320,127,92,"#D35230",true),Text("A strong finish.\nA stronger next chapter.",902,474,286,140,28,"#627084")] };
        var d = new Slide { Name = "What comes next", Background = "#243247", Shapes = [Text("What comes next?",84,84,1100,120,58,"#FFFFFF",true),Text("01    Start with a clear message.\n02    Make every slide matter.\n03    Give your audience a next step.",90,287,1060,261,35,"#EBC7B7"),Text("LET’S BUILD SOMETHING WORTH SHARING.",90,626,1100,40,18,"#FFFFFF")] };
        return new() { Title = "A clearer way to present", Slides = [a,b,c,d] };
    }
    public static PresentationDocument ApplyTheme(PresentationDocument d, string name)
    {
        string accent = name switch { "Ocean" => "#187EAB", "Forest" => "#287D61", "Violet" => "#7654B3", "Slate" => "#53657D", _ => "#D35230" };
        string old = d.Theme switch { "Ocean" => "#187EAB", "Forest" => "#287D61", "Violet" => "#7654B3", "Slate" => "#53657D", _ => "#D35230" };
        SlideShape Retheme(SlideShape shape)
        {
            var result = shape with { Fill = shape.Fill == old ? accent : shape.Fill, TextStyle = shape.TextStyle with { Color = shape.TextStyle.Color == old ? accent : shape.TextStyle.Color } };
            if (shape.Chart is { } chart)
                result = ChartModel.Apply(result, chart with { Series = chart.Series.Select(series => series.Color == old ? series with { Color = accent } : series).ToImmutableArray() });
            return TableModel.Reconcile(shape, result);
        }
        return d with { Theme = name, Slides = d.Slides.Select(s => s with { Shapes = s.Shapes.Select(Retheme).ToImmutableArray() }).ToImmutableArray() };
    }
}
