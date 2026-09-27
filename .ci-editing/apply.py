from pathlib import Path

def edit(path, changes):
    p=Path(path); s=p.read_text()
    for before, after, count in changes:
        assert s.count(before)==count, (path, before, s.count(before), count)
        s=s.replace(before,after)
    p.write_text(s)

edit('src/PresentationSpace.Core/EditorSession.cs', [('x => x with { Text = x.Text.Replace(find,replacement,StringComparison.OrdinalIgnoreCase) }','x => RichTextEditing.ReplaceAll(x, find, replacement)',1)])
edit('src/PresentationSpace.Editor.Uno/PresentationEditor.cs', [('(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle).','(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)?.',5)])
edit('src/PresentationSpace.Rendering.Skia/SlideRenderer.RichText.cs', [('if (piece.Width <= width) { Add(piece); continue; }','if (piece.Width <= width) { if (used > 0 && used + piece.Width > width) Finish(); Add(piece); continue; }',1)])
edit('src/PresentationSpace.Controls.Uno/SlideViewport.cs', [
    ('        _textSelection = null;\n        if (Session','        if (Session',1),
    ('        CommitText();\n        Focus(FocusState.Pointer);','        CommitText();\n        _textSelection = null;\n        Focus(FocusState.Pointer);',1),
    ('        _editingId = shape.Id;','        _textDraft = shape;\n        _editingId = shape.Id;',1),
    ('        _editor.SelectionChanged +=','        _editor.TextChanged += (_, _) => UpdateTextDraft();\n        _editor.KeyDown += HandleFormattingKey;\n        _editor.SelectionChanged +=',1),
    ('        string text = editor.Text;','        string text = editor.Text;\n        var draft = _textDraft;\n        _textDraft = null;',1),
    ('shape with { Text = text })','shape with { Text = text, TextRanges = draft?.TextRanges ?? shape.TextRanges })',1),
    ('        _textSelection = null;\n        _editor = null;','        _textSelection = null;\n        _textDraft = null;\n        _editor = null;',1),
    ('        if (_editor is not null || Session is not { } session) return;','        HandleFormattingKey(sender, e);\n        if (e.Handled || _editor is not null || Session is not { } session) return;',1)
])
edit('src/PresentationSpace.Editor.Uno/PresentationEditor.Ribbon.cs', [
    ('        Ribbon.SetTabs(', '        BuildFidelityCommands();\n        Ribbon.SetTabs(',1),
    ('()=>Viewport.FormatText("Bold",style=>style with{Bold=!style.Bold})','Viewport.ToggleBold',1),
    ('()=>Viewport.FormatText("Italic",style=>style with{Italic=!style.Italic})','Viewport.ToggleItalic',1),
    ('()=>Viewport.FormatText("Underline",style=>style with{Underline=!style.Underline})','Viewport.ToggleUnderline',1),
    ('()=>Viewport.FormatText("Bullets",style=>style with{Bullets=!style.Bullets})','Viewport.ToggleBullets',1),
    ('Viewport.FormatText("Text alignment"','Viewport.FormatParagraph("Text alignment"',1),
    ('Viewport.FormatText("Line spacing"','Viewport.FormatParagraph("Line spacing"',1)
])
