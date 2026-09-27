from pathlib import Path
import re
r=Path('.')
p=r/'src/PresentationSpace.Rendering.Skia/SlideRenderer.cs';s=p.read_text().replace('public sealed class SlideRenderer','public sealed partial class SlideRenderer');s=s.replace('if (!string.IsNullOrEmpty(s.Text)) DrawText(c,s.Text,b,s.TextStyle,s.Kind == ShapeKind.Text ? 3 : 12);','''if (!string.IsNullOrEmpty(s.Text))
        {
            if (s.TextRanges.IsEmpty) DrawText(c,s.Text,b,s.TextStyle,s.Kind == ShapeKind.Text ? 3 : 12);
            else DrawRichText(c,s,s.Kind == ShapeKind.Text ? 3 : 12);
        }''');p.write_text(s)
p=r/'src/PresentationSpace.Controls.Uno/SlideViewport.cs';s=p.read_text().replace('public sealed class SlideViewport','public sealed partial class SlideViewport');s=s.replace('        _editor.LostFocus += (_, _) => CommitText();','        _editor.SelectionChanged += (_, _) => CaptureTextSelection();\n        _editor.LostFocus += (_, _) => CommitText();')
s=s.replace('        string text = editor.Text;','        CaptureTextSelection();\n        string text = editor.Text;')
s=s.replace('        var editor = _editor;\n        _editor = null;', '        var editor = _editor;\n        _textSelection = null;\n        _editor = null;')
s=s.replace('private void OnChanged(object? sender, EditorChangedEventArgs e) => Refresh();','''private void OnChanged(object? sender, EditorChangedEventArgs e)
    {
        if (_textSelection is { } selection && (Session?.CurrentSlide.Id != selection.SlideId || Session?.PrimaryShape?.Id != selection.ShapeId)) _textSelection = null;
        var shape = Session?.PrimaryShape;
        AutomationProperties.SetName(this, shape is null ? "Slide editing canvas" : $"Selected {shape.Kind}: {shape.Name}. {shape.AlternativeText}");
        Refresh();
    }''')
s=s.replace('    private void Pressed(object sender, PointerRoutedEventArgs e)\n    {','    private void Pressed(object sender, PointerRoutedEventArgs e)\n    {\n        _textSelection = null;')
s=s.replace('Geometry.Resize(shape.Bounds, _handle,','Geometry.ResizeRotated(shape.Bounds, shape.Rotation, _handle,')
p.write_text(s)
p=r/'src/PresentationSpace.Editor.Uno/PresentationEditor.Ribbon.cs';s=p.read_text()
pattern=r'Session\.Apply\("([^"]+)",s=>s with\{TextStyle=s\.TextStyle with\{([^{}]+)\}\}\)'
s,n=re.subn(pattern,lambda m:'Viewport.FormatText("'+m[1]+'",style=>style with{'+m[2].replace('s.TextStyle.','style.')+'})',s)
assert n==11,n
s=s.replace('private void SetLayout(string layout){Session.EditSlide("Slide layout",s=>{var template=SlideFactory.Create(layout,Session.Document.Width,Session.Document.Height);return s with{Name=layout,Shapes=template.Shapes.AddRange(s.Shapes.Where(x=>x.Kind!=ShapeKind.Text))};});}', 'private void SetLayout(string layout){FlushEdits();Session.ApplyLayout(layout);}')
p.write_text(s)
p=r/'src/PresentationSpace.Editor.Uno/PresentationEditor.cs';s=p.read_text().replace('Session.PrimaryShape?.TextStyle','(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)');p.write_text(s)
p=r/'src/PresentationSpace.Controls.Uno/FormatPane.cs';s=p.read_text().replace('            Section("Fill");Palette', '''            Section("Accessibility");var alternative=new TextBox{Header="Alternative text",Text=shape.AlternativeText,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=12};
            alternative.LostFocus+=(_,_)=>{if(alternative.Text!=s.PrimaryShape?.AlternativeText)s.Apply("Alternative text",x=>x with{AlternativeText=alternative.Text});};_body.Children.Add(alternative);
            Section("Fill");Palette''');p.write_text(s)
p=r/'Directory.Build.props';p.write_text(p.read_text().replace('<Version>0.1.0</Version>','<Version>0.2.0</Version>'))
p=r/'tests/PresentationSpace.Tests/FormatTests.cs';s=p.read_text();s=s.replace('ExportWarnsAboutChartFlattening(){var result=PptxCodec.Export(SlideFactory.Welcome());Assert.Contains(result.Warnings,x=>x.Contains("Charts"));}', 'ExportDoesNotFlattenCharts(){var result=PptxCodec.Export(SlideFactory.Welcome());Assert.DoesNotContain(result.Warnings,x=>x.Contains("Charts are exported as editable bars"));}');p.write_text(s)
