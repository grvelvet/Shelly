using System.Windows;
using System.Windows.Input;

namespace Shelly.Views;

/// <summary>Keyboard-only focus rings. A template trigger on IsKeyboardFocused also fires on a mouse click (a clicked
/// list item or button takes keyboard focus), which drew the thick focus border on every clicked row. FocusRing.Show
/// is true only while the element has keyboard focus that came from the keyboard (Tab, arrows, or code right after a
/// key press), the same rule WPF uses for FocusVisualStyle; a mouse press clears it.</summary>
public static class FocusRing
{
    public static readonly DependencyProperty ShowProperty = DependencyProperty.RegisterAttached(
        "Show", typeof(bool), typeof(FocusRing), new PropertyMetadata(false));
    public static bool GetShow(DependencyObject d) => (bool)d.GetValue(ShowProperty);
    public static void SetShow(DependencyObject d, bool v) => d.SetValue(ShowProperty, v);

    static bool hooked;

    public static void Init()
    {
        if (hooked) return;
        hooked = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGot), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLost), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouse), true);
    }

    static void OnGot(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.NewFocus) || sender is not DependencyObject d) return;
        SetShow(d, InputManager.Current.MostRecentInputDevice is KeyboardDevice);
    }

    static void OnLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OldFocus) && sender is DependencyObject d && GetShow(d)) SetShow(d, false);
    }

    static void OnMouse(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is DependencyObject f && GetShow(f)) SetShow(f, false);
    }
}
