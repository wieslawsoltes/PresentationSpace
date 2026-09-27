using System.Collections.Immutable;
using Microsoft.UI.Xaml.Input;
using PresentationSpace.Core;
using Windows.System;

namespace PresentationSpace.Controls.Uno;

public sealed partial class SlideViewport
{
    private sealed record TextSelection(Guid SlideId, Guid ShapeId, int Start, int Length);
    private TextSelection? _textSelection;
    private SlideShape? _textDraft;

    private void UpdateTextDraft()
    {
        if (_editor is { } editor && _textDraft is { } draft && editor.Text != draft.Text)
            _textDraft = RichText.Reconcile(draft, draft with { Text = editor.Text });
    }

    private void CaptureTextSelection()
    {
        UpdateTextDraft();
        if (_editor is { } editor && _editingSlideId is { } slide && _editingId is { } shape)
            _textSelection = new(slide, shape, editor.SelectionStart, editor.SelectionLength);
    }

    public TextStyle? CurrentTextStyle
    {
        get
        {
            if (CurrentTableTextStyle() is { } cellStyle) return cellStyle;
            CaptureTextSelection();
            if (Session?.PrimaryShape is not { } selected) return null;
            var shape = _textDraft is { } draft && draft.Id == selected.Id ? draft : selected;
            return _textSelection is { } selection && selection.SlideId == Session.CurrentSlide.Id && selection.ShapeId == shape.Id
                ? RichText.StyleAt(shape, Math.Min(selection.Start, Math.Max(0, shape.Text.Length - 1))) : shape.TextStyle;
        }
    }

    public void ToggleBold() { bool enabled = CurrentTextStyle?.Bold != true; FormatText("Bold", style => style with { Bold = enabled }); }
    public void ToggleItalic() { bool enabled = CurrentTextStyle?.Italic != true; FormatText("Italic", style => style with { Italic = enabled }); }
    public void ToggleUnderline() { bool enabled = CurrentTextStyle?.Underline != true; FormatText("Underline", style => style with { Underline = enabled }); }
    public void ToggleBullets() { bool enabled = CurrentTextStyle?.Bullets != true; FormatParagraph("Bullets", style => style with { Bullets = enabled }); }

    /// <summary>Formats selected characters; with no selected characters, formats the selected objects.</summary>
    public void FormatText(string label, Func<TextStyle, TextStyle> format) => FormatTextCore(label, format, false);
    public void FormatParagraph(string label, Func<TextStyle, TextStyle> format) => FormatTextCore(label, format, true);

    private void FormatTextCore(string label, Func<TextStyle, TextStyle> format, bool paragraph)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (FormatTableText(label, format, paragraph)) { Refresh(); return; }
        CaptureTextSelection(); CommitText();
        if (Session is not { } session) return;
        if (_textSelection is { } selection && (selection.Length > 0 || paragraph) && selection.SlideId == session.CurrentSlide.Id && session.Selection.Count == 1 && session.PrimaryShape is { Locked: false } shape && selection.ShapeId == shape.Id)
        {
            int start = Math.Min(selection.Start, shape.Text.Length);
            int end = Math.Min(shape.Text.Length, selection.Start + selection.Length);
            while (!RichText.IsBoundary(shape.Text, start)) start--;
            while (!RichText.IsBoundary(shape.Text, end)) end++;
            session.Apply(label, value => paragraph ? RichTextEditing.FormatParagraphs(value, start, end - start, format) : RichText.Format(value, start, end - start, format));
        }
        else session.Apply(label, value => value with { TextStyle = format(value.TextStyle), TextRanges = value.TextRanges.Select(range => range with { Style = format(range.Style) }).ToImmutableArray() });
        Refresh();
    }

    private void HandleFormattingKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || !Key(VirtualKey.Control) || Key(VirtualKey.Menu) || e.Key is not (VirtualKey.B or VirtualKey.I or VirtualKey.U)) return;
        bool editing = _editor is not null;
        CaptureTextSelection(); var selection = _textSelection;
        switch (e.Key) { case VirtualKey.B: ToggleBold(); break; case VirtualKey.I: ToggleItalic(); break; case VirtualKey.U: ToggleUnderline(); break; }
        if (editing && selection is not null)
        {
            EditText();
            _editor?.Select(selection.Start, selection.Length);
        }
        e.Handled = true;
    }
}
