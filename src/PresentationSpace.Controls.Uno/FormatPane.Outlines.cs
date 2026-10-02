using PresentationSpace.Core;

namespace PresentationSpace.Controls.Uno;

public sealed partial class FormatPane
{
    private OutlineEditor? _outlineEditor;
    private GradientFillEditor? _outlineGradientEditor;
    private bool _committingOutline;
    public void FocusOutlineWidth() => _outlineEditor?.FocusWidth();
    public void FocusOutlineDashes() => _outlineEditor?.FocusDashes();
    public void FocusOutlineGradient() => _outlineGradientEditor?.FocusStops();
    private void BuildOutlineEditor(EditorSession session, SlideShape shape)
    {
        if (!StrokeModel.Supports(shape) || session.Selection.Count != 1) return;
        Section("Outline");
        var editor = new OutlineEditor { IsEnabled = !shape.Locked }; _outlineEditor = editor;
        var gradientEditor = new GradientFillEditor("outline-gradient") { IsEnabled = !shape.Locked }; _outlineGradientEditor = gradientEditor;
        editor.SetValue(StrokeModel.Settings(shape), StrokeModel.IsLine(shape)); gradientEditor.SetValue(StrokeModel.Resolve(shape).Gradient);
        var target = SelectionEditSnapshot.Capture(session);
        void Commit(Func<SlideShape, SlideShape> change)
        {
            if (!ReferenceEquals(_outlineEditor, editor) || !target.IsCurrent(session))
                throw new InvalidOperationException("The outline target changed. Reopen its editor before applying this draft.");
            _committingOutline = true;
            try
            {
                target.TryApply(session, "Outline", change); _lastShape = session.PrimaryShape; target = SelectionEditSnapshot.Capture(session);
            }
            finally { _committingOutline = false; }
        }
        editor.ValueChanged += (_, settings) => { Commit(s => StrokeModel.Apply(s, settings)); gradientEditor.SetValue(settings.Style.Gradient); };
        gradientEditor.ValueChanged += (_, gradient) =>
        {
            Commit(s => StrokeModel.Apply(s, StrokeModel.Settings(s) with { Style = StrokeModel.Resolve(s) with { Gradient = gradient } }));
            if (session.PrimaryShape is { } current) editor.SetValue(StrokeModel.Settings(current), StrokeModel.IsLine(current));
        };
        _body.Children.Add(editor); Section("Outline gradient");
        Hint("Apply outline fields and gradient fields separately. Applying a gradient refreshes the other outline fields; unapplied drafts are not recovery state.");
        _body.Children.Add(gradientEditor);
    }
}
