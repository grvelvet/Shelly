using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Shelly.Views;

/// <summary>1.1.2: keeps list content out from under the vertical scroll bar. The WPF Fluent ScrollViewer template lets
/// the ScrollContentPresenter span both grid columns, so the 12 px scroll bar is drawn over the right edge of the content
/// (and widens its track on hover). Card borders, the selection border and the keyboard focus ring of the rightmost
/// items ended under the bar. ScrollGutter.Enabled adds right padding to the ScrollViewer while (and only while) the
/// vertical bar is visible: the measured overlap of bar and content plus a small gap. With a theme whose bar does not
/// overlap the content, the overlap is 0 and nothing changes. Works on a ScrollViewer or on a control that has one in
/// its template (ListBox).</summary>
public static class ScrollGutter
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollGutter), new PropertyMetadata(false, OnEnabled));
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    /// <summary>The padding the ScrollViewer had before the gutter (the gutter is added to its right side).</summary>
    static readonly DependencyProperty BaseProperty = DependencyProperty.RegisterAttached(
        "Base", typeof(Thickness?), typeof(ScrollGutter), new PropertyMetadata(null));
    /// <summary>The gutter currently added, in px.</summary>
    static readonly DependencyProperty ExtraProperty = DependencyProperty.RegisterAttached(
        "Extra", typeof(double), typeof(ScrollGutter), new PropertyMetadata(0.0));

    static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || !(bool)e.NewValue) return;
        fe.Loaded += (_, _) => Hook(fe);
        // A control's template can be applied again (theme change), which brings a new ScrollViewer: hook that one too.
        // Hook returns at once for a ScrollViewer it already knows.
        if (fe is not ScrollViewer) fe.LayoutUpdated += (_, _) => { if (fe.IsLoaded) Hook(fe); };
        if (fe.IsLoaded) Hook(fe);
    }

    static void Hook(FrameworkElement fe)
    {
        var sv = fe as ScrollViewer ?? Find<ScrollViewer>(fe);
        if (sv == null || sv.GetValue(BaseProperty) != null) return;
        sv.SetValue(BaseProperty, (Thickness?)sv.Padding);
        // ScrollChanged also fires when the extent or the viewport changes, which is when the bar appears or goes
        sv.ScrollChanged += (_, _) => Update(sv);
        sv.SizeChanged += (_, _) => Update(sv);
        Update(sv);
    }

    static void Update(ScrollViewer sv)
    {
        if (sv.GetValue(BaseProperty) is not Thickness b) return;
        double extra = (double)sv.GetValue(ExtraProperty);
        double want = sv.ComputedVerticalScrollBarVisibility == Visibility.Visible ? Gutter(sv, extra) : 0;
        if (Math.Abs(want - extra) < 0.5) return;
        sv.SetValue(ExtraProperty, want);
        sv.Padding = new Thickness(b.Left, b.Top, b.Right + want, b.Bottom);
    }

    /// <summary>How far the content would reach under the vertical bar without the gutter, plus the gap; 0 if not at all.</summary>
    static double Gutter(ScrollViewer sv, double extra)
    {
        if (sv.Template?.FindName("PART_VerticalScrollBar", sv) is not ScrollBar bar ||
            sv.Template.FindName("PART_ScrollContentPresenter", sv) is not FrameworkElement scp ||
            bar.ActualWidth <= 0 || scp.ActualWidth <= 0) return extra;   // not laid out yet: keep what we have
        try
        {
            double barLeft = bar.TransformToAncestor(sv).Transform(new Point(0, 0)).X;
            double contentRight = scp.TransformToAncestor(sv).Transform(new Point(scp.ActualWidth, 0)).X + extra;
            return Overlap(contentRight, barLeft);
        }
        catch (InvalidOperationException) { return extra; }
    }

    /// <summary>See Layout.ScrollGutter (pure, tested in Shelly.Core.Tests).</summary>
    public static double Overlap(double contentRight, double barLeft) => Shelly.Core.Layout.ScrollGutter(contentRight, barLeft);

    static T? Find<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            if (Find<T>(c) is { } r) return r;
        }
        return null;
    }
}

