using System.Windows;

namespace Shelly.Views;

/// <summary>Opacity of the navigation labels, set once on the pane and inherited by every NavItem, so the labels fade
/// with one animation while the pane opens or closes.</summary>
public static class NavPane
{
    public static readonly DependencyProperty LabelOpacityProperty = DependencyProperty.RegisterAttached(
        "LabelOpacity", typeof(double), typeof(NavPane), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.Inherits));
    public static double GetLabelOpacity(DependencyObject d) => (double)d.GetValue(LabelOpacityProperty);
    public static void SetLabelOpacity(DependencyObject d, double v) => d.SetValue(LabelOpacityProperty, v);
}
