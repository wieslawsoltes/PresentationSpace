using Microsoft.UI.Xaml;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void ChangeOutline(string name, Func<StrokeSpec, StrokeSpec> change)
    {
        FlushEdits();
        if (!Session.SelectedShapes.Any(s => StrokeModel.Supports(s) && !s.Locked)) { Notice("Select an unlocked shape, picture or line."); return; }
        Session.Apply(name, s => StrokeModel.Supports(s) ? StrokeModel.Apply(s, StrokeModel.Settings(s) with { Style = change(StrokeModel.Resolve(s)) }) : s);
    }
    private void OpenOutlineEditor(int field = 0)
    {
        FlushEdits();
        if (Session.Selection.Count != 1 || Session.PrimaryShape is not { Locked: false } shape || !StrokeModel.Supports(shape))
        { Notice("Select one unlocked shape, picture or line to edit its outline."); return; }
        var target = SelectionEditSnapshot.Capture(Session); ShowInspector(InspectorMode.Format);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!target.IsCurrent(Session)) return;
            if (field == 1) _format.FocusOutlineDashes(); else if (field == 2) _format.FocusOutlineGradient(); else _format.FocusOutlineWidth();
        });
    }
    private void FlipLine(bool horizontal, bool vertical)
    {
        FlushEdits(); Session.Apply("Line direction", s => !StrokeModel.IsLine(s) ? s : s with
        { LineDirection = new(horizontal ^ (s.LineDirection?.FlipHorizontal == true), vertical ^ (s.LineDirection?.FlipVertical == true)) });
    }
    private void OpenOutlineSample()
    {
        FlushEdits(); Session.EditDocument("Insert outline sample", d =>
        {
            var sample = DocumentLayout.Resize(StrokeSample.Create(), d.Width, d.Height);
            var result = d with { Slides = d.Slides.AddRange(sample.Slides) }; DocumentSerializer.Validate(result); return result;
        });
        Session.SelectSlide(Session.Document.Slides.Length - 1); Session.Select(Session.CurrentSlide.Shapes.First(StrokeModel.IsLine).Id);
        ShowNormal(); Viewport.Fit(); Ribbon.SelectTab("Shape Format");
    }
    private void BuildOutlineCommands()
    {
        _commands.Add(("Open outline sample", OpenOutlineSample)); _commands.Add(("Edit outline", () => OpenOutlineEditor()));
        _commands.Add(("Edit custom outline dashes", () => OpenOutlineEditor(1))); _commands.Add(("Edit outline gradient", () => OpenOutlineEditor(2)));
        _commands.Add(("Remove outline gradient", () => ChangeOutline("Solid outline", s => s with { Gradient = null })));
        _commands.Add(("Outline gradient Ocean", () => ChangeOutline("Outline gradient", s => s with { Gradient = GradientSample.Preset("Ocean") })));
        foreach (var dash in Enum.GetValues<StrokeDash>()) _commands.Add(("Outline dash " + dash, () => ChangeOutline("Outline dashes", s => s with { Dash = dash, CustomDashes = [] })));
        foreach (var cap in Enum.GetValues<StrokeCap>()) _commands.Add(("Outline cap " + cap, () => ChangeOutline("Outline caps", s => s with { Cap = cap })));
        foreach (var join in Enum.GetValues<StrokeJoin>()) _commands.Add(("Outline join " + join, () => ChangeOutline("Outline joins", s => s with { Join = join })));
        foreach (var end in Enum.GetValues<LineEndKind>())
        {
            _commands.Add(("Outline begin " + end, () => ChangeOutline("Begin arrow", s => s with { Begin = s.Begin with { Kind = end } })));
            _commands.Add(("Outline end " + end, () => ChangeOutline("End arrow", s => s with { End = s.End with { Kind = end } })));
        }
        _commands.Add(("Reverse line direction", () => FlipLine(true, true)));
        _commands.Add(("Line flip horizontal", () => FlipLine(true, false))); _commands.Add(("Line flip vertical", () => FlipLine(false, true)));
    }
    private RibbonCommandButton OutlineMenu() => Menu("outline-options", "Line\nStyle", "\uE8E4",
        new[] { ("Edit outline", (Action)(() => OpenOutlineEditor())), ("Custom dashes", (Action)(() => OpenOutlineEditor(1))),
            ("Gradient outline", (Action)(() => OpenOutlineEditor(2))), ("Reverse line direction", (Action)(() => FlipLine(true, true))) }
        .Concat(Enum.GetValues<StrokeDash>().Select(d => (d.ToString(), (Action)(() => ChangeOutline("Outline dashes", s => s with { Dash = d, CustomDashes = [] }))))));
}
