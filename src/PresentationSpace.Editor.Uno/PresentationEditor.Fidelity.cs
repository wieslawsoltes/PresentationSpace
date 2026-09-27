using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PresentationSpace.Core;
using Windows.System;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void BuildFidelityCommands()
    {
        void Add(string title, Action execute) => _commands.Add((title, () => { execute(); Viewport.Focus(FocusState.Programmatic); }));
        foreach (string layout in SlideLayoutEngine.Layouts)
        {
            string name = layout;
            Add("Apply " + name + " layout", () => SetLayout(name));
        }
        Add("Bold selected text", Viewport.ToggleBold);
        Add("Italic selected text", Viewport.ToggleItalic);
        Add("Underline selected text", Viewport.ToggleUnderline);
        Add("Font color red", () => Viewport.FormatText("Font color", style => style with { Color = "#D00000" }));
        Add("Font color blue", () => Viewport.FormatText("Font color", style => style with { Color = "#0050D0" }));
        foreach (var kind in Enum.GetValues<ChartKind>())
            Add("Chart type " + kind, () => ChangeChartType(kind));
        foreach (var grouping in Enum.GetValues<ChartGrouping>())
            Add("Chart grouping " + grouping, () => ChangeChartGrouping(grouping));
        _commands.Add(("Edit chart data", () => { if(Session.PrimaryShape?.Kind==ShapeKind.Chart) { ShowInspector(Controls.Uno.InspectorMode.Format); DispatcherQueue.TryEnqueue(()=>_format.FocusChartData()); } else Notice("Select a chart first."); }));
        BuildTableCommands();
        BuildPerformanceCommands();
        KeyDown += HandleGlobalEditingKey;
    }

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
    }

    private void HandleGlobalEditingKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _busy || IsPresenting || !Key(VirtualKey.Control) || Key(VirtualKey.Menu) || XamlRoot is not { } root) return;
        if (FocusManager.GetFocusedElement(root) is TextBox or PasswordBox or AutoSuggestBox) return;
        switch (e.Key)
        {
            case VirtualKey.Z: FlushEdits(); if (Key(VirtualKey.Shift)) Session.Redo(); else Session.Undo(); break;
            case VirtualKey.Y: FlushEdits(); Session.Redo(); break;
            case VirtualKey.B: Viewport.ToggleBold(); break;
            case VirtualKey.I: Viewport.ToggleItalic(); break;
            case VirtualKey.U: Viewport.ToggleUnderline(); break;
            default: return;
        }
        e.Handled = true;
    }
}
