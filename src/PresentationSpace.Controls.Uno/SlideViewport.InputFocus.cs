using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PresentationSpace.Controls.Uno;

public sealed partial class SlideViewport
{
    private void RestoreNativeCanvasFocus(object sender, RoutedEventArgs e)
    {
#if __WASM__
        if (!HasTableCellSelection || _cellEditor is not null || XamlRoot is not { } root ||
            !ReferenceEquals(FocusManager.GetFocusedElement(root), this)) return;
        // Uno detaches its native text input in a microtask. Keep browser focus aligned
        // with this already-focused canvas after that detach. Otherwise the first Tab
        // is intercepted as an unfocused-page accessibility-activation key.
        // Never move focus from another input, semantic control, or accessibility button.
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("queueMicrotask(()=>queueMicrotask(()=>{const a=document.activeElement;if(document.hasFocus()&&(!a||a===document.body||a===document.documentElement)){const root=document.getElementById('uno-body');if(root){root.tabIndex=-1;root.focus({preventScroll:true});}}}));");
#endif
    }
}
