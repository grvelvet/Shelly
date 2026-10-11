using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Views;

public partial class MainWindow : Window
{
    public ScriptsPage Scripts { get; }
    public FavoritesPage Favorites { get; }
    public HistoryPage History { get; }
    SettingsPage? settings;
    CompatPage? compat;
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    bool trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.Attach(this, TitleBar.TallHeight);   // 48 px title row: the content layer starts below it, as in WinUI apps
        var st = App.S;
        Scripts = new ScriptsPage();
        Favorites = new FavoritesPage();
        History = new HistoryPage();
        Pages.Children.Add(Scripts);
        MinWidth = Layout.MinWindowWidth; MinHeight = Layout.MinWindowHeight;
        MenuBtn.Click += (_, _) => ToggleNav();
        Scrim.MouseDown += (_, _) => CloseOverlay();
        foreach (var rb in new[] { NavScripts, NavFavorites, NavHistory, NavCompat, NavSettings })
            rb.Checked += (_, _) => { CloseOverlay(); Dispatcher.BeginInvoke(() => MovePill(true), DispatcherPriority.Loaded); };
        Pane.SizeChanged += (_, e) => { if (e.HeightChanged) MovePill(false); };
        Loaded += (_, _) => { ApplyNav(false); MovePill(false); };
        ApplyNav(false);
        NavScripts.Checked += (_, _) => ShowPage(Scripts);
        NavFavorites.Checked += (_, _) => ShowPage(Favorites);
        NavHistory.Checked += (_, _) => ShowPage(History);
        NavSettings.Checked += (_, _) => ShowPage(settings ??= new SettingsPage());
        NavCompat.Checked += (_, _) => { ShowPage(compat ??= new CompatPage()); compat.Refresh(); };
        statusTimer.Tick += (_, _) => { statusTimer.Stop(); StatusText.Text = ""; };
        App.StatusChanged += (text, important) =>
        {
            StatusText.Text = text;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, important ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
            statusTimer.Stop();
            if (text.Length > 0) statusTimer.Start();
        };
        st.Runs.Changed += UpdateRunning;
        st.Runs.ShowRequested += e =>
        {
            // A new run (or a toast click) shows its output where the user is
            if (NavHistory.IsChecked == true) History.Select(e);
            else { if (NavScripts.IsChecked != true && NavFavorites.IsChecked != true) NavScripts.IsChecked = true; Scripts.ShowRun(e); }
        };
        // Resizing switches modes without animation (an animation per resize step would fight the drag)
        SizeChanged += (_, e) => { if (e.WidthChanged) ApplyNav(false); };
        PreviewKeyDown += OnKey;
        Closing += OnClosing;
        Loc.LanguageChanged += () => { UpdateRunning(); MenuBtn.ToolTip = Loc.T(paneTarget >= Layout.PaneOpen ? "navClose" : "navOpen"); };
        // Minimized: the elapsed-time tickers stop (hidden to the tray is covered by IsVisible on the pages)
        StateChanged += (_, _) => { Scripts.UpdateTimer(); Favorites.UpdateTimer(); };
        UpdateRunning();
    }

    // ---------- Navigation pane (WinUI NavigationView, Left / LeftCompact) ----------
    // Wide window: open (Layout.PaneOpen) or compact (Layout.PaneCompact) as the user chose; the menu button switches and saves it.
    // Narrow window (< 800): always compact; the menu button opens the pane over the content with a shadow, and an
    // outside click, Esc or picking a page closes it. The pane's own width animates (decelerate, 250 ms); the content
    // margin changes once (at the start of a close, at the end of an open), so the page is not relaid out per frame.

    bool overlayOpen;
    double paneTarget = -1;
    int paneVersion;

    double InlineWidth() => Layout.NavFor(ActualWidth > 0 ? ActualWidth : Width, App.S.Settings.NavCompact) == Layout.Nav.Expanded
        ? Layout.PaneOpen : Layout.PaneCompact;

    void ApplyNav(bool animate)
    {
        if (overlayOpen && Layout.NavOverlays(ActualWidth)) return;
        overlayOpen = false;
        SetPane(InlineWidth(), false, animate);
    }

    void ToggleNav()
    {
        if (Layout.NavOverlays(ActualWidth))
        {
            if (overlayOpen) CloseOverlay();
            else { overlayOpen = true; SetPane(Layout.PaneOpen, true, true); }
            return;
        }
        var st = App.S;
        st.Settings.NavCompact = !st.Settings.NavCompact;
        st.SaveSettings();
        SetPane(InlineWidth(), false, true);
    }

    void CloseOverlay()
    {
        if (!overlayOpen) return;
        overlayOpen = false;
        SetPane(InlineWidth(), false, true);
    }

    const int PaneMs = 250;
    readonly System.Windows.Media.TranslateTransform bodyShift = new();

    void SetPane(double w, bool overlay, bool animate)
    {
        Scrim.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        MenuBtn.ToolTip = Loc.T(w >= Layout.PaneOpen ? "navClose" : "navOpen");
        if (w == paneTarget && !animate) return;
        bool same = w == paneTarget;
        paneTarget = w;
        int ver = ++paneVersion;
        if (Body.RenderTransform != bodyShift) Body.RenderTransform = bodyShift;

        // Start values are what is on screen now (an animation in flight included), read before anything is set
        double from = Pane.ActualWidth > 0 ? Pane.ActualWidth : w;
        double labelsFrom = NavPane.GetLabelOpacity(Nav);
        var plan = Layout.Plan(from, w, overlay, Body.Margin.Left);
        bool anim = animate && Motion.Enabled && IsLoaded && Math.Abs(from - w) > 0.5 && !same;

        // Final (base) values; the animations below run on top of them from explicit start values
        Pane.Width = w;
        NavPane.SetLabelOpacity(Nav, plan.Labels);
        if (!anim)
        {
            Pane.BeginAnimation(WidthProperty, null);
            Nav.BeginAnimation(NavPane.LabelOpacityProperty, null);
            bodyShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            bodyShift.X = 0;
            Body.Margin = new Thickness(plan.MarginEnd, 0, 0, 0);
            Surface(plan.SurfaceAfter);
            return;
        }

        Body.Margin = new Thickness(plan.MarginStart, 0, 0, 0);   // at most one page layout here...
        Surface(plan.SurfaceDuring);
        var wa = Motion.FromTo(plan.From, plan.To, PaneMs);
        wa.Completed += (_, _) =>
        {
            if (ver != paneVersion) return;
            Pane.BeginAnimation(WidthProperty, null);
            bodyShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            bodyShift.X = 0;
            Body.Margin = new Thickness(plan.MarginEnd, 0, 0, 0);   // ...and at most one here
            Surface(plan.SurfaceAfter);
        };
        Pane.BeginAnimation(WidthProperty, wa);
        // The content layer's edge (margin + shift) follows the pane's edge on the same curve: a render transform, no layout
        bodyShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, Motion.FromTo(plan.ShiftFrom, plan.ShiftTo, PaneMs));
        // Labels: fade in over the second half of an open, fade out at the start of a close
        Nav.BeginAnimation(NavPane.LabelOpacityProperty, plan.Labels > 0 ? Motion.FromTo(labelsFrom, 1, 150, 100) : Motion.FromTo(labelsFrom, 0, 83));
    }

    /// <summary>The pane's flyout surface and shadow: only while it lies over the content (overlay). Inline, the pane
    /// is plain Mica with no background or stroke of its own.</summary>
    void Surface(bool on)
    {
        PaneSurface.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PaneShadow.Opacity = on ? 0.26 : 0;
    }

    /// <summary>The selection indicator slides to the selected item (one element, a render transform: no layout).</summary>
    void MovePill(bool animate)
    {
        var rb = new[] { NavScripts, NavFavorites, NavHistory, NavCompat, NavSettings }.FirstOrDefault(r => r.IsChecked == true);
        if (rb == null || !rb.IsLoaded) { NavPill.Visibility = Visibility.Collapsed; return; }
        NavPill.Visibility = Visibility.Visible;
        double y = rb.TranslatePoint(new Point(0, (rb.ActualHeight - NavPill.Height) / 2), Pane).Y;
        if (animate && Motion.Enabled) PillMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, Motion.To(y, 250));
        else { PillMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null); PillMove.Y = y; }
    }

    void ShowPage(UIElement page)
    {
        if (!Pages.Children.Contains(page)) Pages.Children.Add(page);
        bool change = page.Visibility != Visibility.Visible || Pages.Children.Count > 1 && Pages.Children.Cast<UIElement>().Any(c => c != page && c.Visibility == Visibility.Visible);
        foreach (UIElement c in Pages.Children) c.Visibility = c == page ? Visibility.Visible : Visibility.Collapsed;
        if (change) Motion.Enter(page, 10, 250);   // Fluent page entrance: fade + rise, decelerate
        if (page is IPage p) p.Activated();
    }

    public void Navigate(string page)
    {
        switch (page)
        {
            case "scripts": NavScripts.IsChecked = true; break;
            case "favorites": NavFavorites.IsChecked = true; break;
            case "history": NavHistory.IsChecked = true; break;
            case "settings": NavSettings.IsChecked = true; break;
            case "compat": NavCompat.IsChecked = true; break;
        }
    }

    public void ShowHistory(RunEntry e)
    {
        NavHistory.IsChecked = true;
        History.Select(e);
    }

    public void SelectScript(string rel)
    {
        NavScripts.IsChecked = true;
        Scripts.Select(rel);
    }

    void UpdateRunning()
    {
        var runs = App.S.Runs.Active;
        RunningText.Text = runs.Count == 0 ? "" : runs.Count == 1 ? Loc.F("running", runs[0].Script.Name) : Loc.F("runningN", runs.Count);
        App.Tray?.UpdateTip();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        if (e.Key == Key.Escape && overlayOpen) { CloseOverlay(); e.Handled = true; return; }
        if (m == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1: Navigate("scripts"); e.Handled = true; break;
                case Key.D2: Navigate("favorites"); e.Handled = true; break;
                case Key.D3: Navigate("history"); e.Handled = true; break;
                case Key.OemComma: Navigate("settings"); e.Handled = true; break;
                case Key.M: MenuBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; break;
                case Key.K: Navigate("scripts"); Scripts.FocusSearch(); e.Handled = true; break;
            }
        }
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
        Focus();
    }

    /// <summary>Started hidden in the tray: the window handle exists (hotkey) but nothing is shown.</summary>
    public void InitHidden()
    {
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.Exiting) return;
        var st = App.S;
        if (st.Settings.CloseToTray && App.Tray?.Available == true)
        {
            e.Cancel = true;
            Hide();
            if (!st.Settings.TrayHintShown && !trayHintShown)
            {
                trayHintShown = true;
                st.Settings.TrayHintShown = true;
                st.SaveSettings();
                string hk = App.Hotkey?.Current ?? "";
                App.Toast(Loc.T("trayHintTitle"), hk.Length > 0 ? Loc.F("trayHintText", hk) : Loc.T("trayHintNoKey"), true, App.ShowMain);
            }
            return;
        }
        e.Cancel = true;
        Dispatcher.BeginInvoke(App.Quit);
    }
}

/// <summary>A page that wants to know when it is shown.</summary>
public interface IPage
{
    void Activated();
}
