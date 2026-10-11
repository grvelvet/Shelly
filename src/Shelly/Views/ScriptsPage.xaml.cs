using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

public partial class ScriptsPage : UserControl, IPage
{
    readonly RowCollection items = new();
    readonly Dictionary<string, ScriptRow> rows = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly ToggleButton outToggle = new() { Content = Glyphs.ChevronDown };
    string infoMode = "";
    bool updateDismissed;
    static AppState St => App.S;

    sealed record Cmd(string Glyph, string Text, Action Do);

    public ScriptsPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateTimer();
        List.ItemsSource = items;
        St.ScriptsChanged += Rebuild;
        St.MetaChanged += () => { if (filters.Count > 0) Rebuild(); else foreach (var r in rows.Values) r.Update(St, r.File); UpdateInfoBar(); };
        St.EnginesChanged += UpdateInfoBar;
        St.Runs.Changed += () => { UpdateTimer(); foreach (var r in rows.Values) r.UpdateRunning(St); };
        elapsedTimer.Tick += (_, _) => { foreach (var r in rows.Values) if (r.IsRunning) r.UpdateRunning(St); };
        Updates.Found += UpdateInfoBar;
        Loc.LanguageChanged += () => { Rebuild(); UpdateInfoBar(); outToggle.ToolTip = Loc.T("outputTip"); };

        RefreshBtn.Click += (_, _) => { St.RefreshScripts(); App.Status(Loc.F("statusIdle", St.Scripts.Count)); };
        NewScriptBtn.Click += (_, _) => ScriptActions.NewScript();
        NewFolderBtn.Click += (_, _) => ScriptActions.NewFolder();
        Empty.Primary += () =>
        {
            if (emptyMode == "filter") ClearFilters();
            else if (emptyMode == "search") { Omni.Text = ""; Omni.Focus(); }
            else ScriptActions.NewScript();
        };
        Empty.Secondary += () => ShellOps.OpenFolder(St.Paths.Scripts);
        FoldersBtn.Click += (_, _) => FoldersMenu().IsOpen = true;

        // Toolbar: sort, collapse / expand all, view, filter (or all four under "…" in a narrow window)
        SortBtn.Click += (_, _) => Open(SortBtn, FillSort);
        ViewBtn.Click += (_, _) => Open(ViewBtn, FillView);
        FilterBtn.Click += (_, _) => Open(FilterBtn, FillFilter);
        CollapseBtn.Click += (_, _) => CollapseAll(AnyExpanded());
        MoreBtn.Click += (_, _) => Open(MoreBtn, FillMore);
        FilterClearBtn.Click += (_, _) => ClearFilters();
        ApplyView();

        Omni.TextChanged += (_, _) => OnOmni();
        searchDebounce.Tick += (_, _) => { searchDebounce.Stop(); ApplySearch(); };
        Header.SizeChanged += (_, _) => FitHeader();
        SearchBtn.Click += (_, _) => OpenSearchOverlay();
        SearchClose.Click += (_, _) => CloseSearchOverlay(true);
        Omni.LostKeyboardFocus += (_, _) =>
        {
            if (searchOverlay && Omni.Text.Length == 0 && !OmniPopup.IsOpen && !SearchOverlay.IsKeyboardFocusWithin) CloseSearchOverlay(false);
        };
        List.SizeChanged += (_, e) => { if (e.WidthChanged) FitRows(); };
        FitRows();
        Omni.PreviewKeyDown += OmniKey;
        OmniList.MouseUp += (_, _) => RunCommand();

        List.SelectionChanged += (_, _) => OnSelection();
        List.MouseDoubleClick += (_, e) => { if (RowAt(e.OriginalSource) is ScriptRow r) ScriptActions.Run(r.File); };
        List.PreviewMouseLeftButtonUp += (_, e) => { if (RowAt(e.OriginalSource) is GroupRow g && !(e.OriginalSource is DependencyObject d && IsInButton(d))) Toggle(g); };
        List.PreviewKeyDown += ListKey;
        List.ContextMenuOpening += (_, e) =>
        {
            var row = RowAt(e.OriginalSource);
            if (row is ScriptRow r) List.ContextMenu = ScriptActions.ScriptMenu(r.File);
            else if (row is GroupRow g && g.Key != "*fav") List.ContextMenu = ScriptActions.FolderMenu(g.Key);
            else { List.ContextMenu = ScriptActions.FolderMenu(""); }
        };
        PreviewKeyDown += PageKey;

