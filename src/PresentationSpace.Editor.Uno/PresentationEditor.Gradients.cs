using Microsoft.UI.Xaml;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void OpenGradientEditor(bool background = false, bool angle = false)
    {
        FlushEdits();
        if (background) Session.Select(null);
        if (Session.PrimaryShape is { } shape && (Session.Selection.Count != 1 || shape.Locked || !GradientModel.Supports(shape)))
        { Notice("Select one unlocked filled shape or picture, or clear the selection for a slide background."); return; }
        var slide = Session.CurrentSlide; var source = Session.PrimaryShape;
        ShowInspector(InspectorMode.Format);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(Session.CurrentSlide, slide) || !ReferenceEquals(Session.PrimaryShape, source)) return;
            if (angle) _format.FocusGradientAngle(); else _format.FocusGradientStops();
        });
    }
    private void SetGradient(GradientFill? gradient, bool background = false)
    {
        FlushEdits();
        if (background) Session.EditSlide("Background gradient", s => s with { BackgroundGradient = gradient });
        else if (Session.SelectedShapes.Any(s => !s.Locked && GradientModel.Supports(s)))
            Session.Apply("Shape gradient", s => GradientModel.Supports(s) ? GradientModel.Apply(s, gradient) : s);
        else Notice("Select an unlocked filled shape or picture.");
    }
    private void OpenGradientSample()
    {
        FlushEdits(); Session.EditDocument("Insert gradient sample", d =>
        {
            var sample = DocumentLayout.Resize(GradientSample.Create(), d.Width, d.Height);
            var result = d with { Slides = d.Slides.AddRange(sample.Slides) }; DocumentSerializer.Validate(result); return result;
        });
        Session.SelectSlide(Session.Document.Slides.Length - 1);
        Session.Select(Session.CurrentSlide.Shapes.First(s => s.FillGradient is not null).Id);
        ShowNormal(); Viewport.Fit(); Ribbon.SelectTab("Shape Format");
    }
    private void BuildGradientCommands()
    {
        _commands.Add(("Open gradient fill sample", OpenGradientSample));
        _commands.Add(("Edit gradient fill", () => OpenGradientEditor()));
        _commands.Add(("Edit gradient angle", () => OpenGradientEditor(angle: true)));
        _commands.Add(("Edit background gradient", () => OpenGradientEditor(background: true)));
        _commands.Add(("Remove gradient fill", () => SetGradient(null)));
        _commands.Add(("Remove background gradient", () => SetGradient(null, true)));
        foreach (var name in new[] { "Ocean", "Sunset", "Violet", "Fade" })
        {
            _commands.Add(("Gradient fill " + name, () => SetGradient(GradientSample.Preset(name))));
            _commands.Add(("Background gradient " + name, () => SetGradient(GradientSample.Preset(name), true)));
        }
        _commands.Add(("Reverse gradient fill", () => { FlushEdits(); Session.Apply("Reverse gradient", s => s.FillGradient is { } g ? s with { FillGradient = GradientModel.Reverse(g) } : s); }));
        _commands.Add(("Edit table gradient", () => DesignTable(t => t.FocusGradient(), true)));
        _commands.Add(("Table gradient Ocean", () => DesignTable(t => t.ApplyGradient(GradientSample.Preset("Ocean")))));
    }
    private RibbonCommandButton GradientMenu(bool background = false) => Menu(background ? "background-gradient" : "shape-gradient", "Gradient\nFill", "\uE790",
        new[] { ("Edit gradient fill", (Action)(() => OpenGradientEditor(background))) }
        .Concat(new[] { "Ocean", "Sunset", "Violet", "Fade" }.Select(n => (n, (Action)(() => SetGradient(GradientSample.Preset(n), background)))))
        .Append(("Remove gradient", () => SetGradient(null, background))));
}
