using System.Windows;
using System.Windows.Controls;
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
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateTimer();
        St.ScriptsChanged += Rebuild;
        St.MetaChanged += () => { foreach (var r in rows) r.Update(St, r.File); };
        St.Runs.Changed += () => { UpdateTimer(); foreach (var r in rows) r.UpdateRunning(St); };
        timer.Tick += (_, _) => { foreach (var r in rows) if (r.IsRunning) r.UpdateRunning(St); };
        Empty.Set(Glyphs.Star, "favEmptyTitle", "favEmptyText", "goScripts");
        Empty.Primary += () => App.Win?.Navigate("scripts");
        Tiles.MouseDoubleClick += (_, e) => { if (RowAt(e.OriginalSource) is ScriptRow r) ScriptActions.Run(r.File); };
        Tiles.ContextMenuOpening += (_, e) =>
        {
            if (RowAt(e.OriginalSource) is ScriptRow r) Tiles.ContextMenu = ScriptActions.ScriptMenu(r.File);
            else e.Handled = true;
        };
        Rebuild();
    }

    public void Activated() => Rebuild();

    static ScriptRow? RowAt(object src)
    {
        for (var x = src as DependencyObject; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is FrameworkElement fe && fe.DataContext is ScriptRow r) return r;
        return null;
    }

    void Rebuild()
    {
        rows.Clear();
        foreach (var rel in St.Settings.Favorites)
        {
            var f = St.Find(rel);
            if (f == null) continue;
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
