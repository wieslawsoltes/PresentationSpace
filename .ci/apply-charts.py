from pathlib import Path
import hashlib

# Exact-baseline integration. All offsets are Unicode code-point offsets, guarded by Git blob hashes.
def patch(path, sha, changes):
    p = Path(path)
    raw = p.read_bytes()
    assert hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest() == sha, path
    text = raw.decode('utf-8')
    for start, end, replacement in reversed(changes):
        text = text[:start] + replacement + text[end:]
    p.write_text(text, encoding='utf-8')

patch('Directory.Build.props', '655ec76b92bfb69bc074a1b2dba2015c29ae46ed', [(142,171,'    <Version>0.3.0</Version>\n')])
patch('src/PresentationSpace.Core/Model.cs', '832ba736b3f945be4f75eeea5adb671415f62b09', [
    (3301,3301,'    public ChartSpec? Chart { get; init; }\n'),
    (4979,5138,'    public static string Serialize(PresentationDocument document) => JsonSerializer.Serialize(document.Slides.Any(s => s.Shapes.Any(x => x.Chart is not null)) ? document with { SchemaVersion = 2 } : document, PresentationJsonContext.Default.PresentationDocument);\n'),
    (5616,5734,'        if (d.SchemaVersion is not (1 or 2)) throw new InvalidDataException($"Unsupported document version {d.SchemaVersion}.");\n'),
    (8792,8792,'                if (s.Chart is { } chart) ChartModel.Validate(chart);\n                if (s.Values.Length > ChartModel.MaxCategories) throw new InvalidDataException("Too many legacy chart values.");\n')])
patch('src/PresentationSpace.Formats/PptxCodec.Native.cs', '5de1b2ff23a3c714f5a9abd7eb32a1d7dcbdb9ce', [(225,271,''),(7891,17768,'')])
patch('src/PresentationSpace.Rendering.Skia/SlideRenderer.cs', '0dcfd64332342b6f3432875a645c8f25bcf5f316', [(9951,11399,'')])
patch('tests/PresentationSpace.Tests/FidelityTests.cs', '2016dce92a8dcdafbb226474e862d804fca44729', [
    (14986,15070,'    [Fact] public void DuplicateSeriesIdentifiersAreRejectedInsteadOfSilentlyDroppingSeries()\n'),
    (15290,15456,'        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));\n')])
