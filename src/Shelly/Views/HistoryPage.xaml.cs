using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

public partial class HistoryPage : UserControl, IPage
{
    static AppState St => App.S;
    static readonly string[] Filters = { "all", "ok", "fail", "stopped", "window" };

    public HistoryPage()
    {
        InitializeComponent();
        FillFilter();
        Empty.Primary += () => { if (St.History.Entries.Count == 0) App.Win?.Navigate("scripts"); else { Search.Text = ""; Filter.SelectedIndex = 0; } };
        Filter.SelectionChanged += (_, _) =>
        {
            if (Filter.SelectedIndex < 0) return;
            St.Settings.HistoryFilter = Filters[Filter.SelectedIndex];
            St.SaveSettings();
            Rebuild();
        };
        Search.TextChanged += (_, _) => { SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Rebuild(); };
        List.SelectionChanged += (_, _) => { if (List.SelectedItem is RunEntry e) Output.Show(e); };
        List.PreviewKeyDown += (_, e) =>
        {
            if (List.SelectedItem is not RunEntry r) return;
            if (e.Key == Key.Delete) { Delete(r); e.Handled = true; }
            else if (e.Key == Key.Enter) { Rerun(r); e.Handled = true; }
        };
        List.MouseDoubleClick += (_, e) => { if (EntryAt(e.OriginalSource) is RunEntry r) Rerun(r); };
        List.ContextMenuOpening += (_, e) =>
        {
            if (EntryAt(e.OriginalSource) is not RunEntry r) { e.Handled = true; return; }
            List.ContextMenu = Menu(r);
        };
        ClearBtn.Click += (_, _) => Clear();
        App.HistoryUpdated += Rebuild;
        Loc.LanguageChanged += () => { FillFilter(); Rebuild(); };
        Rebuild();
    }

    public void Activated()
    {
        Rebuild();
        if (List.SelectedItem == null && List.Items.Count > 0) List.SelectedIndex = 0;
    }

    void FillFilter()
    {
        int i = Array.IndexOf(Filters, St.Settings.HistoryFilter);
        Filter.ItemsSource = new[] { Loc.T("histFilterAll"), Loc.T("histFilterOk"), Loc.T("histFilterFail"), Loc.T("histFilterStopped"), Loc.T("histFilterWindow") };
        Filter.SelectedIndex = i < 0 ? 0 : i;
    }

    static RunEntry? EntryAt(object src)
    {
        for (var x = src as DependencyObject; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is ListBoxItem li) return li.DataContext as RunEntry;
        return null;
    }

    void Rebuild()
    {
        var sel = List.SelectedItem as RunEntry;
        string q = Search.Text.Trim();
        var list = St.History.Entries.Where(e => HistoryStore.Matches(e, St.Settings.HistoryFilter, null, q.Length > 0 ? q : null)).ToList();
        foreach (var e in list) e.Refresh();
        List.ItemsSource = list;
        if (sel != null && list.Contains(sel)) List.SelectedItem = sel;
        if (St.History.Entries.Count == 0) Empty.Set(Glyphs.History, "histEmptyTitle", "histEmptyText", "goScripts");
        else Empty.Set(Glyphs.Search, "histNoMatchTitle", "histNoMatchText", "clearSearch");
        Empty.Show(list.Count == 0);
        Output.Refresh();
    }

    public void Select(RunEntry e)
    {
        if (!HistoryStore.Matches(e, St.Settings.HistoryFilter, null, null) || Search.Text.Length > 0)
        {
            Search.Text = "";
            Filter.SelectedIndex = 0;
        }
        Rebuild();
        List.SelectedItem = e;
        List.ScrollIntoView(e);
        Output.Show(e);
    }

    static void Rerun(RunEntry e)
    {
        var f = St.Find(e.Rel);
        if (f == null) { App.Status(Loc.F("histScriptGone", e.Rel), true); return; }
        if (St.Runs.SessionOf(e.Id) != null) return;
        _ = St.Runs.RunAsync(f, new RunOptions { RawArgs = e.Args, Mode = e.Mode, Engine = e.Engine, Origin = "rerun" });
    }

    void Delete(RunEntry e)
    {
        if (!St.History.Remove(e)) { App.Status(Loc.T("histRunningKept"), true); return; }
        St.History.Save();
        Rebuild();
    }

    void Clear()
    {
        int n = St.History.Entries.Count(e => !e.IsRunning);
        if (n == 0) { App.Status(Loc.T("histNothing")); return; }
        if (!Dlg.Confirm(Loc.T("histClearTitle"), Loc.T("histClearTitle"), Loc.F("histClearText", n), Loc.T("histClearBtn"), true, Glyphs.Delete)) return;
        int done = St.History.Clear();
        St.History.Save();
        Output.Show(null);
        Rebuild();
        App.Status(Loc.F("histCleared", done));
    }

    ContextMenu Menu(RunEntry e)
    {
        var m = new ContextMenu();
        void Add(string text, string glyph, Action a, bool enabled = true) { var mi = new MenuItem { Header = text, Icon = ScriptActions.MenuIcon(glyph), IsEnabled = enabled }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
        var f = St.Find(e.Rel);
        Add(Loc.T("histRerun"), Glyphs.Rerun, () => Rerun(e), f != null && !e.IsRunning);
        Add(Loc.T("histOpenScript"), Glyphs.Edit, () => { if (f != null) App.OpenEditor(f.Path); }, f != null);
        Add(Loc.T("histSelect"), Glyphs.Script, () => App.Win?.SelectScript(e.Rel), f != null);
        m.Items.Add(new Separator());
        Add(Loc.T("histDelete"), Glyphs.Delete, () => Delete(e), !e.IsRunning);
        return m;
    }
}
