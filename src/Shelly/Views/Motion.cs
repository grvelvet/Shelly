using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Shelly.Views;

/// <summary>Fluent motion, kept subtle: page entrance (fade + 10 px rise, 250 ms decelerate), the output panel
/// (fade + rise, 167 ms), menus, tooltips and drop-downs fade in, hover fills fade in 83 ms. Everything is off when
/// Windows "Animation effects" is off (SPI_GETCLIENTAREAANIMATION = SystemParameters.ClientAreaAnimation).</summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    static bool hooked;

    /// <summary>The Fluent "decelerate" curve (cubic-bezier 0.1, 0.9, 0.2, 1): fast start, gentle stop.</summary>
    static readonly KeySpline Decelerate = Frozen(new KeySpline(0.1, 0.9, 0.2, 1.0));
    static KeySpline Frozen(KeySpline k) { k.Freeze(); return k; }

    /// <summary>Popup animations of menus, tooltips and combo box drop-downs; called with every theme apply.</summary>
    public static void ApplyResources()
    {
        var app = Application.Current;
        var r = app.Resources;
        var anim = Enabled ? PopupAnimation.Fade : PopupAnimation.None;
        r[SystemParameters.MenuPopupAnimationKey] = anim;      // ContextMenu takes its popup animation from this key
        r[SystemParameters.ToolTipPopupAnimationKey] = anim;   // ToolTip likewise
        r[SystemParameters.ComboBoxPopupAnimationKey] = anim;
        // Styles.xaml ComboBox template binds PART_Popup.PopupAnimation to ComboBoxPopupAnimationKey directly
        r.Remove(typeof(ComboBox));
        if (!hooked)
        {
            hooked = true;
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) app.Dispatcher.BeginInvoke(ApplyResources);
            };
        }
    }

    /// <summary>From the current (possibly animated) value to <paramref name="to"/> on the decelerate curve.</summary>
    public static DoubleAnimationUsingKeyFrames To(double to, int ms, int delayMs = 0)
    {
        var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), Decelerate));
        return a;
    }

    /// <summary>From an explicit start value: a From-less animation started right after the property's local value was
    /// set starts at the new value and does nothing. <paramref name="holdMs"/> keeps the start value first (a delay
    /// that, unlike BeginTime, doesn't show the final value during the wait).</summary>
    public static DoubleAnimationUsingKeyFrames FromTo(double from, double to, int ms, int holdMs = 0)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (holdMs > 0) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs))));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdMs + ms)), Decelerate));
        return a;
    }

    /// <summary>Entrance: fade in and rise by <paramref name="rise"/> px. Without animations the element just shows.</summary>
    public static void Enter(UIElement e, double rise = 10, int ms = 250)
    {
        var tt = e.RenderTransform as TranslateTransform;
        if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); e.RenderTransform = tt; }
        if (!Enabled)
        {
            e.BeginAnimation(UIElement.OpacityProperty, null);
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            e.Opacity = 1; tt.Y = 0;
            return;
        }
        e.BeginAnimation(UIElement.OpacityProperty, Curve(0, 1, ms));
        if (rise != 0) tt.BeginAnimation(TranslateTransform.YProperty, Curve(rise, 0, ms));
    }

    static DoubleAnimationUsingKeyFrames Curve(double from, double to, int ms)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), Decelerate));
        a.Freeze();
        return a;
    }

    // Hover fills: a template layer whose opacity a trigger switches through these attached properties. The change
    // fades over 83 ms (WinUI ControlFasterAnimationDuration) instead of jumping.

    /// <summary>True shows the layer (opacity 0 → 1).</summary>
    public static readonly DependencyProperty ShowProperty = DependencyProperty.RegisterAttached(
        "Show", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) => Fade(d, (bool)e.NewValue ? 1 : 0)));
    public static bool GetShow(DependencyObject d) => (bool)d.GetValue(ShowProperty);
    public static void SetShow(DependencyObject d, bool v) => d.SetValue(ShowProperty, v);

    /// <summary>True hides the layer (opacity 1 → 0); used for the resting fill under a hover fill (cross-fade).</summary>
    public static readonly DependencyProperty HideProperty = DependencyProperty.RegisterAttached(
        "Hide", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) => Fade(d, (bool)e.NewValue ? 0 : 1)));
    public static bool GetHide(DependencyObject d) => (bool)d.GetValue(HideProperty);
    public static void SetHide(DependencyObject d, bool v) => d.SetValue(HideProperty, v);

    static void Fade(DependencyObject d, double to)
    {
        if (d is not UIElement u) return;
        if (!Enabled || !u.IsVisible) { u.BeginAnimation(UIElement.OpacityProperty, null); u.Opacity = to; return; }
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(83));
        a.Freeze();
        u.BeginAnimation(UIElement.OpacityProperty, a);
    }
}
