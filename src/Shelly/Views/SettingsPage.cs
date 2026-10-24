using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>Settings in Windows 11 style cards.</summary>
public sealed class SettingsPage : UserControl, IPage
{
    static AppState St => App.S;

    /// <summary>1.1.2: one minimum width for every drop-down of this page (and the History filter), so the boxes line up
    /// and do not change width with the selection. Sized for the longest item in English and Russian ("Use system setting",
    /// about 170 px with the Fluent padding and chevron); a longer text still widens the box instead of being cut.</summary>
    public const double ComboWidth = 148;

    readonly StackPanel body = new() { Margin = new Thickness(36, 24, 36, 36), MaxWidth = 1000 };
    readonly ScrollViewer scroll;
    bool _sourcesExpanded = false;
    bool _extFoldersExpanded = false;
    readonly StackPanel schedHost = new();
    bool schedHooked;
    string _shellView = "ps";   // Settings > Environment: whose settings are shown, "ps", "py" or "custom" (for this session)

    public SettingsPage()
    {
        scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll;
        Loc.LanguageChanged += Build;
        PwshInstall.WatchChanged += Build;
        St.EnginesChanged += Build;
        St.PythonChanged += Build;
        Build();
    }

    public void Activated()
    {
        if (showNext != null) { _shellView = showNext; showNext = null; }
        Build();
    }

    static string? showNext;
    /// <summary>The next time Settings opens, Environment shows Python (the "Python not found" and broken venv dialogs).</summary>
    public static void ShowPythonNext() => showNext = "py";
    /// <summary>The next time Settings opens, Environment shows the custom interpreters (a missing interpreter or program).</summary>
    public static void ShowCustomNext() => showNext = "custom";

    // ---------- Shared actions (also used by the omnibox) ----------

    public static void SetLanguage(string lang)
    {
        St.Settings.Language = lang;
        Loc.SetLanguage(lang);
        ThemeManager.UpdateTexts();
        St.NotifySettingsChanged();
        St.NotifyScriptsChanged();
        JumpLists.Update();
    }

    public static void ToggleSafety()
    {
        if (St.Settings.SafetyWarnings)
        {
            if (!Dlg.Confirm(Loc.T("safetyOffTitle"), Loc.T("safetyOffTitle"), Loc.T("safetyOffText"), Loc.T("safetyOffBtn"), true, Glyphs.Shield)) return;
            St.Settings.SafetyWarnings = false;
        }
        else St.Settings.SafetyWarnings = true;
        St.NotifySettingsChanged();
        App.Status(Loc.T(St.Settings.SafetyWarnings ? "safetyIsOn" : "safetyIsOff"));
    }

    // ---------- Building blocks ----------

