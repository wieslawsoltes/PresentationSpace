using System.Collections.Immutable;
using PresentationSpace.Core;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void BuildCustomTabCommands()
    {
        _commands.Add(("Edit custom tab stops", () => OpenTextBodyEditor(true)));
        _commands.Add(("Clear custom tab stops", () => Viewport.FormatParagraph("Clear custom tab stops", style => style with { TabStops = [] })));
        foreach (var alignment in Enum.GetValues<TextTabAlignment>())
        {
            var stops = ImmutableArray.Create(new TextTabStop(120, alignment), new TextTabStop(240, alignment), new TextTabStop(420, alignment));
            _commands.Add(("Tab stops " + alignment, () => Viewport.FormatParagraph("Custom tab stops", style => style with { TabStops = stops })));
        }
        _commands.Add(("Open custom tabs sample", () =>
        {
            FlushEdits(); Session.EditDocument("Insert custom tabs sample", d =>
            {
                var sample = DocumentLayout.Resize(TabLayoutSample.Create(), d.Width, d.Height);
                var result = d with { Slides = d.Slides.AddRange(sample.Slides) };
                DocumentSerializer.Validate(result); return result;
            });
            Session.SelectSlide(Session.Document.Slides.Length - 1); ShowNormal(); Viewport.Fit();
        }));
    }
}