patch('src/PresentationSpace.Controls.Uno/FormatPane.cs', 'a044b176e47a8b3a63094432e70c319efca684f1', [
    (553,553,'    private ChartDataEditor? _chartEditor;\n    public void FocusChartData() => _chartEditor?.FocusData();\n'),
    (2219,2474,'            _chartEditor=null;_body.Children.Clear();_lastSelection=s.PrimaryShape?.Id;_lastShape=s.PrimaryShape;_title.Text=_mode switch{InspectorMode.Selection=>"Selection",InspectorMode.Comments=>"Comments",_=>s.PrimaryShape is null?"Format Background":"Format Shape"};\n'),
    (3345,3434,'''            if(shape.Kind==ShapeKind.Chart)
            {
                Section("Chart design");
                if(s.Selection.Count==1)
                {
                    _chartEditor=new ChartDataEditor();_chartEditor.SetValue(ChartModel.Get(shape));
                    var id=shape.Id;
                    _chartEditor.ValueChanged+=(_,chart)=>s.EditSlide("Edit chart",slide=>slide with{Shapes=slide.Shapes.Select(x=>x.Id==id&&!x.Locked?ChartModel.Apply(x,chart):x).ToImmutableArray()});
                    _body.Children.Add(_chartEditor);
                }
                else Hint("Select one chart to edit its data and design.");
            }
            else {Section("Fill");Palette(color=>s.Apply("Shape fill",x=>x with{Fill=color}));}
'''), (5204,6208,'')])
patch('src/PresentationSpace.Editor.Uno/PresentationEditor.Fidelity.cs', 'e10bbce60e7420cc6848696e8748dd43407ad42e', [
    (1014,1014,'''        foreach (var kind in Enum.GetValues<ChartKind>())
            Add("Chart type " + kind, () => ChangeChartType(kind));
        foreach (var grouping in Enum.GetValues<ChartGrouping>())
            Add("Chart grouping " + grouping, () => ChangeChartGrouping(grouping));
        _commands.Add(("Edit chart data", () => { if(Session.PrimaryShape?.Kind==ShapeKind.Chart) { ShowInspector(Controls.Uno.InspectorMode.Format); DispatcherQueue.TryEnqueue(()=>_format.FocusChartData()); } else Notice("Select a chart first."); }));
'''), (1057,1057,'''    }

    private void ChangeChartType(ChartKind kind)
    {
        if(Session.PrimaryShape is not {Kind:ShapeKind.Chart} shape) { Notice("Select a chart first."); return; }
        var next=ChartModel.Get(shape) with { Kind=kind };
        if(kind is not (ChartKind.Column or ChartKind.Bar))next=next with{Grouping=ChartGrouping.Clustered};
        try { ChartModel.Validate(next); Session.Apply("Chart type",x=>x.Id==shape.Id?ChartModel.Apply(x,next):x); }
        catch(InvalidDataException e) { Notice(e.Message,true); }
    }
    private void ChangeChartGrouping(ChartGrouping grouping)
    {
        if(Session.PrimaryShape is not {Kind:ShapeKind.Chart} shape) { Notice("Select a chart first."); return; }
        var next=ChartModel.Get(shape) with{Grouping=grouping};
        try { ChartModel.Validate(next); Session.Apply("Chart grouping",x=>x.Id==shape.Id?ChartModel.Apply(x,next):x); }
        catch(InvalidDataException e) { Notice(e.Message,true); }
''')])
patch('src/PresentationSpace.App/MainPage.cs', '1d7b13dc0da617e55d74d2aeba1529fdb08beba1', [
    (1467,1467,'        var chart = session.PrimaryShape?.Kind == ShapeKind.Chart ? ChartModel.Get(session.PrimaryShape) : null;\n'),
    (1651,2949,'''        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-chart-type','{chart?.Kind.ToString() ?? ""}');document.documentElement.setAttribute('data-chart-series','{chart?.Series.Length ?? 0}');document.documentElement.setAttribute('data-chart-categories','{chart?.Categories.Length ?? 0}');document.documentElement.setAttribute('data-chart-grouping','{chart?.Grouping.ToString() ?? ""}');document.documentElement.setAttribute('data-chart-missing','{chart?.Series.Sum(s=>s.Values.Count(v=>v is null)) ?? 0}');document.title=decodeURIComponent('{title}');document.documentElement.setAttribute('data-slide-count','{session.Document.Slides.Length}');document.documentElement.setAttribute('data-slide-index','{session.SlideIndex}');document.documentElement.setAttribute('data-shape-count','{session.CurrentSlide.Shapes.Length}');document.documentElement.setAttribute('data-selection-count','{session.Selection.Count}');document.documentElement.setAttribute('data-presenting','{(_editor.IsPresenting ? "true" : "false")}');document.documentElement.setAttribute('data-active-layout',decodeURIComponent('{layout}'));document.documentElement.setAttribute('data-text-length','{session.CurrentSlide.Shapes.Sum(shape => shape.Text.Length)}');document.documentElement.setAttribute('data-primary-text-length','{session.PrimaryShape?.Text.Length ?? 0}');document.documentElement.setAttribute('data-primary-range-count','{session.PrimaryShape?.TextRanges.Length ?? 0}');document.documentElement.setAttribute('data-primary-range-start','{firstRange?.Start ?? -1}');document.documentElement.setAttribute('data-primary-range-length','{firstRange?.Length ?? 0}');document.documentElement.setAttribute('data-command-version','{_editor.CommandExecutionVersion}');");
''')])
patch('tools/browser-smoke.py', '74014e41b9c52c765fc33519664b5f389ceee29d', [(5487,5487,'''        command('Insert chart')
        attr('data-chart-series', 1)
        command('Edit chart data')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'TEXTAREA'", timeout=20000)
        page.keyboard.insert_text('Category\\tActual\\tPlan\\nQ1\\t10\\t15\\nQ2\\t\\t18\\nQ3\\t25\\t22')
        page.keyboard.press('Control+Enter')
        attr('data-chart-series', 2)
        attr('data-chart-categories', 3)
        attr('data-chart-missing', 1)
        command('Chart type Line')
        attr('data-chart-type', 'Line')
        page.screenshot(path=str(output / 'chart-line.png'), full_page=True)
        command('Chart type Bar')
        command('Chart grouping Stacked')
        attr('data-chart-type', 'Bar')
        attr('data-chart-grouping', 'Stacked')
        page.keyboard.press('Control+z')
        attr('data-chart-grouping', 'Clustered')
        page.keyboard.press('Control+y')
        attr('data-chart-grouping', 'Stacked')
        page.screenshot(path=str(output / 'chart-stacked.png'), full_page=True)
        command('Edit chart data')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'TEXTAREA'", timeout=20000)
        page.keyboard.insert_text('Category\\tRevenue\\nProduct A\\t45\\nProduct B\\t30\\nProduct C\\t25')
        page.keyboard.press('Control+Enter')
        attr('data-chart-series', 1)
        command('Chart type Doughnut')
        attr('data-chart-type', 'Doughnut')
        page.screenshot(path=str(output / 'chart-doughnut.png'), full_page=True)
        print('PASS: real chart-data input, two series, missing values, type changes, stacked grouping, undo/redo and doughnut conversion.', flush=True)
''')])
