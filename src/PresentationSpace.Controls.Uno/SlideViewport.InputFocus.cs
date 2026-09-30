using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PresentationSpace.Controls.Uno;

public sealed partial class SlideViewport
{
#if __WASM__
    private bool _nativeCanvasFocusQueued;
#endif
    private void RestoreNativeCanvasFocus(object sender, RoutedEventArgs e)
    {
#if __WASM__
        if (_nativeCanvasFocusQueued || ActiveTable is null || _editor is not null || _cellEditor is not null ||
            XamlRoot is not { } root || !ReferenceEquals(FocusManager.GetFocusedElement(root), this)) return;
        // Removing native input can finish after the routed key/focus handlers.
        // Defer past that event, then recheck the actual managed focus and target;
        // an event from a closed cell must never steal a newly selected input's focus.
        _nativeCanvasFocusQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _nativeCanvasFocusQueued = false;
            if (!IsLoaded || !ReferenceEquals(XamlRoot, root) || ActiveTable is null || _editor is not null ||
                _cellEditor is not null || !ReferenceEquals(FocusManager.GetFocusedElement(root), this)) return;
            // Focus only the already-focused canvas' browser root, never another
            // input, semantic control or accessibility-activation button.
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("queueMicrotask(()=>{const a=document.activeElement;if(document.hasFocus()&&(!a||a===document.body||a===document.documentElement)){const target=document.getElementById('uno-body');if(target){target.tabIndex=-1;target.focus({preventScroll:true});}}});");
        })) _nativeCanvasFocusQueued = false;
#endif
    }
}
