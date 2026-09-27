using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace PresentationSpace.Ribbon.Uno;

/// <summary>A compact, keyboard-operable switch with ToggleButton automation semantics.
/// The 40×20 visual is centered inside a 48×32 hit target; no theme header/content row is clipped.</summary>
public sealed class CompactToggleSwitch : ToggleButton
{
    private readonly Border _track;
    private readonly Border _thumb;
    public FrameworkElement Track => _track;
    public FrameworkElement Thumb => _thumb;
    public CompactToggleSwitch()
    {
        Width = 48; Height = 32; MinWidth = MinHeight = 0; Padding = new(4, 6, 4, 6);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        BorderThickness = new(0); CornerRadius = new(4);
        Background = OfficePalette.Brush("00FFFFFF");
        // An explicit compiled template keeps global ToggleButton checked-state brushes
        // from painting a rectangular background behind the capsule.
        Template = (ControlTemplate)new CompactControlResources()["CompactToggleTemplate"];
        UseSystemFocusVisuals = true;
        _thumb = new Border { Width = 14, Height = 14, CornerRadius = new(7), Background = OfficePalette.White,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(3, 0, 3, 0) };
        _track = new Border { Width = 40, Height = 20, CornerRadius = new(10), Child = _thumb };
        Content = _track;
        Checked += (_, _) => UpdateVisual(); Unchecked += (_, _) => UpdateVisual();
        IsEnabledChanged += (_, _) => UpdateVisual();
        AutomationProperties.SetName(this, "AutoSave local recovery");
        UpdateVisual();
    }
    private void UpdateVisual()
    {
        if (_track is null) return;
        _track.Background = IsChecked == true ? OfficePalette.Accent : OfficePalette.Brush("777777");
        _thumb.HorizontalAlignment = IsChecked == true ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _track.Opacity = IsEnabled ? 1 : .45;
    }
}
