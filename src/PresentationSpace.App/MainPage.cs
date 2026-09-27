using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Editor.Uno;
namespace PresentationSpace.App;

public sealed class MainPage : Page
{
    private readonly PresentationEditor _editor;
    private bool _initialized, _diagnosticsQueued;
    private TableSpec? _diagnosticTable;
    private int _zeroBorders, _mergedCells, _tableTextLength;
    public MainPage()
    {
        _editor = new(new EditorSession(SlideFactory.Welcome()), new LocalWorkspaceStorage());
        Content = _editor;
        _editor.Session.Changed += (_, _) => QueueDiagnostics();
        _editor.ViewChanged += (_, _) => QueueDiagnostics();
        SizeChanged += (_, _) => QueueDiagnostics();
        _editor.Ribbon.TabChanged += (_, _) => QueueDiagnostics();
        GotFocus += (_, _) => QueueDiagnostics();
        Loaded += async (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            await _editor.RestoreRecoveryAsync();
            QueueDiagnostics();
            _editor.Viewport.Focus(FocusState.Programmatic);
#if __WASM__
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("document.addEventListener('keydown',e=>{if(e.key==='F5'||(e.ctrlKey&&['s','o','m','b','i','u'].includes(e.key.toLowerCase())))e.preventDefault();});requestAnimationFrame(()=>requestAnimationFrame(()=>document.documentElement.setAttribute('data-presentationspace','ready')));");
#endif
        };
    }
    private void QueueDiagnostics()
    {
        if (_diagnosticsQueued) return;
        _diagnosticsQueued = true;
        if (!DispatcherQueue.TryEnqueue(() => { _diagnosticsQueued = false; if (_editor.IsLoaded) UpdateDiagnostics(); })) _diagnosticsQueued = false;
    }
    private void UpdateDiagnostics()
    {
#if __WASM__
        string chrome = Uri.EscapeDataString(_editor.GetChromeDiagnostics());
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-ui-chrome',decodeURIComponent('{chrome}'));document.documentElement.setAttribute('data-autosave','{(_editor.AutoSaveEnabled ? "true" : "false")}');document.documentElement.setAttribute('data-ribbon-tab','{_editor.Ribbon.SelectedTab}');");
        var session = _editor.Session;
        var table = session.PrimaryShape?.Table;
        if (!ReferenceEquals(table, _diagnosticTable))
        {
            _diagnosticTable = table; _zeroBorders = _mergedCells = _tableTextLength = 0;
            if (table is not null) foreach (var item in table.Cells)
            {
                _zeroBorders += (item.Top.Width == 0 ? 1 : 0) + (item.Bottom.Width == 0 ? 1 : 0) + (item.Left.Width == 0 ? 1 : 0) + (item.Right.Width == 0 ? 1 : 0);
                if (item.RowSpan > 1 || item.ColumnSpan > 1) _mergedCells++;
                _tableTextLength += item.Text.Length;
            }
        }
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-table-accent','{table?.Accent ?? ""}');document.documentElement.setAttribute('data-table-first-column','{(table?.FirstColumn == true ? "true" : "false")}');document.documentElement.setAttribute('data-table-last-column','{(table?.LastColumn == true ? "true" : "false")}');document.documentElement.setAttribute('data-table-banded-columns','{(table?.BandedColumns == true ? "true" : "false")}');document.documentElement.setAttribute('data-table-zero-borders','{_zeroBorders}');");
        var cell = _editor.Viewport.ActiveTableRange;
        var focused = XamlRoot is null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        string focusName = Uri.EscapeDataString(focused is null ? "" : Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) + ":" + focused.GetType().Name);
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-focus-id',decodeURIComponent('{focusName}')); ");
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-filmstrip-allocated','{_editor.AllocatedFilmstripTiles}');document.documentElement.setAttribute('data-sorter-allocated','{_editor.AllocatedSorterTiles}');document.documentElement.setAttribute('data-filmstrip-realized','{_editor.RealizedFilmstripTiles}');document.documentElement.setAttribute('data-sorter-realized','{_editor.RealizedSorterTiles}');document.documentElement.setAttribute('data-table-cell-row','{cell?.Row ?? -1}');document.documentElement.setAttribute('data-table-cell-column','{cell?.Column ?? -1}');document.documentElement.setAttribute('data-primary-height','{session.PrimaryShape?.Bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""}');document.documentElement.setAttribute('data-canvas-backend','SKCanvasElement');");
        string title = Uri.EscapeDataString(session.Document.Title + " — PresentationSpace");
        string layout = Uri.EscapeDataString(session.CurrentSlide.LayoutName ?? "");
        var chart = session.PrimaryShape?.Kind == ShapeKind.Chart ? ChartModel.Get(session.PrimaryShape) : null;
        var firstRange = session.PrimaryShape?.TextRanges.FirstOrDefault();
        // Read-only diagnostics: tests invoke real keyboard/pointer input, not a document-mutation bridge.
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-chart-type','{chart?.Kind.ToString() ?? ""}');document.documentElement.setAttribute('data-chart-series','{chart?.Series.Length ?? 0}');document.documentElement.setAttribute('data-chart-categories','{chart?.Categories.Length ?? 0}');document.documentElement.setAttribute('data-chart-grouping','{chart?.Grouping.ToString() ?? ""}');document.documentElement.setAttribute('data-chart-missing','{chart?.Series.Sum(s=>s.Values.Count(v=>v is null)) ?? 0}');document.title=decodeURIComponent('{title}');document.documentElement.setAttribute('data-slide-count','{session.Document.Slides.Length}');document.documentElement.setAttribute('data-slide-index','{session.SlideIndex}');document.documentElement.setAttribute('data-shape-count','{session.CurrentSlide.Shapes.Length}');document.documentElement.setAttribute('data-selection-count','{session.Selection.Count}');document.documentElement.setAttribute('data-presenting','{(_editor.IsPresenting ? "true" : "false")}');document.documentElement.setAttribute('data-active-layout',decodeURIComponent('{layout}'));document.documentElement.setAttribute('data-text-length','{session.CurrentSlide.Shapes.Sum(shape => shape.Text.Length)}');document.documentElement.setAttribute('data-primary-text-length','{session.PrimaryShape?.Text.Length ?? 0}');document.documentElement.setAttribute('data-primary-range-count','{session.PrimaryShape?.TextRanges.Length ?? 0}');document.documentElement.setAttribute('data-primary-range-start','{firstRange?.Start ?? -1}');document.documentElement.setAttribute('data-primary-range-length','{firstRange?.Length ?? 0}');document.documentElement.setAttribute('data-command-version','{_editor.CommandExecutionVersion}');document.documentElement.setAttribute('data-table-rows','{session.PrimaryShape?.Table?.RowCount ?? 0}');document.documentElement.setAttribute('data-table-columns','{session.PrimaryShape?.Table?.ColumnCount ?? 0}');document.documentElement.setAttribute('data-table-origins','{session.PrimaryShape?.Table?.Cells.Length ?? 0}');document.documentElement.setAttribute('data-table-merged','{_mergedCells}');document.documentElement.setAttribute('data-table-text-length','{_tableTextLength}');");
#endif
    }
}
