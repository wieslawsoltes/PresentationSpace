using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Controls.Uno;

public sealed partial class TextLayoutEditor
{
    private readonly TextBox _tabs = new() { AcceptsReturn = true, Header = "Custom tab stops", FontSize = 12,
        MinWidth = 0, MinHeight = 84, MaxHeight = 150, TextWrapping = TextWrapping.NoWrap,
        PlaceholderText = "120 Left\n240 Center\n420 Decimal", Padding = new(6) };
    private void BuildTabsEditor()
    {
        AutomationProperties.SetName(_tabs, "Custom tab stops");
        AutomationProperties.SetAutomationId(_tabs, "text-layout-tabs");
        _tabs.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            if ((state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) { Apply(); e.Handled = true; }
        };
        _body.Children.Add(_tabs);
        var hint = OfficePalette.Text("One position and Left, Center, Right or Decimal per line. Ascending slide units from the content edge. Decimal uses a period. Ctrl+Enter applies; clear to use the regular interval.", 10);
        hint.TextWrapping = TextWrapping.Wrap; _body.Children.Add(hint);
    }
    public void FocusTabStops() { _tabs.Focus(FocusState.Programmatic); _tabs.SelectAll(); }
}
