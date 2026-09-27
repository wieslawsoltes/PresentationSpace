from pathlib import Path
root=Path('.')
def change(path, old, new):
 p=root/path;s=p.read_text();assert s.count(old)==1,(path,s.count(old));p.write_text(s.replace(old,new))
change('src/PresentationSpace.Controls.Uno/SlideViewport.cs','        KeyDown += OnKeyDown;', '        PreviewKeyDown += HandleTablePreviewKey;\n        KeyDown += OnKeyDown;')
change('src/PresentationSpace.Controls.Uno/SlideViewport.Tables.cs', '    private bool HandleTableKey(KeyRoutedEventArgs e)\n', '''    private void HandleTablePreviewKey(object sender, KeyRoutedEventArgs e)
    {
        // Tab belongs to the selected table, not the surrounding XAML focus traversal.
        // Handle it before a native text control or focus manager consumes the key.
        if (e.Handled || e.Key != VirtualKey.Tab || Key(VirtualKey.Control) || ActiveTable is null) return;
        e.Handled = true;
        bool editing = _cellEditor is not null;
        CommitCellText();
        MoveTableCell(Key(VirtualKey.Shift) ? -1 : 1);
        if (editing) EditTableCell();
        else Focus(FocusState.Programmatic);
    }
    private bool HandleTableKey(KeyRoutedEventArgs e)
''')
change('src/PresentationSpace.App/MainPage.cs','        _editor.ViewChanged += (_, _) => UpdateDiagnostics();','        _editor.ViewChanged += (_, _) => UpdateDiagnostics();\n        GotFocus += (_, _) => UpdateDiagnostics();')
change('src/PresentationSpace.App/MainPage.cs', '        var cell = _editor.Viewport.ActiveTableRange;', '''        var cell = _editor.Viewport.ActiveTableRange;
        var focused = XamlRoot is null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        string focusName = Uri.EscapeDataString(focused is null ? "" : Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) + ":" + focused.GetType().Name);
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-focus-id',decodeURIComponent('{focusName}')); ");''')
change('tools/browser-performance.py', "        assert abs(float(value('data-primary-height')) - height) < .01", '''        page.wait_for_function('(h) => Math.abs(Number(document.documentElement.getAttribute("data-primary-height")) - h) < .01', arg=height)''')
