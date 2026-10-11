using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>Windows 11 title bar integrated with Mica, for the main window and every dialog.
///
/// How: WPF has no ExtendsContentIntoTitleBar, so the window gets a WindowChrome with GlassFrameThickness -1 and
/// UseAeroCaptionButtons (the same setup as Microsoft's WPF Gallery). The content then starts at the very top of the
/// window, the Mica backdrop shows through the transparent title row, and the minimize / maximize / close buttons stay
/// the real ones drawn by DWM: native hover and pressed states, red close, tooltips, Snap Layouts on maximize hover
/// (DwmDefWindowProc answers HTMAXBUTTON), dark glyphs via DWMWA_USE_IMMERSIVE_DARK_MODE. WindowChrome keeps the
/// caption behaviour: drag, double-click to maximize, right-click and Alt+Space system menu, resize borders, Aero Snap.
///
/// Where DWM can't show them through the window (no Mica before Windows 11 22H2, the backdrop switched off, High
/// Contrast: the window background is then opaque), Shelly draws its own caption buttons in the same 46 × 32 layout,
/// with Fluent hover / pressed fills and the red close hover.</summary>
public static class TitleBar
{
    public const double Height = 32;
    /// <summary>Title row of a window with a navigation pane (WinUI NavigationView apps such as Wintoys): 48 px, so the
    /// content layer starts 48 px from the top. Caption buttons stay 32 px at the top.</summary>
    public const double TallHeight = 48;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19, DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_MAINWINDOW = 2;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static readonly List<WeakReference<Window>> windows = new();
    static bool hooked;

    /// <summary>DWM draws the caption buttons through the transparent (Mica) window: Windows 11 22H2+, backdrop on, no High Contrast.</summary>
    public static bool NativeCaption =>
        Environment.OSVersion.Version.Build >= 22621 && !SystemParameters.HighContrast &&
        !(AppContext.TryGetSwitch("Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop", out bool off) && off);

    static bool Win11 => Environment.OSVersion.Version.Build >= 22000;

    sealed class Parts
    {
        public required Grid Root;
        public required Grid Bar;
        public required TextBlock Title;
        public required StackPanel Buttons;
        public Button? Max;
        public bool? Native;
        public double Height = TitleBar.Height;
    }
    static readonly DependencyProperty PartsProperty = DependencyProperty.RegisterAttached("TitleBarParts", typeof(Parts), typeof(TitleBar));

    /// <summary>Moves the window's content under a 32 px title row (icon + title) and extends it into the caption.</summary>
    public static void Attach(Window w) => Attach(w, Height);

    public static void Attach(Window w, double height)
    {
        var content = w.Content as UIElement;
        w.Content = null;

        var bar = new Grid { Height = height, Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // App icon: a click opens the system menu, as on a standard caption
        var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Source = IconOf(w) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        WindowChrome.SetIsHitTestVisibleInChrome(icon, true);
        icon.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) { w.Close(); return; }
            var p = icon.PointToScreen(new Point(0, icon.ActualHeight + 8));
            var src = PresentationSource.FromVisual(w);
            if (src?.CompositionTarget != null) p = src.CompositionTarget.TransformFromDevice.Transform(p);
            SystemCommands.ShowSystemMenu(w, p);
        };
        bar.Children.Add(icon);

