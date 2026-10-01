using PresentationSpace.Core;

namespace PresentationSpace.Controls.Uno;

public sealed partial class FormatPane
{
    private GradientFillEditor? _gradientEditor;
    private bool _committingGradient;
    private Slide? _lastSlide;
    public void FocusGradientStops() => _gradientEditor?.FocusStops();
    public void FocusGradientAngle() => _gradientEditor?.FocusAngle();
    private void BuildGradientEditor(EditorSession session, SlideShape? shape)
    {
        if (shape is not null && (!GradientModel.Supports(shape) || session.Selection.Count != 1)) return;
        Section("Gradient fill");
        var editor = new GradientFillEditor { IsEnabled = shape?.Locked != true }; _gradientEditor = editor;
        editor.SetValue(shape is null ? session.CurrentSlide.BackgroundGradient : shape.FillGradient);
        var target = SelectionEditSnapshot.Capture(session); var sourceSlide = session.CurrentSlide;
        editor.ValueChanged += (_, gradient) =>
        {
            bool current = shape is null ? session.Selection.Count == 0 && ReferenceEquals(sourceSlide, session.CurrentSlide) : target.IsCurrent(session);
            if (!ReferenceEquals(_gradientEditor, editor) || !current)
                throw new InvalidOperationException("The fill target changed. Reopen its gradient editor before applying this draft.");
            _committingGradient = true;
            try
            {
                if (shape is null) session.EditSlide("Background gradient", s => s with { BackgroundGradient = gradient });
                else target.TryApply(session, "Shape gradient", s => GradientModel.Apply(s, gradient));
                _lastShape = session.PrimaryShape; _lastSlide = sourceSlide = session.CurrentSlide;
                target = SelectionEditSnapshot.Capture(session);
            }
            finally { _committingGradient = false; }
        };
        _body.Children.Add(editor);
    }
}
