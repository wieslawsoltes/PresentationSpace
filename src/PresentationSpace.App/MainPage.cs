using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Editor.Uno;
namespace PresentationSpace.App;

public sealed class MainPage : Page
{
    private readonly PresentationEditor _editor;
    private bool _initialized;
    public MainPage()
    {
        _editor=new(new EditorSession(SlideFactory.Welcome()),new LocalWorkspaceStorage());Content=_editor;
        _editor.Session.Changed+=(_,_)=>UpdateDiagnostics();_editor.ViewChanged+=(_,_)=>UpdateDiagnostics();
        Loaded+=async(_,_)=>
        {
            if(_initialized)return;_initialized=true;
#if __WASM__
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("document.documentElement.setAttribute('data-presentationspace','ready');document.addEventListener('keydown',e=>{if(e.key==='F5'||(e.ctrlKey&&['s','o','m'].includes(e.key.toLowerCase())))e.preventDefault();});");
#endif
            UpdateDiagnostics();await _editor.RestoreRecoveryAsync();
        };
    }
    private void UpdateDiagnostics()
    {
#if __WASM__
        var session=_editor.Session;string title=Uri.EscapeDataString(session.Document.Title+" — PresentationSpace");
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.title=decodeURIComponent('{title}');document.documentElement.setAttribute('data-slide-count','{session.Document.Slides.Length}');document.documentElement.setAttribute('data-slide-index','{session.SlideIndex}');document.documentElement.setAttribute('data-shape-count','{session.CurrentSlide.Shapes.Length}');document.documentElement.setAttribute('data-selection-count','{session.Selection.Count}');document.documentElement.setAttribute('data-presenting','{(_editor.IsPresenting?"true":"false")}');");
#endif
    }
}
