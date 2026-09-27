using System.Collections.Immutable;
using PresentationSpace.Core;

namespace PresentationSpace.Controls.Uno;

public sealed partial class SlideViewport
{
    private sealed record TextSelection(Guid SlideId, Guid ShapeId, int Start, int Length);
    private TextSelection? _textSelection;

    private void CaptureTextSelection()
    {
        if (_editor is { } editor && _editingSlideId is { } slide && _editingId is { } shape)
            _textSelection = new(slide, shape, editor.SelectionStart, editor.SelectionLength);
    }

    public TextStyle? CurrentTextStyle
    {
        get
        {
            CaptureTextSelection();
            if (Session?.PrimaryShape is not { } shape) return null;
            return _textSelection is { } selection && selection.SlideId == Session.CurrentSlide.Id && selection.ShapeId == shape.Id
                ? RichText.StyleAt(shape, Math.Min(selection.Start, Math.Max(0, shape.Text.Length - 1))) : shape.TextStyle;
        }
    }

    /// <summary>Formats the selected characters, or all selected shapes when no text range is active.</summary>
    public void FormatText(string label, Func<TextStyle, TextStyle> format)
    {
        CaptureTextSelection(); CommitText();
        if (Session is not { } session) return;
        if (_textSelection is { Length: > 0 } selection && selection.SlideId == session.CurrentSlide.Id && session.Selection.Count == 1 && session.PrimaryShape is { Locked: false } shape && selection.ShapeId == shape.Id)
        {
            int start = Math.Min(selection.Start, shape.Text.Length), length = Math.Min(selection.Length, shape.Text.Length - start);
            while (!RichText.IsBoundary(shape.Text, start)) start--;
            int end = Math.Min(shape.Text.Length, selection.Start + length);
            while (!RichText.IsBoundary(shape.Text, end)) end++;
            session.Apply(label, value => RichText.Format(value, start, end - start, format));
        }
        else session.Apply(label, value => value with { TextStyle = format(value.TextStyle), TextRanges = value.TextRanges.Select(range => range with { Style = format(range.Style) }).ToImmutableArray() });
        Refresh();
    }
}