    static TextBlock Glyph(string g, double size = 18)
    {
        var t = new TextBlock { Text = g, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    TextBlock Section(string title)
    {
        var t = new TextBlock { Text = title };
        t.SetResourceReference(StyleProperty, "SectionTitle");
        body.Children.Add(t);
        return t;
    }

    Border Card(string glyph, string title, string? desc, UIElement? right, UIElement? below = null, Panel? into = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var ic = Glyph(glyph);
        ic.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(ic);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(desc))
        {
            var d = new TextBlock { Text = desc };
            d.SetResourceReference(StyleProperty, "Caption");
            sp.Children.Add(d);
        }
        Grid.SetColumn(sp, 1);
        g.Children.Add(sp);
        if (right != null)
        {
            if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
        }
        if (below != null)
        {
            Grid.SetRow(below, 1);
            Grid.SetColumn(below, 1);
            Grid.SetColumnSpan(below, 2);
            if (below is FrameworkElement b) b.Margin = new Thickness(0, 12, 0, 0);
            g.Children.Add(below);
        }
        var card = new Border { Child = g };
        card.SetResourceReference(StyleProperty, "Card");
        (into ?? body).Children.Add(card);
        return card;
    }

    static CheckBox Toggle(bool on, Action<bool> changed)
    {
        var t = new CheckBox { IsChecked = on };
        t.SetResourceReference(StyleProperty, "ToggleSwitch");
        t.Click += (_, _) => changed(t.IsChecked == true);
        return t;
    }

    static ComboBox Combo(IEnumerable<(string Value, string Text)> items, string current, Action<string> changed)
    {
        var c = new ComboBox { MinWidth = ComboWidth, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (v, t) in items) c.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? c.Items[0];
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem i) changed((string)i.Tag); };
        return c;
    }

    static Button Btn(string text, Action a, bool accent = false)
    {
        var b = new Button { Content = text, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        if (accent) b.SetResourceReference(StyleProperty, "AccentButtonStyle");
        b.Click += (_, _) => a();
        return b;
    }

    static Button IconBtn(string glyph, string tip, Action a)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Margin = new Thickness(4, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, "IconButton");
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    static StackPanel Row(params UIElement[] els)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var e in els) sp.Children.Add(e);
        return sp;
    }

    static TextBlock Link(string text, Action a)
    {
        var h = new Hyperlink(new Run(text));
        h.Click += (_, _) => a();
        return new TextBlock(h) { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
    }

    static TextBlock Caption(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "Caption");
        return t;
    }

    // ---------- Schedules (1.2.0) ----------

    /// <summary>All schedules: script, when, preset, next run, the state of its task; on/off, Run now, Edit, Delete.
    /// Rebuilt alone (not the whole page) when Task Scheduler was checked.</summary>
    void BuildSchedules()
    {
        var svc = App.Sched;
        if (!schedHooked && svc != null)
        {
            schedHooked = true;
            svc.Changed += () => Dispatcher.BeginInvoke(() => { if (schedHost.Parent != null) BuildSchedules(); });
        }
        schedHost.Children.Clear();
        var s = St.Settings;
        var list = ScheduleList.All(s)
            .Select(x => (Sched: x, File: St.Find(x.Rel)))
            .OrderBy(x => x.File?.Name ?? x.Sched.Rel, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Sched.NextRun(DateTime.Now) ?? DateTime.MaxValue).ToList();
        var rowsPanel = new StackPanel();
        if (list.Count == 0) rowsPanel.Children.Add(Caption(Loc.T("schedNone")));
        var culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.Language == "ru" ? "ru-RU" : "en-US");
        foreach (var (sc, f) in list)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string state = svc?.StateOf(sc.Id) ?? "";
            if (state is "missing" or "outdated" && f != null)
            {
                var repair = Btn(Loc.T("schedRepair"), () => App.Fire(async () =>
                {
                    string? fail = svc != null ? await svc.ApplyAsync(sc) : null;
                    App.Status(fail != null ? Loc.F("schedTaskFail", fail) : Loc.F("schedSaved", f.Name, sc.Describe()), fail != null);
                    BuildSchedules();
                }, "schedule"));
                repair.ToolTip = Loc.T("schedRepairTip");
                actions.Children.Add(repair);
            }
            var toggle = Toggle(sc.Enabled, on => App.Fire(async () =>
            {
                sc.Enabled = on;
                ScheduleList.Put(s, sc);
                St.SaveSettingsNow();
                St.NotifyScriptsChanged();
                string? fail = svc != null ? await svc.ApplyAsync(sc) : null;
                if (fail != null) App.Status(Loc.F("schedTaskFail", fail), true);
                BuildSchedules();
            }, "schedule"));
            toggle.ToolTip = Loc.T("schedEnableTip");
            toggle.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(toggle, Loc.T("schedEnabled"));
            actions.Children.Add(toggle);
            var runNow = IconBtn(Glyphs.Play, Loc.T("schedRunNow"), () => _ = St.Runs.RunUnattendedAsync(sc.Rel, sc.Preset, sc.Window));
            runNow.IsEnabled = f != null;
            actions.Children.Add(runNow);
            var edit = IconBtn(Glyphs.Edit, Loc.T("schedEditTip"), () => { if (f != null) ScheduleDialog.Show(f, sc); });
            edit.IsEnabled = f != null;
            actions.Children.Add(edit);
            actions.Children.Add(IconBtn(Glyphs.Delete, Loc.T("schedDeleteTip"), () => App.Fire(async () =>
            {
                string name = f?.Name ?? sc.Rel;
                if (!Dlg.Confirm(Loc.T("schedTitle"), Loc.T("schedDeleteHead"), Loc.F("schedDeleteText", name, sc.Describe()), Loc.T("schedDelete"), true, Glyphs.Delete)) return;
                await ScheduleDialog.DeleteAsync(sc, name);
                BuildSchedules();
            }, "schedule")));
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = f?.Name ?? sc.Rel, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f?.Rel ?? sc.Rel });
            var parts = new List<string> { sc.Describe() };
            if (sc.Preset.Length > 0) parts.Add(Loc.F("schedPresetPart", sc.Preset));
            if (sc.Window) parts.Add(Loc.T("schedWindowPart"));
            var next = sc.NextRun(DateTime.Now);
            parts.Add(!sc.Enabled ? Loc.T("schedIsOff") : next != null ? Loc.F("schedNext", next.Value.ToString("g", culture)) : "");
            text.Children.Add(Caption(string.Join(" · ", parts.Where(p => p.Length > 0))));
            string problem = f == null ? Loc.T("schedScriptGone") : state == "missing" ? Loc.T("schedStateMissing") : state == "outdated" ? Loc.T("schedStateOutdated") : "";
            if (problem.Length > 0)
            {
                var p = new TextBlock { Text = problem, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                p.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
                text.Children.Add(p);
            }
            row.Children.Add(text);
            rowsPanel.Children.Add(row);
        }
        if (svc is { Checking: true }) rowsPanel.Children.Add(Caption(Loc.T("schedChecking")));
        else if (svc?.QueryError != null) rowsPanel.Children.Add(Caption(Loc.F("schedQueryFail", svc.QueryError)));
        var refresh = IconBtn(Glyphs.Refresh, Loc.T("schedRefreshTip"), () => { if (svc != null) _ = svc.RefreshAsync(); });
        refresh.IsEnabled = svc is { Checking: false };
        Card(Glyphs.Clock, Loc.T("schedListTitle"), Loc.T("schedListNote"), refresh, rowsPanel, schedHost);
        if (svc?.Last is { Orphans.Count: > 0 } last)
        {
            Card(Glyphs.Warning, Loc.F("schedOrphans", last.Orphans.Count), Loc.T("schedOrphansNote") + "\n" + string.Join("\n", last.Orphans.Take(5)) + (last.Orphans.Count > 5 ? "\n…" : ""),
                Btn(Loc.T("schedOrphansClean"), () => App.Fire(async () =>
                {
                    int n = await svc.CleanOrphansAsync();
                    App.Status(Loc.F("schedOrphansDone", n));
                    BuildSchedules();
                }, "schedule")), null, schedHost);
        }
    }

    // ---------- The page ----------

    void Build()
    {
        var s = St.Settings;
        body.Children.Clear();
        // Title, then 16 px to the first section like the other pages (was 8 + the section's 24 = 32)
        var title = new TextBlock { Text = Loc.T("navSettings"), Margin = new Thickness(0) };
        title.SetResourceReference(StyleProperty, "PageTitle");
        body.Children.Add(title);

        // General
        Section(Loc.T("setGeneral")).Margin = new Thickness(0, 16, 0, 8);
        Card(Glyphs.Globe, Loc.T("setLanguage"), Loc.T("setLangTip"),
            Combo(new[] { ("en", "English"), ("ru", "Русский") }, s.Language, v => Dispatcher.BeginInvoke(() => SetLanguage(v))));
        string? auto = AutoStart.Current();
        Card("\uE7E8", Loc.T("setAutostart"), auto is { Length: > 0 } ? Loc.T("setAutostartOther") : Loc.T("setAutostartDesc"),
            Toggle(auto != null, on =>
            {
                try { AutoStart.Set(on); App.Status(on ? Loc.F("setAutostartNote", AutoStart.Command) : Loc.T("setAutostart") + ": " + Loc.T("offWord")); }
                catch (Exception ex) { App.Status(Loc.F("setAutostartFail", ex.Message), true); }
                Build();
            }));
        Card("\uE73F", Loc.T("setStartMin"), Loc.T("setStartMinNote"), Toggle(s.StartMinimized, on => { s.StartMinimized = on; St.SaveSettings(); }));
        Card("\uE8BB", Loc.T("setCloseTray"), Loc.T("setCloseTrayNote"), Toggle(s.CloseToTray, on => { s.CloseToTray = on; St.SaveSettings(); }));
        BuildHotkey();
        Card(Glyphs.Edit, Loc.T("setEditor"), ExternalEditors.SummaryText(s),
            Btn(Loc.T("setEditorBtn"), () => { if (EditorChoiceDialog.ShowDialog(Window.GetWindow(this))) Build(); }));

        // Appearance
        Section(Loc.T("setAppearance"));
        Card(Glyphs.Color, Loc.T("setTheme"), null,
            Combo(new[] { ("system", Loc.T("setThemeSystem")), ("light", Loc.T("setThemeLight")), ("dark", Loc.T("setThemeDark")) }, s.Theme,
                v => { s.Theme = v; St.SaveSettings(); Dispatcher.BeginInvoke(() => ThemeManager.Apply(s)); }));
        // Accent: "Windows accent" on its own line, then a tidy row of 32 px swatches with 8 px gaps
        var accentPanel = new StackPanel();
        var sys = new RadioButton { Content = Loc.T("setAccentSystem"), ToolTip = Loc.T("setAccentSystemTip"), GroupName = "accent", IsChecked = s.Accent == "system", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        sys.Checked += (_, _) => { s.Accent = "system"; St.SaveSettings(); ThemeManager.Apply(s); };
        accentPanel.Children.Add(sys);
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var hex in Settings.AccentChoices)
        {
            var color = ThemeManager.ParseColor(hex, Colors.Gray);
            var rb = new RadioButton
            {
                GroupName = "accent", IsChecked = string.Equals(s.Accent, hex, StringComparison.OrdinalIgnoreCase), ToolTip = hex,
                Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand, FocusVisualStyle = null, Template = SwatchTemplate(color)
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, hex);
            string h = hex;
            rb.Checked += (_, _) => { s.Accent = h; St.SaveSettings(); ThemeManager.Apply(s); };
            swatches.Children.Add(rb);
        }
        accentPanel.Children.Add(swatches);
        var colorsLink = Link(Loc.T("setColorsLink"), () => ShellOps.Open("ms-settings:colors"));
        colorsLink.Margin = new Thickness(0);
        Card("\uE771", Loc.T("setAccent"), Loc.T("setAccentDesc"), colorsLink, accentPanel);

        // Running
        Section(Loc.T("secRun"));
        // 1.2.0: what a plain Run does for scripts without their own choice
        Card(Glyphs.Window, Loc.T("setDefaultMode"), Loc.T("setDefaultModeNote"),
            Combo(new[] { ("window", Loc.T("modeDefWindow")), ("inline", Loc.T("modeDefInline")) }, s.DefaultMode,
                v => { s.DefaultMode = v; St.SaveSettings(); St.NotifyScriptsChanged(); }));
        // Notifications (1.2.0: off / only errors / always; was an on/off switch) and the minimum run length
        ComboBox? afterBox = null;
        Card(Glyphs.Bell, Loc.T("setNotify"), Loc.T("setNotifyNote"),
            Combo(new[] { ("off", Loc.T("notifyOff")), ("errors", Loc.T("notifyErrors")), ("always", Loc.T("notifyAlways")) }, s.Notify,
                v => { s.Notify = v; St.SaveSettings(); if (afterBox != null) afterBox.IsEnabled = v != "off"; }));
        var toastSecs = Settings.ToastAfterChoices.Append(s.ToastAfterSeconds).Distinct().OrderBy(n => n);
        afterBox = Combo(toastSecs.Select(n => (n.ToString(), n == 0 ? Loc.T("toastAnyLength") : Loc.F("secondsN", n))), s.ToastAfterSeconds.ToString(), v => { s.ToastAfterSec = v; St.SaveSettings(); });
        afterBox.IsEnabled = s.Notify != "off";
        Card("\uE916", Loc.T("setToastAfter"), Loc.T("setToastAfterNote"), afterBox);
        Card(Glyphs.Pin, Loc.T("setJumpList"), Loc.T("setJumpListNote"), Toggle(s.JumpList, on => { s.JumpList = on; St.SaveSettings(); JumpLists.Update(); }));

        // Schedules (1.2.0)
        Section(Loc.T("secSchedules"));
        body.Children.Add(schedHost);
        BuildSchedules();

        // Environment (was "Engines"): the shell picker shows the settings of one shell (PowerShell or Python); it is a view, not a setting
        Section(Loc.T("setEngines"));
        if (!St.PythonChecked && !St.PythonSearching) Dispatcher.BeginInvoke(() => { if (!St.PythonChecked && !St.PythonSearching) _ = St.FindPythonAsync(); });
        var engHost = new StackPanel();
        void ShowShell()
        {
            engHost.Children.Clear();
            if (_shellView == "py") BuildPython(engHost);
            else if (_shellView == "custom") BuildCustom(engHost);
            else BuildPowerShell(engHost);
        }
        Card(Glyphs.Script, Loc.T("setShell"), Loc.T("setShellNote"),
            Combo(new[] { ("ps", "PowerShell"), ("py", "Python"), ("custom", Loc.T("setShellCustom")) }, _shellView, v => { _shellView = v; ShowShell(); }));
        body.Children.Add(engHost);
        ShowShell();

        // Safety
        Section(Loc.T("setSafety"));
        Card(Glyphs.Shield, Loc.T("setSafetyOn"), Loc.T("setSafetyNote"), Toggle(s.SafetyWarnings, _ => { ToggleSafety(); Build(); }));
        BuildSources();

        // History
        Section(Loc.T("setHistory"));
        Card(Glyphs.History, Loc.T("setHistMax"), Loc.F("setHistInfo", St.History.Entries.Count, RunResult.FormatSize(St.History.DiskBytes())),
            Combo(Settings.HistoryMaxChoices.Select(n => (n.ToString(), n.ToString())), s.HistoryMax, v => { s.HistoryMax = v; St.History.Max = s.HistoryMaxValue; St.History.Limit(); St.History.Save(); St.SaveSettings(); App.HistoryChanged(); }));
        Card(Glyphs.Data, Loc.T("setHistKB"), Loc.T("setHistKBNote"),
            Combo(Settings.HistoryKBChoices.Select(n => (n.ToString(), RunResult.FormatSize(n * 1024L))), s.HistoryOutputKB, v => { s.HistoryOutputKB = v; St.SaveSettings(); }));

        // Folders and files
        Section(Loc.T("setFolders"));
        Card(Glyphs.Folder, Loc.T("setScriptsFolder"), St.Paths.Scripts, Btn(Loc.T("setOpenFolder"), () => ShellOps.OpenFolder(St.Paths.Scripts)));
        BuildExternalFolders();
        Card(Glyphs.Build, Loc.T("openBuildFolder"), St.Paths.Build, Btn(Loc.T("setOpenFolder"), () => ShellOps.OpenFolder(St.Paths.Build)));
        Card("\uE713", Loc.T("setSettingsFile"), St.Paths.Settings, Row(
            Btn(Loc.T("setOpenFile"), () => { St.SaveSettingsNow(); ShellOps.Open(St.Paths.Settings); }),
            Btn(Loc.T("setOpenFolder"), () => ShellOps.ShowInExplorer(St.Paths.Settings))));

        // Updates
        Section(Loc.T("setUpdates"));
        Card(Glyphs.Update, Loc.T("setUpdateCheck"), Loc.T("setUpdateNote"), Toggle(s.UpdateCheck, on => { s.UpdateCheck = on; St.SaveSettings(); }));

        // About: version, repo link, and log
        Section(Loc.T("setAbout"));
        var links = new WrapPanel();
        if (UpdateChecker.IsRepoSet(AppState.Repo))
        {
            var repoH = new Hyperlink(new Run("github.com/" + AppState.Repo)) { TextDecorations = null };
            repoH.Click += (_, _) => ShellOps.Open("https://github.com/" + AppState.Repo);
            var repoIcon = new TextBlock { Text = Glyphs.Globe, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            repoIcon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var repoText = new TextBlock(repoH) { VerticalAlignment = VerticalAlignment.Center };
            var repoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("aboutRepoTip") };
            repoRow.Children.Add(repoIcon);
            repoRow.Children.Add(repoText);
            links.Children.Add(repoRow);
        }
        // Support the project (Boosty): a link like the GitHub one, no pop-ups
        {
            var supH = new Hyperlink(new Run(Loc.T("aboutSupport"))) { TextDecorations = null };
            supH.Click += (_, _) => ShellOps.Open(Links.Donate);
            var supIcon = new TextBlock { Text = "\uEB51", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            supIcon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var supRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Links.DonateShort };
            supRow.Children.Add(supIcon);
            supRow.Children.Add(new TextBlock(supH) { VerticalAlignment = VerticalAlignment.Center });
            System.Windows.Automation.AutomationProperties.SetName(supRow, Loc.T("aboutSupport"));
            links.Children.Add(supRow);
        }
        if (AppLog.FilePath != null)
        {
            var logH = new Hyperlink(new Run(Loc.T("logTitle"))) { TextDecorations = null };
            logH.Click += (_, _) => { if (File.Exists(AppLog.FilePath)) ShellOps.Open(AppLog.FilePath); };
            links.Children.Add(new TextBlock(logH) { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        var about = Card("\uE946", "Shelly " + AppState.Version,
            Loc.F("setAboutHost", Environment.Version.ToString(3), SystemInfo.WindowsBuild),
            links);
        // The icon of this card hides a small secret (three quick clicks)
        if (about.Child is Grid ag && ag.Children.Count > 0 && ag.Children[0] is TextBlock aboutIcon)
        {
            aboutIcon.Background = Brushes.Transparent;   // the whole icon cell takes clicks, not only the glyph strokes
            MascotEgg.Attach(aboutIcon);
        }
    }

    /// <summary>A round color swatch: 24 px dot; selected = 2 px ring in the primary text color with a 2 px gap;
    /// hover = a lighter ring; keyboard focus = the focus stroke.</summary>
    static ControlTemplate SwatchTemplate(Color c)
    {
        var t = new ControlTemplate(typeof(RadioButton));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(WidthProperty, 32.0);
        grid.SetValue(HeightProperty, 32.0);
        grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var ring = new FrameworkElementFactory(typeof(Ellipse), "Ring");
        ring.SetValue(Shape.StrokeThicknessProperty, 2.0);
        ring.SetValue(Shape.StrokeProperty, Brushes.Transparent);
        var fill = new FrameworkElementFactory(typeof(Ellipse));
        fill.SetValue(Shape.FillProperty, new SolidColorBrush(c));
        fill.SetValue(MarginProperty, new Thickness(4));
        grid.AppendChild(ring);
        grid.AppendChild(fill);
        t.VisualTree = grid;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension("ControlStrongStrokeColorDefaultBrush"), "Ring"));
        t.Triggers.Add(hover);
        var focus = new Trigger { Property = FocusRing.ShowProperty, Value = true };
        focus.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension("FocusStrokeColorOuterBrush"), "Ring"));
        t.Triggers.Add(focus);
        var trig = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        trig.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension("TextFillColorPrimaryBrush"), "Ring"));
        t.Triggers.Add(trig);
        return t;
    }

    public void ShowCompat() { }

    // ---------- Trusted sources ----------

    /// <summary>Download-and-run from these HTTPS sources is an info notice instead of a danger. Built-ins (marked) can
    /// be turned off; the user's own entries can be removed.</summary>
    // ---------- External script folders ----------

    void BuildExternalFolders()
    {
        var s = St.Settings;
        var list = new StackPanel();
        if (s.ExternalFolders.Count == 0) list.Children.Add(Caption(Loc.T("setExtNone")));
        foreach (var path in s.ExternalFolders.ToList())
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            string folder = path;
            var buttons = Row(Btn(Loc.T("setOpenFolder"), () => ShellOps.OpenFolder(folder)), Btn(Loc.T("setExtRemove"), () => RemoveExternalFolder(folder)));
            DockPanel.SetDock(buttons, Dock.Right);
            dp.Children.Add(buttons);
            var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = folder };
            tb.Inlines.Add(new Run(folder));
            if (!St.ExternalRoots.Any(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase)))
            {
                var tag = new Run("   " + Loc.T("setExtMissing"));
                tag.SetResourceReference(TextElement.ForegroundProperty, "SystemFillColorCautionBrush");
                tb.Inlines.Add(tag);
            }
            dp.Children.Add(tb);
            list.Children.Add(dp);
        }
        list.Visibility = _extFoldersExpanded ? Visibility.Visible : Visibility.Collapsed;

        var toggleBtn = new Button { Content = _extFoldersExpanded ? Glyphs.ChevronUp : Glyphs.ChevronDown, Margin = new Thickness(4, 0, 0, 0) };
        toggleBtn.SetResourceReference(StyleProperty, "IconButton");
        Ico.SetSize(toggleBtn, 12);
        System.Windows.Automation.AutomationProperties.SetName(toggleBtn, Loc.T("setExtFolders"));
        toggleBtn.Click += (_, _) =>
        {
            _extFoldersExpanded = !_extFoldersExpanded;
            list.Visibility = _extFoldersExpanded ? Visibility.Visible : Visibility.Collapsed;
            toggleBtn.Content = _extFoldersExpanded ? Glyphs.ChevronUp : Glyphs.ChevronDown;
        };

        Card(Glyphs.Folder, Loc.T("setExtFolders"), Loc.T("setExtNote"), Row(Btn(Loc.T("setExtAdd"), AddExternalFolder), toggleBtn), list);
    }

    void AddExternalFolder()
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("setExtPick") };
        if (d.ShowDialog(App.OwnerWindow()) != true) return;
        if (TryAddExternalFolder(d.FolderName))
        {
            _extFoldersExpanded = true;
            Build();
        }
    }

    /// <summary>Adds an external script folder (Settings, and "Add as external folder" of a large folder import); a problem
    /// is shown in a message. True when it was added.</summary>
    public static bool TryAddExternalFolder(string folder)
    {
        var s = St.Settings;
        string err = ExternalScripts.Check(folder, St.Paths.Scripts, s.ExternalFolders, out string full);
        if (err.Length > 0)
        {
            string text = err switch
            {
                "extNotAbsolute" => Loc.T("extNotAbsolute"),
                "extMissing" => Loc.T("extMissing"),
                "extDuplicate" => Loc.T("extDuplicate"),
                "extInsideScripts" => Loc.T("extInsideScripts"),
                "extContainsScripts" => Loc.T("extContainsScripts"),
                "extOverlap" => Loc.T("extOverlap"),
                "extTooMany" => Loc.F("extTooMany", ExternalScripts.MaxFolders),
                _ => Loc.T("extInvalid")
            };
            Dlg.Message(Loc.T("setExtFolders"), text, Glyphs.Warning, "SystemFillColorCautionBrush");
            return false;
        }
        s.ExternalFolders.Add(full);
        St.ExternalFoldersChanged();
        App.Status(Loc.F("extAdded", full));
        return true;
    }

    void RemoveExternalFolder(string path)
    {
        var s = St.Settings;
        s.ExternalFolders.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        s.DropFolder(ExternalScripts.RootKey(path));
        St.ReloadLastRuns();
        St.ExternalFoldersChanged();
        JumpLists.Update();
        App.Status(Loc.F("extRemoved", path));
        Build();
    }

    void BuildSources()
    {
        var s = St.Settings;
        void Changed() { St.NotifySettingsChanged(); St.NotifyScriptsChanged(); }
        var list = new StackPanel();
        foreach (var b in TrustedSources.BuiltIn)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var src = b;
            var tg = Toggle(!TrustedSources.IsDisabled(s, b.Pattern), on => { TrustedSources.SetBuiltIn(s, src.Pattern, on); Changed(); });
            System.Windows.Automation.AutomationProperties.SetName(tg, b.Pattern);
            DockPanel.SetDock(tg, Dock.Right);
            dp.Children.Add(tg);
            var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Loc.F("setSrcBuiltinTip", b.Name, b.Evidence) };
            tb.Inlines.Add(new Run(b.Pattern));
            var tag = new Run("   " + Loc.T("setSrcBuiltin") + " · " + b.Name);
            tag.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
            tb.Inlines.Add(tag);
            dp.Children.Add(tb);
            list.Children.Add(dp);
        }
        foreach (var u in s.TrustedSources.ToList())
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            string pat = u;
            var rm = Btn(Loc.T("setSrcRemove"), () => { TrustedSources.Remove(s, pat); Changed(); App.Status(Loc.F("srcRemoved", pat)); Build(); });
            DockPanel.SetDock(rm, Dock.Right);
            dp.Children.Add(rm);
            dp.Children.Add(new TextBlock { Text = u, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            list.Children.Add(dp);
        }
        var box = new TextBox { MinWidth = 260, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = Loc.T("setSrcAddTip") };
        void AddIt()
        {
            var p = TrustedSources.Normalize(box.Text);
            if (p == null) { App.Status(Loc.F("srcInvalid", box.Text.Trim()), true); return; }
            TrustedSources.Add(s, p); Changed(); App.Status(Loc.F("srcAdded", p)); Build();
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddIt(); };
        var add = Row(box, Btn(Loc.T("setSrcAdd"), AddIt));
        add.Margin = new Thickness(0, 12, 0, 0);
        list.Children.Add(add);
        list.Visibility = _sourcesExpanded ? Visibility.Visible : Visibility.Collapsed;

        // Chevron button, the same icon button as in the Compatibility card: up when expanded, down when collapsed
        var toggleBtn = new Button { Content = _sourcesExpanded ? Glyphs.ChevronUp : Glyphs.ChevronDown, Margin = new Thickness(4, 0, 0, 0) };
        toggleBtn.SetResourceReference(StyleProperty, "IconButton");
        Ico.SetSize(toggleBtn, 12);
        System.Windows.Automation.AutomationProperties.SetName(toggleBtn, Loc.T("setSources"));
        toggleBtn.Click += (_, _) =>
        {
            _sourcesExpanded = !_sourcesExpanded;
            list.Visibility = _sourcesExpanded ? Visibility.Visible : Visibility.Collapsed;
            toggleBtn.Content = _sourcesExpanded ? Glyphs.ChevronUp : Glyphs.ChevronDown;
        };

        Card(Glyphs.Globe, Loc.T("setSources"), Loc.T("setSourcesNote"), toggleBtn, list);
    }

    // ---------- PowerShell / Python ----------

    /// <summary>PowerShell: the engine that "Auto" scripts get (behaviour unchanged since 1.0). 1.1.2: the items read
    /// System / PowerShell 5.1 / PowerShell 7+; the stored values stay "auto" / "5.1" / "7".</summary>
    void BuildPowerShell(Panel into)
    {
        var s = St.Settings;
        var combo = Combo(new[] { ("auto", Loc.T("setEngSystem")), ("5.1", Loc.T("setEng51")), ("7", Loc.T("setEng7")) }, s.EngineDefault,
            v => { s.EngineDefault = v; St.NotifySettingsChanged(); St.NotifyScriptsChanged(); });
        if (combo.Items[0] is ComboBoxItem autoItem) autoItem.ToolTip = Loc.T("engDefAuto");
        Card(Glyphs.Script, Loc.T("setEngDefault"), Loc.T("setEngDefaultNote"), combo, null, into);
    }

    /// <summary>Python: one summary line ("Python 3.12 · custom path", full path in the tooltip; the text comes from
    /// <see cref="PythonSummary"/>) and two small buttons, Choose… and Reset. The search runs off the UI thread
    /// (<see cref="Services.AppState.FindPythonAsync"/>); PythonChanged rebuilds the page with the result.</summary>
    void BuildPython(Panel into)
    {
        var s = St.Settings;
        var sum = PythonSummary.For(s.PythonPath, St.Python, St.PythonError, St.PythonSearching || !St.PythonChecked);
        void Save(string path)
        {
            path = path.Trim().Trim('"').Trim();
            if (path == s.PythonPath) { _ = St.FindPythonAsync(); return; }
            s.PythonPath = path;
            St.SaveSettings();
            App.Status(Loc.F("setPythonSaved", path.Length > 0 ? path : Loc.T("pySumAuto")));
            _ = St.FindPythonAsync();
        }
        var pick = SmallBtn(Loc.T("pyPick"), Loc.T("setPythonPick"), () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("pyExeFilter"), Title = Loc.T("setPythonPick"), CheckFileExists = true };
            try { if (s.PythonPath.Length > 0 && System.IO.Path.GetDirectoryName(s.PythonPath) is { Length: > 0 } dir && System.IO.Directory.Exists(dir)) d.InitialDirectory = dir; } catch { }
            if (d.ShowDialog(App.OwnerWindow()) == true) Save(d.FileName);
        });
        var reset = SmallBtn(Loc.T("pyReset"), Loc.T("pyResetTip"), () => Save(""));
        reset.IsEnabled = sum.CanReset;
        var card = Card(ScriptHeader.PythonGlyph, sum.Text, null, Row(pick, reset), null, into);
        card.ToolTip = sum.Tip;
        System.Windows.Automation.AutomationProperties.SetName(card, "Python: " + sum.Text);
        Card(Glyphs.Folder, Loc.T("setUseVenv"), Loc.T("setUseVenvNote"),
            Toggle(s.UseVenv, on => { s.UseVenv = on; St.SaveSettings(); St.NotifyScriptsChanged(); }), null, into);
    }

    /// <summary>Custom interpreters (1.3.0): one card per entry (name, extensions, program; red when the program is not
    /// found) with Edit and Delete, then Add. Batch files need nothing here (cmd.exe), and nothing is detected or scanned:
    /// the note says so.</summary>
    void BuildCustom(Panel into)
    {
        var s = St.Settings;
        var list = CustomInterpreters.Load(s);
        var note = Caption(Loc.T("ciNote"));
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 0, 0, 8);
        into.Children.Add(note);
        if (list.Count == 0) Card(ScriptHeader.CustomGlyph, Loc.T("ciEmpty"), null, null, null, into);
        foreach (var c in list)
        {
            string? prog = CustomInterpreters.ResolveProgram(c.Program, System.IO.File.Exists, AnalysisService.PathDirs());
            var edit = SmallBtn(Loc.T("ciEdit"), Loc.T("ciEdit"), () => { if (Dialogs.CustomInterpreterDialog.Show(c)) Build(); });
            var del = SmallBtn(Loc.T("ciDelete"), Loc.T("ciDelete"), () =>
            {
                if (!Dlg.Confirm(Loc.T("setShellCustom"), Loc.T("ciDeleteTitle"), Loc.F("ciDeleteText", c.Name, c.ExtText), Loc.T("ciDelete"), true, Glyphs.Delete)) return;
                CustomInterpreters.Remove(s, c.Id);
                St.CustomInterpretersChanged();
                App.Status(Loc.F("ciDeleted", c.Name));
                Build();
            });
            var desc = c.ExtText + "  ·  " + (prog == null ? Loc.F("ciMissing", c.Program) : prog);
            var card = Card(ScriptHeader.CustomGlyph, c.Name, desc, Row(edit, del), null, into);
            card.ToolTip = c.Program + "\n" + c.Template;
            System.Windows.Automation.AutomationProperties.SetName(card, c.Name);
            if (prog == null && card.Child is Grid g && g.Children.OfType<StackPanel>().FirstOrDefault() is { } sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
                tb.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        }
        var add = Btn(Loc.T("ciAdd"), () => { if (Dialogs.CustomInterpreterDialog.Show(null)) Build(); });
        add.IsEnabled = list.Count < CustomInterpreters.MaxEntries;
        add.Margin = new Thickness(0, 8, 0, 0);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        if (!add.IsEnabled) { add.ToolTip = Loc.F("ciErrMax", CustomInterpreters.MaxEntries); ToolTipService.SetShowOnDisabled(add, true); }
        into.Children.Add(add);
    }

    /// <summary>A small text button (the Python row): no minimum width, tight padding.</summary>
    static Button SmallBtn(string text, string tip, Action a)
    {
        var b = new Button { Content = text, ToolTip = tip, MinWidth = 0, MinHeight = 0, Padding = new Thickness(10, 3, 10, 4), FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        ToolTipService.SetShowOnDisabled(b, true);
        System.Windows.Automation.AutomationProperties.SetHelpText(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    // ---------- Hotkey ----------

    void BuildHotkey()
    {
        var s = St.Settings;
        var box = new TextBox { Width = 170, IsReadOnly = true, Text = s.Hotkey.Length > 0 ? s.Hotkey : Loc.T("setHotkeyNone"), ToolTip = Loc.T("setHotkeyNote"), VerticalContentAlignment = VerticalAlignment.Center };
        var state = Caption("");
        state.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        void ShowState(string text) { state.Text = text; state.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
        ShowState(App.Hotkey?.StateIsProblem == true ? App.Hotkey.StateText : "");
        box.GotKeyboardFocus += (_, _) => box.Text = Loc.T("setHotkeyPress");
        box.LostKeyboardFocus += (_, _) => box.Text = s.Hotkey.Length > 0 ? s.Hotkey : Loc.T("setHotkeyNone");
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (key == Key.Escape || key == Key.Tab) { Keyboard.ClearFocus(); return; }
            var m = Keyboard.Modifiers;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= ModifierKeys.Windows;
            string text = HotkeyService.Format(m, key);
            if (HotkeyService.Parse(text) == null) { ShowState(Loc.T("setHotkeyInvalid")); return; }
            if (App.Hotkey != null && App.Hotkey.Apply(text))
            {
                s.Hotkey = App.Hotkey.Current;
                St.SaveSettings();
            }
            ShowState(App.Hotkey?.StateIsProblem == true ? App.Hotkey.StateText : "");
            Keyboard.ClearFocus();
        };
        // One compact control, flush with the right edge like the other rows: the box plus icon buttons (turn off, default), each shown only when it changes something
        var off = IconBtn(Glyphs.Cancel, Loc.T("setHotkeyOffTip"), () => { App.Hotkey?.Apply(""); s.Hotkey = ""; St.SaveSettings(); Build(); });
        off.Visibility = s.Hotkey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var def = IconBtn(Glyphs.Rerun, Loc.T("setHotkeyResetTip"), () => { if (App.Hotkey?.Apply("Ctrl+Alt+L") == true) { s.Hotkey = "Ctrl+Alt+L"; St.SaveSettings(); } Build(); });
        def.Visibility = s.Hotkey != "Ctrl+Alt+L" ? Visibility.Visible : Visibility.Collapsed;
        Card(Glyphs.Keyboard, Loc.T("setHotkey"), Loc.T("setHotkeyLabel"), Row(box, off, def), state);
    }
}