        InfoClose.Click += (_, _) => DismissInfo();
        InfoAction.Click += (_, _) => InfoDo();

        // Output panel: collapse chevron and remembered height
        outToggle.SetResourceReference(StyleProperty, "IconToggle");
        Ico.SetSize(outToggle, 11);
        outToggle.ToolTip = Loc.T("outputTip");
        outToggle.Click += (_, _) => { St.Settings.OutputCollapsed = !St.Settings.OutputCollapsed; ApplyOutput(); St.SaveSettings(); };
        Output.Lead.Children.Add(outToggle);
        Splitter.DragCompleted += (_, _) =>
        {
            if (St.Settings.OutputCollapsed) return;
            St.Settings.OutputHeight = ((int)Math.Max(120, OutputRow.ActualHeight)).ToString();
            St.SaveSettings();
        };
        Output.EntryChanged += ApplyOutput;
        ApplyOutput();
        Rebuild();
        UpdateInfoBar();
    }

    public void Activated() { }

    /// <summary>The output panel appears only when there is a run with output to show (an inline run, live or
    /// finished, of the selected script); before the first run, and for runs in a separate window, it is hidden.</summary>
    bool HasOutput => Output.Entry is RunEntry e && (e.Mode == "inline" || e.HasOutput);

    bool outputShown, outCollapsedShown;

    void ApplyOutput()
    {
        bool show = HasOutput;
        // The panel slides up and fades in when it appears (and when it is expanded again)
        bool expand = show && (!outputShown || (outCollapsedShown && !St.Settings.OutputCollapsed));
        outputShown = show;
        outCollapsedShown = St.Settings.OutputCollapsed;
        if (expand) Motion.Enter(Output, 16, 167);
        Output.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Splitter.Visibility = Output.Visibility;
        if (!show)
        {
            OutputRow.MinHeight = 0;
            OutputRow.Height = new GridLength(0);
            return;
        }
        bool c = St.Settings.OutputCollapsed;
        outToggle.Content = c ? Glyphs.ChevronUp : Glyphs.ChevronDown;
        outToggle.IsChecked = false;
        double h = double.TryParse(St.Settings.OutputHeight, out var v) ? Math.Clamp(v, 120, 1200) : 240;
        OutputRow.Height = c ? new GridLength(44) : new GridLength(h);
        OutputRow.MinHeight = c ? 44 : 120;
        Splitter.IsEnabled = !c;
    }

    public void FocusSearch()
    {
        if (searchCollapsed && !searchOverlay) { OpenSearchOverlay(); return; }
        Omni.Focus(); Omni.SelectAll();
    }

    // ---------- Narrow window: header search and row status slots ----------

    bool searchCollapsed, searchOverlay;

    /// <summary>Header layout (Layout.Header): the search box shrinks first (340 down to 200 px), then sort, collapse,
    /// view and filter move into the "…" menu, then the box shrinks to 160 px, and below that it becomes a button.</summary>
    void FitHeader()
    {
        double room = Header.ActualWidth - TitleText.ActualWidth - TitleText.Margin.Right - RightBtns.ActualWidth;
        var (overflow, w, collapsed) = Layout.Header(room);
        toolsOverflow = overflow;
        ViewTools.Visibility = overflow ? Visibility.Collapsed : Visibility.Visible;
        MoreTools.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        searchCollapsed = collapsed;
        if (searchOverlay)
        {
            if (!collapsed) CloseSearchOverlay(false);
            return;
        }
        SearchBtn.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (!collapsed) { SearchBox.Width = w; OmniPopup.Width = Math.Max(w, 280); }
    }

    bool toolsOverflow;

    /// <summary>Search over the header: the box (same control, so the query and the > commands keep working) moves
    /// into a full-width bar with a back button; Esc or the back button closes it.</summary>
    void OpenSearchOverlay()
    {
        if (searchOverlay) { Omni.Focus(); return; }
        searchOverlay = true;
        CmdBtns.Children.Remove(SearchBox);
        SearchBox.Width = double.NaN;
        SearchBox.Visibility = Visibility.Visible;
        SearchSlot.Children.Add(SearchBox);
        Header.Visibility = Visibility.Hidden;   // keeps its size, so FitHeader still sees the real room
        SearchOverlay.Visibility = Visibility.Visible;
        Motion.Enter(SearchOverlay, 0, 167);
        Dispatcher.BeginInvoke(() => { Omni.Focus(); Omni.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    void CloseSearchOverlay(bool clear)
    {
        if (!searchOverlay) return;
        searchOverlay = false;
        if (clear && Omni.Text.Length > 0) Omni.Text = "";
        OmniPopup.IsOpen = false;
        SearchSlot.Children.Remove(SearchBox);
        CmdBtns.Children.Add(SearchBox);
        SearchOverlay.Visibility = Visibility.Collapsed;
        Header.Visibility = Visibility.Visible;
        FitHeader();
        if (searchCollapsed && Omni.Text.Length > 0) SearchBtn.ToolTip = Omni.Text;   // the hidden query stays visible somewhere
        else SearchBtn.ToolTip = Loc.T("searchOpenTip");
    }

    /// <summary>Row status slots drop out as the list narrows (engine, then last run, then warnings). One resource
    /// change per breakpoint, not per row.</summary>
    void FitRows()
    {
        var (eng, last, danger) = Layout.RowSlots(List.ActualWidth > 0 ? List.ActualWidth : 1000);
        SetSlot("SlotEngineVis", eng);
        SetSlot("SlotLastVis", last);
        SetSlot("SlotDangerVis", danger);
    }

    void SetSlot(string key, bool on)
    {
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        if (List.Resources[key] is Visibility cur && cur == v) return;
        List.Resources[key] = v;
    }

    static bool IsInButton(DependencyObject d)
    {
        for (var x = d; x != null; x = x is Visual || x is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
        {
            if (x is ButtonBase) return true;
            if (x is ListBoxItem) return false;
        }
        return false;
    }

    static RowBase? RowAt(object source)
    {
        for (var x = source as DependencyObject; x != null; x = x is Visual || x is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is ListBoxItem li) return li.DataContext as RowBase;
        return null;
    }

    ScriptRow? Selected => List.SelectedItem as ScriptRow;

    ScriptRow RowFor(ScriptFile f, string key, int depth)
    {
        if (!rows.TryGetValue(key, out var r))
        {
            r = new ScriptRow(f);
            rows[key] = r;
        }
        r.Depth = depth;
        r.Update(St, f);
        return r;
    }

    // ---------- The list ----------

    public void Rebuild()
    {
        string? selKey = List.SelectedItem switch { ScriptRow r => r.Rel, GroupRow g => "#" + g.Key, _ => null };
        bool fromFav = List.SelectedItem is ScriptRow sr && items.IndexOf(sr) >= 0 && IsInFavGroup(sr);
        var newItems = new List<RowBase>();
        string q = Omni.Text.Trim();
        bool searching = q.Length > 0 && !q.StartsWith(">");
        var set = St.Settings;
        var facts = Facts();
        IReadOnlyList<ScriptFile> scripts = St.Scripts;
        if (filters.Count > 0) scripts = scripts.Where(f => ScriptFilters.Matches(facts(f), filters)).ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (searching)
        {
            foreach (var f in ScriptGrouping.Search(scripts, q, x => St.Meta(x).Desc)) { newItems.Add(RowFor(f, f.Rel, 0)); used.Add(f.Rel); }
        }
        else
        {
            var collapsed = new HashSet<string>(set.CollapsedGroups, StringComparer.OrdinalIgnoreCase);
            // "Favorites only": the favorites are the list itself, so no separate group on top
            IEnumerable<ScriptFile> favs = filters.Contains("fav") ? Array.Empty<ScriptFile>() : set.Favorites.Select(St.Find).Where(f => f != null).Select(f => f!)
                .Where(f => filters.Count == 0 || ScriptFilters.Matches(facts(f), filters));
            var folders = filters.Count > 0 ? ScriptGrouping.FoldersWith(scripts, St.Folders) : St.Folders;
            Func<IEnumerable<ScriptFile>, IEnumerable<ScriptFile>>? order = set.ListSort == "name" && !set.ListSortDesc ? null
                : l => ScriptOrder.Sort(l, set.ListSort, set.ListSortDesc, facts);
            foreach (var e in ScriptGrouping.Build(scripts, folders, favs, collapsed, Loc.T("grpFav"), Loc.T("grpGeneral"), order))
            {
                if (e.IsGroup) newItems.Add(new GroupRow { Key = e.Key, Title = e.Title, Count = e.Count, Depth = e.Depth, Collapsed = e.Collapsed });
                else { newItems.Add(RowFor(e.File!, e.Key, e.Depth)); used.Add(e.Key); }
            }
        }
        foreach (var k in rows.Keys.Where(k => !used.Contains(k)).ToList()) rows.Remove(k);
        items.ReplaceAll(newItems);   // one Reset instead of a change notification per row

        if (filters.Count > 0 && St.Scripts.Count > 0 && (scripts.Count == 0 || searching && items.Count == 0))
        {
            emptyMode = scripts.Count == 0 ? "filter" : "search";
            if (emptyMode == "filter") Empty.Set(Glyphs.Filter, "filterEmptyTitle", "filterEmptyText", "clearFilters", "");
            else Empty.Set(Glyphs.Search, "searchEmptyTitle", "searchEmptyText", "clearSearch", "", q);
        }
        else if (searching) { emptyMode = "search"; Empty.Set(Glyphs.Search, "searchEmptyTitle", "searchEmptyText", "clearSearch", "", q); }
        else { emptyMode = ""; Empty.Set(Glyphs.Script, "scriptsEmptyTitle", "scriptsEmptyText", "new", "openScriptsFolder"); }
        Empty.Show(items.Count == 0);
        OmniHint.Visibility = Omni.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTools(searching);

        if (selKey != null)
        {
            RowBase? again;
            if (selKey.StartsWith("#")) again = items.OfType<GroupRow>().FirstOrDefault(g => "#" + g.Key == selKey);
            else
            {
                var cands = items.OfType<ScriptRow>().Where(r => string.Equals(r.Rel, selKey, StringComparison.OrdinalIgnoreCase)).ToList();
                again = cands.Count == 0 ? null : fromFav ? cands[0] : cands[^1];
            }
            if (again != null) List.SelectedItem = again;
        }
    }

    string emptyMode = "";

    /// <summary>What the sort and the filters know about a script; computed on demand, once per rebuild.</summary>
    Func<ScriptFile, ScriptFacts> Facts()
    {
        var cache = new Dictionary<ScriptFile, ScriptFacts>();
        Dictionary<string, int>? counts = null;
        Dictionary<string, string>? modes = null;
        return f =>
        {
            if (cache.TryGetValue(f, out var x)) return x;
            counts ??= St.History.Entries.GroupBy(e => e.Rel, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            if (modes == null)
            {
                modes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in St.Settings.RunPresets) if (RunPreset.Parse(l) is { } p) modes[p.Rel] = p.Mode;
            }
            var m = St.Meta(f);
            x = new ScriptFacts(St.LastRunOf(f.Rel)?.Time, counts.TryGetValue(f.Rel, out var n) ? n : 0, m.Engine.Engine == "5.1",
                Safety.Blocking(m.Danger).Count > 0 || m.Zone?.Blocks == true, modes.TryGetValue(f.Rel, out var mode) && mode == "admin", St.Settings.IsFavorite(f.Rel));
            cache[f] = x;
            return x;
        };
    }

    bool IsInFavGroup(ScriptRow r)
    {
        int i = items.IndexOf(r);
        for (int j = i - 1; j >= 0; j--) if (items[j] is GroupRow g) return g.Key == "*fav";
        return false;
    }

    void Toggle(GroupRow g)
    {
        string key = g.Key.Length == 0 ? "*root" : g.Key;
        var list = St.Settings.CollapsedGroups;
        if (list.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) list.RemoveAll(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
        else list.Add(key);
        St.SaveSettings();
        Rebuild();
    }

    public void Select(string rel)
    {
        if (Omni.Text.Length > 0) Omni.Text = "";
        if (filters.Count > 0 && St.Find(rel) is { } sf && !ScriptFilters.Matches(Facts()(sf), filters)) { filters.Clear(); Rebuild(); }
        var folder = ScriptCatalog.FolderOf(rel);
        // Expand the folders on the way
        bool changed = false;
        for (string f = folder; f.Length > 0; f = ScriptCatalog.FolderOf(f))
            changed |= St.Settings.CollapsedGroups.RemoveAll(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase)) > 0;
        if (folder.Length == 0) changed |= St.Settings.CollapsedGroups.RemoveAll(x => x == "*root") > 0;
        if (changed) { St.SaveSettings(); Rebuild(); }
        var row = items.OfType<ScriptRow>().LastOrDefault(r => string.Equals(r.Rel, rel, StringComparison.OrdinalIgnoreCase));
        if (row != null)
        {
            List.SelectedItem = row;
            List.ScrollIntoView(row);
            (List.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem)?.Focus();
        }
    }

    void OnSelection()
    {
        if (Selected is not ScriptRow r) return;
        var e = St.History.LastOf(r.Rel);
        if (e != null && Output.Entry?.Id != e.Id) Output.Show(e);
        else if (e == null && Output.Entry?.IsRunning != true) Output.Show(null);
    }

    /// <summary>Show a run in the output panel (opens the panel).</summary>
    public void ShowRun(RunEntry e)
    {
        if (St.Settings.OutputCollapsed) { St.Settings.OutputCollapsed = false; ApplyOutput(); St.SaveSettings(); }
        Output.Show(e);
    }

    // ---------- Row buttons ----------

    void RowRun(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ScriptRow r) { List.SelectedItem = r; ScriptActions.Run(r.File); } }
    void RowSettings(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ScriptRow r) { List.SelectedItem = r; ScriptActions.RunSettings(r.File); } }
    void RowStop(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ScriptRow r) ScriptActions.Stop(r.File); }

    // ---------- Keys ----------

    void ListKey(object sender, KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        if (List.SelectedItem is GroupRow g)
        {
            if (e.Key is Key.Enter or Key.Space) { Toggle(g); e.Handled = true; }
            else if (e.Key == Key.Left && !g.Collapsed) { Toggle(g); e.Handled = true; }
            else if (e.Key == Key.Right && g.Collapsed) { Toggle(g); e.Handled = true; }
            else if (e.Key == Key.F2 && g.IsFolder) { ScriptActions.RenameFolder(g.Key); e.Handled = true; }
            else if (e.Key == Key.Delete && g.IsFolder) { ScriptActions.DeleteFolder(g.Key); e.Handled = true; }
            return;
        }
        if (Selected is not ScriptRow r) return;
        var f = r.File;
        switch (e.Key)
        {
            case Key.Enter when m == ModifierKeys.Control: ScriptActions.RunSettings(f); e.Handled = true; break;
            case Key.Enter when m == ModifierKeys.None: ScriptActions.Run(f); e.Handled = true; break;
            case Key.F2: ScriptActions.Rename(f); e.Handled = true; break;
            case Key.Delete: ScriptActions.Delete(f); e.Handled = true; break;
            case Key.E when m == ModifierKeys.Control: ScriptActions.Edit(f); e.Handled = true; break;
            case Key.E when m == (ModifierKeys.Control | ModifierKeys.Shift): ShellOps.ExternalEditor(f.Path); e.Handled = true; break;
            case Key.D when m == ModifierKeys.Control: ScriptActions.ToggleFavorite(f); e.Handled = true; break;
            case Key.B when m == ModifierKeys.Control: ScriptActions.Build(f); e.Handled = true; break;
            case Key.I when m == ModifierKeys.Control: ScriptActions.RunSettings(f); e.Handled = true; break;
        }
    }

    void PageKey(object sender, KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        if (e.Key == Key.N && m == ModifierKeys.Control) { ScriptActions.NewScript(); e.Handled = true; }
        else if (e.Key == Key.N && m == (ModifierKeys.Control | ModifierKeys.Shift)) { ScriptActions.NewFolder(); e.Handled = true; }
        else if (e.Key == Key.F5 && m == ModifierKeys.None) { St.RefreshScripts(); e.Handled = true; }
        else if (e.Key == Key.OemOpenBrackets && m == (ModifierKeys.Control | ModifierKeys.Shift)) { CollapseAll(true); e.Handled = true; }
        else if (e.Key == Key.OemCloseBrackets && m == (ModifierKeys.Control | ModifierKeys.Shift)) { CollapseAll(false); e.Handled = true; }
        else if (e.Key == Key.F && m == ModifierKeys.Control && !Output.IsKeyboardFocusWithin) { FocusSearch(); e.Handled = true; }
    }

    // ---------- Omnibox: search, or > commands ----------

    List<Cmd> Commands() => new()
    {
        new(Glyphs.Add, Loc.T("new"), () => ScriptActions.NewScript()),
        new(Glyphs.NewFolder, Loc.T("cmdNewFolder"), () => ScriptActions.NewFolder()),
        new(Glyphs.OpenFolder, Loc.T("cmdOpenScripts"), () => ShellOps.OpenFolder(St.Paths.Scripts)),
        new(Glyphs.OpenFolder, Loc.T("openBuildFolder"), () => ShellOps.OpenFolder(St.Paths.Build)),
        new(Glyphs.Refresh, Loc.T("refresh"), () => St.RefreshScripts()),
        new(Glyphs.Globe, Loc.T("cmdLanguage"), () => SettingsPage.SetLanguage(Loc.Language == "ru" ? "en" : "ru")),
        new(Glyphs.Color, Loc.T("cmdTheme"), () => App.Win?.Navigate("settings")),
        new(Glyphs.Shield, St.Settings.SafetyWarnings ? Loc.T("safetyOff") : Loc.T("safetyOn"), () => SettingsPage.ToggleSafety()),
        new(Glyphs.Compat, Loc.T("cmdCompat"), () => App.Win?.Navigate("compat")),
        new(Glyphs.History, Loc.T("navHistory"), () => App.Win?.Navigate("history")),
        new(Glyphs.Settings, Loc.T("navSettings"), () => App.Win?.Navigate("settings")),
        new(Glyphs.Download, Loc.T("p7Install"), App.InstallPwsh),
    };

    void OnOmni()
    {
        OmniHint.Visibility = Omni.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string t = Omni.Text;
        if (t.StartsWith(">"))
        {
            string q = t.Substring(1).Trim();
            var list = Commands().Where(c => q.Length == 0 || c.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (list.Count == 0) list.Add(new Cmd(Glyphs.Info, Loc.T("omniNone"), () => { }));
            OmniList.ItemsSource = list;
            OmniList.SelectedIndex = 0;
            OmniPopup.PopupAnimation = Motion.Enabled ? System.Windows.Controls.Primitives.PopupAnimation.Fade : System.Windows.Controls.Primitives.PopupAnimation.None;
            OmniPopup.IsOpen = true;
            if (items.Count > 0 || Empty.Visibility == Visibility.Visible) { }
            return;
        }
        OmniPopup.IsOpen = false;
        // Large libraries: rebuild once typing pauses (120 ms) instead of on every key
        if (St.Scripts.Count > 300) { searchDebounce.Stop(); searchDebounce.Start(); return; }
        ApplySearch();
    }

    readonly System.Windows.Threading.DispatcherTimer searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };

    void ApplySearch()
    {
        string t = Omni.Text;
        Rebuild();
        if (t.Length > 0 && items.FirstOrDefault(i => i is ScriptRow) is ScriptRow first) List.SelectedItem = first;
    }

    void RunCommand()
    {
        if (OmniList.SelectedItem is not Cmd c) return;
        OmniPopup.IsOpen = false;
        Omni.Text = "";
        c.Do();
    }

    void OmniKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            bool overlay = searchOverlay;
            Omni.Text = ""; OmniPopup.IsOpen = false;
            if (overlay) CloseSearchOverlay(true);
            List.Focus(); e.Handled = true; return;
        }
        if (OmniPopup.IsOpen)
        {
            if (e.Key == Key.Down) { OmniList.SelectedIndex = Math.Min(OmniList.Items.Count - 1, OmniList.SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == Key.Up) { OmniList.SelectedIndex = Math.Max(0, OmniList.SelectedIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Enter) { RunCommand(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Down && items.Count > 0)
        {
            if (List.SelectedItem == null) List.SelectedIndex = 0;
            (List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Selected is ScriptRow r) { ScriptActions.Run(r.File); e.Handled = true; }
    }

    ContextMenu FoldersMenu()
    {
        var m = new ContextMenu { PlacementTarget = FoldersBtn, Placement = PlacementMode.Bottom };
        void Add(string text, string dir)
        {
            var mi = new MenuItem { Header = text, Icon = ScriptActions.MenuIcon(Glyphs.OpenFolder) };
            mi.Click += (_, _) => ShellOps.OpenFolder(dir);
            m.Items.Add(mi);
        }
        Add(Loc.T("openScriptsFolder"), St.Paths.Scripts);
        Add(Loc.T("openBuildFolder"), St.Paths.Build);
        Add(Loc.T("openShellyFolder"), St.Paths.Root);
        return m;
    }

    // ---------- Toolbar: sort, collapse / expand all, view, filter ----------

    /// <summary>Active filters (ScriptFilters.All); for this session only, not saved.</summary>
    readonly HashSet<string> filters = new(StringComparer.Ordinal);

    static readonly (string Key, string Text)[] SortKeys = { ("name", "sortName"), ("modified", "sortModified"), ("lastrun", "sortLastRun"), ("runcount", "sortRunCount") };

    static void Open(Button anchor, Action<ItemsControl> fill)
    {
        var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        fill(m);
        m.IsOpen = true;
    }

    static MenuItem Check(string text, bool on, Action a, bool staysOpen = false)
    {
        var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = on, StaysOpenOnClick = staysOpen };
        mi.Click += (_, _) => a();
        return mi;
    }

    static MenuItem Sub(string text, string glyph, Action<ItemsControl> fill)
    {
        var mi = new MenuItem { Header = text, Icon = ScriptActions.MenuIcon(glyph) };
        fill(mi);
        return mi;
    }

    void FillSort(ItemsControl m)
    {
        var s = St.Settings;
        foreach (var (key, text) in SortKeys)
            m.Items.Add(Check(Loc.T(text), s.ListSort == key, () => SetSort(key, s.ListSortDesc)));
        m.Items.Add(new Separator());
        m.Items.Add(Check(Loc.T("sortAsc"), !s.ListSortDesc, () => SetSort(s.ListSort, false)));
        m.Items.Add(Check(Loc.T("sortDesc"), s.ListSortDesc, () => SetSort(s.ListSort, true)));
    }

    void SetSort(string key, bool desc)
    {
        St.Settings.ListSort = key;
        St.Settings.ListSortDesc = desc;
        St.SaveSettings();
        Rebuild();
    }

    void FillView(ItemsControl m)
    {
        bool compact = St.Settings.ListView == "compact";
        m.Items.Add(Check(Loc.T("viewCards"), !compact, () => SetView("cards")));
        m.Items.Add(Check(Loc.T("viewCompact"), compact, () => SetView("compact")));
    }

    void SetView(string view)
    {
        if (St.Settings.ListView == view) { UpdateTools(); return; }
        St.Settings.ListView = view;
        St.SaveSettings();
        ApplyView();
        if (List.SelectedItem != null) Dispatcher.BeginInvoke(() => { if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem); }, DispatcherPriority.Loaded);
    }

    /// <summary>Cards or Compact list: swaps the container style and row template selectors (the same virtualizing list),
    /// which regenerates the realized rows.</summary>
    void ApplyView()
    {
        bool compact = St.Settings.ListView == "compact";
        List.ItemContainerStyleSelector = (StyleSelector)FindResource(compact ? "CompactRowStyles" : "RowStyles");
        List.ItemTemplateSelector = (DataTemplateSelector)FindResource(compact ? "CompactTemplates" : "CardTemplates");
        UpdateTools();
    }

    void FillFilter(ItemsControl m)
    {
        var clear = ScriptActions.Item(Loc.T("clearFilters"), Glyphs.Clear, ClearFilters);
        foreach (var k in ScriptFilters.All)
        {
            string key = k;
            m.Items.Add(Check(Loc.T("filter_" + key), filters.Contains(key), () =>
            {
                if (!filters.Remove(key)) filters.Add(key);
                clear.IsEnabled = filters.Count > 0;
                Rebuild();
            }, staysOpen: true));
        }
        m.Items.Add(new Separator());
        clear.IsEnabled = filters.Count > 0;
        m.Items.Add(clear);
    }

    void ClearFilters()
    {
        if (filters.Count == 0) return;
        filters.Clear();
        Rebuild();
    }

    void FillMore(ItemsControl m)
    {
        m.Items.Add(Sub(Loc.T("sortMenu"), Glyphs.Sort, FillSort));
        bool expanded = AnyExpanded();
        var c = ScriptActions.Item(Loc.T(expanded ? "collapseAll" : "expandAll"), expanded ? Glyphs.CollapseAll : Glyphs.ExpandAll,
            () => CollapseAll(expanded), expanded ? "Ctrl+Shift+[" : "Ctrl+Shift+]");
        c.IsEnabled = CollapseBtn.IsEnabled;
        m.Items.Add(c);
        m.Items.Add(Sub(Loc.T("viewMenu"), St.Settings.ListView == "compact" ? Glyphs.ViewList : Glyphs.ViewCards, FillView));
        m.Items.Add(Sub(filters.Count > 0 ? Loc.F("filtersChip", filters.Count) : Loc.T("filterMenu"), Glyphs.Filter, FillFilter));
    }

    /// <summary>Is any group on screen expanded? (Then the button collapses all; else it expands all.)</summary>
    bool AnyExpanded() => items.OfType<GroupRow>().Any(g => !g.Collapsed);

    /// <summary>Collapse (or expand) every group: favorites, root scripts and all folders. Kept in collapsedGroups.</summary>
    void CollapseAll(bool collapse)
    {
        if (!CollapseBtn.IsEnabled) return;
        var list = St.Settings.CollapsedGroups;
        list.Clear();
        if (collapse)
        {
            bool hasFav = St.Settings.Favorites.Any(r => St.Find(r) != null);
            list.AddRange(ScriptGrouping.GroupKeys(St.Scripts, St.Folders, hasFav));
        }
        St.SaveSettings();
        Rebuild();
    }

    /// <summary>Glyphs, tooltips and the filter badge / chip follow the current state.</summary>
    void UpdateTools(bool? searching = null)
    {
        var s = St.Settings;
        bool search = searching ?? (Omni.Text.Trim().Length > 0 && !Omni.Text.Trim().StartsWith(">"));
        string sortName = Loc.T(SortKeys.FirstOrDefault(k => k.Key == s.ListSort).Text ?? "sortName");
        Tip(SortBtn, Loc.F("sortTip", sortName + ", " + Loc.T(s.ListSortDesc ? "sortDesc" : "sortAsc").ToLowerInvariant()));
        // A non-default order tints the sort glyph (search results keep their own relevance order)
        if (s.ListSort != "name" || s.ListSortDesc) SortBtn.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        else SortBtn.ClearValue(ForegroundProperty);

        bool groups = !search && items.OfType<GroupRow>().Any();
        CollapseBtn.IsEnabled = groups;
        bool expanded = AnyExpanded();
        CollapseBtn.Content = expanded || !groups ? Glyphs.CollapseAll : Glyphs.ExpandAll;
        Tip(CollapseBtn, Loc.T(expanded || !groups ? "collapseAllTip" : "expandAllTip"));

        bool compact = s.ListView == "compact";
        ViewBtn.Content = compact ? Glyphs.ViewList : Glyphs.ViewCards;
        Tip(ViewBtn, Loc.F("viewTip", Loc.T(compact ? "viewCompact" : "viewCards")));

        bool on = filters.Count > 0;
        var names = ScriptFilters.All.Where(filters.Contains).Select(k => Loc.T("filter_" + k)).ToList();
        Tip(FilterBtn, on ? Loc.F("filterTipOn", string.Join(", ", names)) : Loc.T("filterTip"));
        FilterDot.Visibility = MoreDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        FilterChip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        FilterChipText.Text = Loc.F("filtersChip", filters.Count);
        FilterChip.ToolTip = on ? string.Join("\n", names) : null;
    }

    static void Tip(FrameworkElement e, string text)
    {
        e.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(e, text);
    }

    // ---------- Info bar ----------

    void UpdateInfoBar()
    {
        infoMode = "";
        InfoAction2.Visibility = Visibility.Collapsed;
        if (Updates.Latest != null && !updateDismissed)
        {
            infoMode = "update";
            InfoGlyph.Text = Glyphs.Download;
            InfoText.Text = Loc.F("updAvailable", Updates.Latest.Version, AppState.Version);
            InfoAction.Content = Loc.T("updDownload");
        }
        else if (!St.PwshOk && !St.Settings.PwshOfferDismissed && St.Scripts.Any(s => St.Meta(s).Engine.Missing))
        {
            infoMode = "pwsh";
            InfoGlyph.Text = Glyphs.Info;
            InfoText.Text = Loc.T("p7Offer");
            InfoAction.Content = Loc.T("p7OfferBtn");
        }
        else
        {
            var (w, key) = Compat.Warnings();
            if (w.Count > 0 && key != St.Settings.CompatDismissed)
            {
                infoMode = "compat";
                InfoGlyph.Text = Glyphs.Warning;
                InfoText.Text = Loc.F("compatSummary", string.Join(", ", w.Select(i => i.Title)));
                InfoAction.Content = Loc.T("details");
            }
        }
        InfoBar.Visibility = infoMode.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void InfoDo()
    {
        switch (infoMode)
        {
            case "update": Updates.OpenDownload(); break;
            case "pwsh": App.InstallPwsh(); break;
            case "compat": App.Win?.Navigate("compat"); break;
        }
    }

    void DismissInfo()
    {
        switch (infoMode)
        {
            case "update": updateDismissed = true; break;
            case "pwsh": St.Settings.PwshOfferDismissed = true; St.SaveSettings(); break;
            case "compat": St.Settings.CompatDismissed = Compat.Warnings().Key; St.SaveSettings(); break;
        }
        UpdateInfoBar();
    }

    /// <summary>The elapsed-time ticker runs only while a script runs and the page can be seen (not minimized, not in the tray).</summary>
    public void UpdateTimer()
    {
        bool on = St.Runs.Active.Count > 0 && IsVisible && App.Win?.WindowState != WindowState.Minimized;
        if (on && !elapsedTimer.IsEnabled) elapsedTimer.Start();
        else if (!on) elapsedTimer.Stop();
    }
}
