using Microsoft.UI.Xaml;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void OpenTextBodyEditor(bool tabs = false)
    {
        FlushEdits();
        if (Session.Selection.Count != 1 || Session.PrimaryShape is null || Session.PrimaryShape.Kind is ShapeKind.Table or ShapeKind.Chart or ShapeKind.Image)
        { Notice("Select one text box or text-bearing shape. Tables have separate cell margins."); return; }
        ShowInspector(InspectorMode.Format);
        DispatcherQueue.TryEnqueue(() => { if (tabs) _format.FocusTabStops(); else _format.FocusTextLayout(); });
    }
    private void SetTextBox(string label, Func<TextBoxSpec, TextBoxSpec> change)
    {
        FlushEdits();
        Session.Apply(label, shape =>
        {
            if (shape.Kind is ShapeKind.Table or ShapeKind.Chart or ShapeKind.Image) return shape;
            var box = change(TextBoxModel.Resolve(shape)); TextBoxModel.Validate(box); return shape with { TextBox = box };
        });
        Viewport.Focus(FocusState.Programmatic);
    }
    private void BuildTextBodyCommands()
    {
        _commands.Add(("Edit text layout", () => OpenTextBodyEditor()));
        BuildCustomTabCommands();
        _commands.Add(("Text wrap on", () => SetTextBox("Wrap text", box => box with { Wrap = true })));
        _commands.Add(("Text wrap off", () => SetTextBox("No text wrapping", box => box with { Wrap = false })));
        foreach (float value in new[] { 0f, 3f, 12f, 24f })
        {
            float margin = value;
            _commands.Add(("Text margins " + margin, () => SetTextBox("Text margins", box => box with { MarginLeft = margin, MarginRight = margin, MarginTop = margin, MarginBottom = margin })));
        }
        foreach (var align in Enum.GetValues<ParagraphAlignment>())
            _commands.Add(("Paragraph align " + align, () => Viewport.FormatParagraph("Paragraph alignment", style => style with { Alignment = align })));
        foreach (float value in new[] { 0f, 6f, 12f, 24f })
        {
            float space = value;
            _commands.Add(("Paragraph space before " + space, () => Viewport.FormatParagraph("Paragraph spacing", style => style with { SpaceBefore = space })));
            _commands.Add(("Paragraph space after " + space, () => Viewport.FormatParagraph("Paragraph spacing", style => style with { SpaceAfter = space })));
        }
        _commands.Add(("Paragraph hanging indent", () => Viewport.FormatParagraph("Hanging indent", style => style with { ParagraphLeftMargin = 36, ParagraphIndent = -18 })));
        _commands.Add(("Paragraph reset layout", () => Viewport.FormatParagraph("Reset paragraph layout", style => style with { SpaceBefore = 0, SpaceAfter = 0,
            ParagraphLeftMargin = null, ParagraphRightMargin = 0, ParagraphIndent = null, DefaultTabSize = 0, TabStops = [], LineSpacingPoints = null, Alignment = ParagraphAlignment.Left })));
        _commands.Add(("Open paragraph layout sample", () =>
        {
            FlushEdits(); Session.EditDocument("Insert paragraph layout sample", d =>
            {
                var sample = DocumentLayout.Resize(ParagraphLayoutSample.Create(), d.Width, d.Height);
                var result = d with { Slides = d.Slides.AddRange(sample.Slides) }; DocumentSerializer.Validate(result); return result;
            });
            Session.SelectSlide(Session.Document.Slides.Length - 1); ShowNormal(); Viewport.Fit();
        }));
    }
}