        // Title: caption type ramp (12 px), Segoe UI Variable at its small optical size
        var title = new TextBlock
        {
            FontSize = 12, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = new FontFamily("Segoe UI Variable Small, Segoe UI Variable Text, Segoe UI"),
        };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = w });
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(bar);
        if (content != null) { Grid.SetRow(content, 1); root.Children.Add(content); }
        w.Content = root;

        var parts = new Parts { Root = root, Bar = bar, Title = title, Buttons = buttons, Height = height };
        w.SetValue(PartsProperty, parts);
        ApplyChrome(w);

        w.Activated += (_, _) => title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        w.Deactivated += (_, _) => title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        w.StateChanged += (_, _) => UpdateState(w);
        w.SourceInitialized += (_, _) => ApplyDwm(w);
        w.Loaded += (_, _) => ApplyDwm(w);   // after WPF's own Fluent backdrop setup
        lock (windows) { windows.RemoveAll(x => !x.TryGetTarget(out _)); windows.Add(new WeakReference<Window>(w)); }
        if (!hooked)
        {
            hooked = true;
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast)) Application.Current.Dispatcher.BeginInvoke(ApplyThemeToAll);
            };
            Loc.LanguageChanged += ApplyThemeToAll;
        }
    }

    static ImageSource? IconOf(Window w)
    {
        if (w.Icon != null) return w.Icon;
        try { return BitmapFrame.Create(new Uri("pack://application:,,,/Shelly;component/Assets/shelly.ico")); } catch { return null; }
    }

    static void ApplyChrome(Window w)
    {
        if (w.GetValue(PartsProperty) is not Parts parts) return;
        bool native = NativeCaption;
        parts.Native = native;
        bool resizable = w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        var resize = resizable ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            // The caption hit area starts above the top resize border, so the two together make the 32 px row
            CaptionHeight = Math.Max(0, parts.Height - resize.Top),
            ResizeBorderThickness = resize,
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = native,
            // Windows 11: keep the system frame on the sides and bottom (resize, shadow, rounded corners), like WPF Gallery
            NonClientFrameEdges = Win11 && resizable && !SystemParameters.HighContrast
                ? NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom : NonClientFrameEdges.None,
        });

        // Without the backdrop the window must be opaque, or the bare DWM frame (and its buttons) would show through
        if (native) w.ClearValue(Control.BackgroundProperty);
        else w.SetResourceReference(Control.BackgroundProperty, "WindowBackground");

        parts.Buttons.Children.Clear();
        parts.Max = null;
        if (!native)
        {
            if (w.ResizeMode != ResizeMode.NoResize)
            {
                parts.Buttons.Children.Add(CaptionButton("\uE921", "capMin", false, () => SystemCommands.MinimizeWindow(w)));
                if (resizable)
                    parts.Buttons.Children.Add(parts.Max = CaptionButton("\uE922", "capMax", false,
                        () => { if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w); else SystemCommands.MaximizeWindow(w); }));
            }
            parts.Buttons.Children.Add(CaptionButton("\uE8BB", "capClose", true, () => SystemCommands.CloseWindow(w)));
        }
        else if (Win11 && resizable)
        {
            // Keep our own content clear of the DWM buttons (3 × 46 px) so they stay clickable
            parts.Buttons.Children.Add(new Border { Width = 138 });
        }
        else parts.Buttons.Children.Add(new Border { Width = 46 });
        UpdateState(w);
    }

    static Button CaptionButton(string glyph, string tipKey, bool close, Action click)
    {
        var b = new Button { Content = glyph, ToolTip = Loc.T(tipKey), Tag = tipKey, IsTabStop = false };
        b.SetResourceReference(FrameworkElement.StyleProperty, close ? "CaptionCloseButton" : "CaptionButton");
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += (_, _) => click();
        return b;
    }

    static void UpdateState(Window w)
    {
        if (w.GetValue(PartsProperty) is not Parts parts) return;
        bool max = w.WindowState == WindowState.Maximized;
        // Maximized windows hang over the screen edge by the frame width; pull the content back in
        parts.Root.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        if (parts.Max != null)
        {
            parts.Max.Content = max ? "\uE923" : "\uE922";
            parts.Max.ToolTip = Loc.T(max ? "capRestore" : "capMax");
        }
    }

    /// <summary>Dark caption glyphs in the dark theme and the Mica backdrop (the attributes WPF's Fluent theme also sets).</summary>
    static void ApplyDwm(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = ThemeManager.IsDark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
            if (NativeCaption)
            {
                int mica = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>After a theme, language or High Contrast change: DWM attributes, caption buttons, tooltips.</summary>
    public static void ApplyThemeToAll()
    {
        List<Window> list;
        lock (windows) list = windows.Select(x => x.TryGetTarget(out var w) ? w : null).OfType<Window>().ToList();
        foreach (var w in list)
        {
            if (w.GetValue(PartsProperty) is not Parts parts) continue;
            if (parts.Native != NativeCaption) ApplyChrome(w);
            else
            {
                foreach (var b in parts.Buttons.Children.OfType<Button>()) if (b.Tag is string k) b.ToolTip = Loc.T(k);
                UpdateState(w);
            }
            ApplyDwm(w);
        }
    }
}
