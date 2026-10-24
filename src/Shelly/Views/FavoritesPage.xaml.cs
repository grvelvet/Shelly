using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Views;

public partial class FavoritesPage : UserControl, IPage
{
    readonly List<ScriptRow> rows = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    static AppState St => App.S;

    public FavoritesPage()
    {
        ApplySizeResources();   // before the tiles are created
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateTimer();
        St.ScriptsChanged += Rebuild;
        St.MetaChanged += () => { foreach (var r in rows) r.Update(St, r.File); };
        St.Runs.Changed += () =>
        {
            UpdateTimer();
            foreach (var r in rows) r.UpdateRunning(St);
            // A finished run changes "last run" and "run count": keep those orders current while the page is shown
            if (IsVisible && St.Settings.FavoritesSort is "lastrun" or "runcount")
                Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Background);
        };
        timer.Tick += (_, _) => { foreach (var r in rows) if (r.IsRunning) r.UpdateRunning(St); };
        Empty.Set(Glyphs.Star, "favEmptyTitle", "favEmptyText", "goScripts");
        Empty.Primary += () => App.Win?.Navigate("scripts");
        Tiles.MouseDoubleClick += (_, e) => { if (RowAt(e.OriginalSource) is ScriptRow r) ScriptActions.Run(r.File); };
        Tiles.ContextMenuOpening += (_, e) =>
        {
            if (RowAt(e.OriginalSource) is ScriptRow r) Tiles.ContextMenu = TileMenu(r);
            else e.Handled = true;
        };
        SortBtn.Click += (_, _) =>
        {
            var m = new ContextMenu { PlacementTarget = SortBtn, Placement = PlacementMode.Bottom };
            foreach (var (key, text) in SortKeys)
            {
                var mi = new MenuItem { Header = Loc.T(text), IsCheckable = true, IsChecked = St.Settings.FavoritesSort == key };
                string k = key;
                mi.Click += (_, _) => SetSort(k);
                m.Items.Add(mi);
            }
            m.IsOpen = true;
        };
        SizeBtn.Click += (_, _) =>
        {
            var m = new ContextMenu { PlacementTarget = SizeBtn, Placement = PlacementMode.Bottom };
            foreach (var t in Layout.FavoriteTiles)
            {
                var mi = new MenuItem { Header = Loc.T(SizeText(t.Key)), IsCheckable = true, IsChecked = Layout.FavoriteTileFor(St.Settings.FavoritesSize).Key == t.Key };
                string k = t.Key;
                mi.Click += (_, _) => SetSize(k);
                m.Items.Add(mi);
            }
            m.IsOpen = true;
        };
        // Ctrl + mouse wheel over the page: one size step per notch (up = larger), like Explorer's icon sizes
        PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0) return;
            e.Handled = true;
            SetSize(Layout.StepFavoriteTile(St.Settings.FavoritesSize, e.Delta));
        };
        Loc.LanguageChanged += UpdateSortButton;
        Loc.LanguageChanged += UpdateSizeButton;
        UpdateSizeButton();
        Rebuild();
    }

    public void Activated() => Rebuild();

    static ScriptRow? RowAt(object src)
    {
        for (var x = src as DependencyObject; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is FrameworkElement fe && fe.DataContext is ScriptRow r) return r;
        return null;
    }

    /// <summary>Favorites sort keys (Settings.FavoritesSortChoices) and their menu texts.</summary>
    static readonly (string Key, string Text)[] SortKeys =
    {
        ("manual", "favSortManual"), ("name", "favSortName"), ("lastrun", "favSortLastRun"), ("runcount", "favSortRunCount"), ("type", "favSortType"),
    };

    void SetSort(string key)
    {
        if (St.Settings.FavoritesSort == key) return;
        St.Settings.FavoritesSort = key;
        St.SaveSettings();
        Rebuild();
    }

    static string SizeText(string key) => key switch { "small" => "favSizeSmall", "large" => "favSizeLarge", _ => "favSizeMedium" };

    void SetSize(string key)
    {
        key = Layout.FavoriteTileFor(key).Key;
        if (St.Settings.FavoritesSize == key) return;
        St.Settings.FavoritesSize = key;
        St.SaveSettings();
        ApplySizeResources();
        UpdateSizeButton();
    }

    /// <summary>The tile template takes its sizes from these resources, so a new size applies to the shown tiles at once.</summary>
    void ApplySizeResources()
    {
        var t = Layout.FavoriteTileFor(St.Settings.FavoritesSize);
        Resources["FavTileWidth"] = t.Width;
        Resources["FavTileHeight"] = t.Height;
        Resources["FavTilePadding"] = new Thickness(t.Padding.Left, t.Padding.Top, t.Padding.Right, t.Padding.Bottom);
        Resources["FavTileIcon"] = t.Icon;
        Resources["FavTileIconMargin"] = new Thickness(0, 0, t.IconGap, 0);
        Resources["FavTileGlyph"] = t.Glyph;
        Resources["FavTileImage"] = t.Image;
        Resources["FavTileName"] = t.NameSize;
        Resources["FavTileDesc"] = t.DescSize;
        Resources["FavTileDescMargin"] = new Thickness(0, t.DescGap, 0, 0);
    }

    void UpdateSizeButton()
    {
        string tip = Loc.F("favSizeTip", Loc.T(SizeText(Layout.FavoriteTileFor(St.Settings.FavoritesSize).Key)));
        SizeBtn.ToolTip = tip + "\n" + Loc.T("favSizeWheel");
        System.Windows.Automation.AutomationProperties.SetName(SizeBtn, tip);
    }

    void UpdateSortButton()
    {
        string key = St.Settings.FavoritesSort;
        string text = Loc.T(SortKeys.FirstOrDefault(k => k.Key == key).Text ?? "favSortManual");
        SortBtn.ToolTip = Loc.F("sortTip", text);
        System.Windows.Automation.AutomationProperties.SetName(SortBtn, Loc.F("sortTip", text));
        // Accent when the order is not the default one, like the sort button of the script list
        if (key != "manual") SortBtn.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        else SortBtn.ClearValue(ForegroundProperty);
    }

    /// <summary>The script menu of a tile; in the manual order also "Move earlier" / "Move later".</summary>
    ContextMenu TileMenu(ScriptRow r)
    {
        var m = ScriptActions.ScriptMenu(r.File);
        if (St.Settings.FavoritesSort != "manual" || rows.Count < 2) return m;
        int i = rows.IndexOf(r);
        m.Items.Add(new Separator());
        void Add(string text, string glyph, int delta, bool enabled)
        {
            var mi = new MenuItem { Header = Loc.T(text), Icon = ScriptActions.MenuIcon(glyph), IsEnabled = enabled };
            mi.Click += (_, _) => { if (St.Settings.MoveFavorite(r.File.Rel, delta)) { St.SaveSettings(); Rebuild(); } };
            m.Items.Add(mi);
        }
        Add("favMoveEarlier", "\uE74A", -1, i > 0);
        Add("favMoveLater", "\uE74B", +1, i >= 0 && i < rows.Count - 1);
        return m;
    }

    void Rebuild()
    {
        UpdateSortButton();
        rows.Clear();
        var files = new List<ScriptFile>();
        foreach (var rel in St.Settings.Favorites)
            if (St.Find(rel) is { } sf) files.Add(sf);
        Dictionary<string, int>? counts = null;
        var ordered = FavoriteOrder.Sort(files, St.Settings.FavoritesSort, f =>
        {
            counts ??= St.History.Entries.GroupBy(e => e.Rel, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            return new ScriptFacts(St.LastRunOf(f.Rel)?.Time, counts.TryGetValue(f.Rel, out var n) ? n : 0, false, false, false, true);
        });
        foreach (var f in ordered)
        {
            var r = new ScriptRow(f);
            r.Update(St, f);
            rows.Add(r);
        }
        Tiles.ItemsSource = null;
        Tiles.ItemsSource = rows;
        Empty.Show(rows.Count == 0);
    }

    void TileRun(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ScriptRow r) ScriptActions.Run(r.File); }
    void TileSettings(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ScriptRow r) ScriptActions.RunSettings(r.File); }
    void TileStop(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ScriptRow r) ScriptActions.Stop(r.File); }

    /// <summary>The elapsed-time ticker runs only while a script runs and the page can be seen (not minimized, not in the tray).</summary>
    public void UpdateTimer()
    {
        bool on = St.Runs.Active.Count > 0 && IsVisible && App.Win?.WindowState != WindowState.Minimized;
        if (on && !timer.IsEnabled) timer.Start();
        else if (!on) timer.Stop();
    }
}