/// <summary>1.4.2: Windows 11 in-place ComboBox flyout placement and bounds validation (width, height, screen position).</summary>
public static class ComboPopup
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ComboPopup), new PropertyMetadata(false, OnEnabled));
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    const double ShadowLeft = 12, ShadowTop = 8, ShadowRight = 12, ShadowBottom = 16, ScreenMargin = 8;

    static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup || !(bool)e.NewValue) return;
        popup.Placement = PlacementMode.Custom;
        popup.CustomPopupPlacementCallback = (popupSize, _, _) => Place(popup, popupSize);
        popup.Opened += (_, _) => Validate(popup);
    }

    static void Validate(Popup popup)
    {
        if (popup.TemplatedParent is not ComboBox combo || popup.Child is not FrameworkElement root) return;
        var work = SystemParameters.WorkArea;
        double minW = Math.Max(0, combo.ActualWidth);
        double maxW = Math.Max(minW, work.Width - 2 * ScreenMargin);
        double maxH = Math.Min(combo.MaxDropDownHeight, Math.Max(120, work.Height - 2 * ScreenMargin));
        if (combo.Template?.FindName("DropDownBorder", combo) is FrameworkElement bd)
        {
            bd.MinWidth = minW;
            bd.MaxWidth = maxW;
            bd.MaxHeight = maxH;
        }
        if (combo.Template?.FindName("ShadowBorder", combo) is FrameworkElement sb)
        {
            sb.MinWidth = minW;
            sb.MaxWidth = maxW;
            sb.MaxHeight = maxH;
        }
        root.UpdateLayout();
        double h = popup.HorizontalOffset;
        popup.HorizontalOffset = h + 0.01;
        popup.HorizontalOffset = h;
    }

    static CustomPopupPlacement[] Place(Popup popup, Size popupSize)
    {
        var combo = popup.TemplatedParent as ComboBox ?? popup.PlacementTarget as ComboBox;
        double scaleX = 1.0, scaleY = 1.0;
        if (combo != null)
        {
            var dpi = VisualTreeHelper.GetDpi(combo);
            if (dpi.DpiScaleX > 0) scaleX = dpi.DpiScaleX;
            if (dpi.DpiScaleY > 0) scaleY = dpi.DpiScaleY;
        }
        double sLeft = ShadowLeft * scaleX, sTop = ShadowTop * scaleY;
        double sRight = ShadowRight * scaleX, sBottom = ShadowBottom * scaleY;
        double marginX = ScreenMargin * scaleX, marginY = ScreenMargin * scaleY;

        double x = -sLeft;
        double y = -sTop;

        if (combo != null && combo.IsVisible && PresentationSource.FromVisual(combo) != null)
        {
            try
            {
                if (combo.SelectedIndex > 0 && combo.Template?.FindName("DropDownBorder", combo) is FrameworkElement bd &&
                    combo.ItemContainerGenerator.ContainerFromIndex(combo.SelectedIndex) is FrameworkElement item &&
                    PresentationSource.FromVisual(item) != null && PresentationSource.FromVisual(bd) != null)
                {
                    double itemY = item.TranslatePoint(new Point(0, 0), bd).Y * scaleY;
                    if (itemY > 0 && itemY < popupSize.Height - sTop - sBottom)
                        y = -sTop - itemY + 4 * scaleY;
                }

                Point screenOrigin = combo.PointToScreen(new Point(0, 0));
                var work = SystemParameters.WorkArea;
                double waLeft = work.Left * scaleX, waTop = work.Top * scaleY;
                double waRight = work.Right * scaleX, waBottom = work.Bottom * scaleY;

                double visW = Math.Max(0, popupSize.Width - sLeft - sRight);
                double visH = Math.Max(0, popupSize.Height - sTop - sBottom);

                double visLeft = screenOrigin.X + x + sLeft;
                if (visLeft + visW > waRight - marginX) x -= (visLeft + visW) - (waRight - marginX);
                visLeft = screenOrigin.X + x + sLeft;
                if (visLeft < waLeft + marginX) x += (waLeft + marginX) - visLeft;

                double visTop = screenOrigin.Y + y + sTop;
                if (visTop + visH > waBottom - marginY) y -= (visTop + visH) - (waBottom - marginY);
                visTop = screenOrigin.Y + y + sTop;
                if (visTop < waTop + marginY) y += (waTop + marginY) - visTop;
            }
            catch (InvalidOperationException) { }
        }

        return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Vertical) };
    }
}
