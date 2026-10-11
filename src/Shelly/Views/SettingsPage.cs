using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>Settings in Windows 11 style cards.</summary>
public sealed class SettingsPage : UserControl, IPage
{
    static AppState St => App.S;
    readonly StackPanel body = new() { Margin = new Thickness(36, 24, 36, 36), MaxWidth = 1000 };

    public SettingsPage()
    {
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loc.LanguageChanged += Build;
        PwshInstall.WatchChanged += Build;
        St.EnginesChanged += Build;
        Build();
    }

    public void Activated() => Build();

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

    void Section(string title)
    {
        var t = new TextBlock { Text = title };
        t.SetResourceReference(StyleProperty, "SectionTitle");
        body.Children.Add(t);
    }

    Border Card(string glyph, string title, string? desc, UIElement? right, UIElement? below = null)
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
        body.Children.Add(card);
        return card;
    }

    static CheckBox Toggle(bool on, Action<bool> changed)
    {
        var t = new CheckBox { IsChecked = on };
        t.SetResourceReference(StyleProperty, "ToggleSwitch");
        t.Click += (_, _) => changed(t.IsChecked == true);
        return t;
    }

    static ComboBox Combo(IEnumerable<(string Value, string Text)> items, string current, Action<string> changed, double width = 220)
    {
        var c = new ComboBox { MinWidth = width };
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

    // ---------- The page ----------

    void Build()
    {
        var s = St.Settings;
        body.Children.Clear();
        var title = new TextBlock { Text = Loc.T("navSettings"), Margin = new Thickness(0, 0, 0, 8) };
        title.SetResourceReference(StyleProperty, "PageTitle");
        body.Children.Add(title);

        // General
        Section(Loc.T("setGeneral"));
        Card(Glyphs.Globe, Loc.T("setLanguage"), Loc.T("setLangTip"),
            Combo(new[] { ("en", "English"), ("ru", "Русский") }, s.Language, v => Dispatcher.BeginInvoke(() => SetLanguage(v)), 180));
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

        // Appearance
        Section(Loc.T("setAppearance"));
        Card(Glyphs.Color, Loc.T("setTheme"), null,
            Combo(new[] { ("system", Loc.T("setThemeSystem")), ("light", Loc.T("setThemeLight")), ("dark", Loc.T("setThemeDark")) }, s.Theme,
                v => { s.Theme = v; St.SaveSettings(); Dispatcher.BeginInvoke(() => ThemeManager.Apply(s)); }));
        // Accent: "Windows accent" on its own line, then a tidy row of 32 px swatches with 8 px gaps
        var accentPanel = new StackPanel();
        var sys = new RadioButton { Content = Loc.T("setAccentSystem"), ToolTip = Loc.T("setAccentSystemTip"), GroupName = "accent", IsChecked = s.Accent == "system", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
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
        Card(Glyphs.Bell, Loc.T("setToast"), Loc.T("setToastNote"), Toggle(s.ToastOnFinish, on => { s.ToastOnFinish = on; St.SaveSettings(); }));
        var toastSecs = Settings.ToastAfterChoices.Append(s.ToastAfterSeconds).Distinct().OrderBy(n => n);
        Card("\uE916", Loc.T("setToastAfter"), Loc.T("setToastAfterNote"),
            Combo(toastSecs.Select(n => (n.ToString(), Loc.F("secondsN", n))), s.ToastAfterSeconds.ToString(), v => { s.ToastAfterSec = v; St.SaveSettings(); }, 120));
        Card(Glyphs.Pin, Loc.T("setJumpList"), Loc.T("setJumpListNote"), Toggle(s.JumpList, on => { s.JumpList = on; St.SaveSettings(); JumpLists.Update(); }));

        // Engines
        Section(Loc.T("setEngines"));
        string eng7 = St.PwshOk ? EngineInfo.Name("7") : EngineInfo.Name("5.1");
        Card(Glyphs.Script, Loc.T("setEngDefault"), Loc.T("setEngDefaultNote"),
            Combo(new[] { ("auto", Loc.T("engAuto") + " · " + Loc.T("engDefAuto")), ("5.1", Loc.T("eng51")), ("7", Loc.T("eng7")) }, s.EngineDefault,
                v => { s.EngineDefault = v; St.NotifySettingsChanged(); St.NotifyScriptsChanged(); }, 280));
        var p7 = Row(Btn(Loc.T("p7Check"), () =>
        {
            bool changed = St.RecheckEngines();
            App.Status(St.PwshOk ? Loc.F("p7Found", St.Pwsh!.Version, St.Pwsh.Path) : Loc.T("p7NotFound"));
            if (!changed) Build();
        }));
        if (!St.PwshOk) p7.Children.Add(Btn(Loc.T("p7Install"), App.InstallPwsh, true));
        Card("\uE756", Loc.T("cPwsh"), St.PwshOk ? Loc.F("p7Status", St.Pwsh!.Label, St.Pwsh.Path) : Loc.T("p7StatusNone") + (PwshInstall.Watching ? "  ·  " + Loc.T("p7Started") : ""), p7);
        Card("\uE756", Loc.T("cPs51"), St.Ps51Ok ? Loc.F("cPs51Ok", St.Ps51) : Loc.T("cPs51No"), null);

        // Safety
        Section(Loc.T("setSafety"));
        Card(Glyphs.Shield, Loc.T("setSafetyOn"), Loc.T("setSafetyNote"), Toggle(s.SafetyWarnings, _ => { ToggleSafety(); Build(); }));
        var trusted = new StackPanel();
        if (s.Trusted.Count == 0) trusted.Children.Add(Caption(Loc.T("setTrustedNone")));
        foreach (var t in s.Trusted.ToList())
        {
            int i = t.IndexOf('|');
            string rel = i < 0 ? t : t.Substring(0, i), hash = i < 0 ? "" : t.Substring(i + 1);
            var f = St.Scripts.FirstOrDefault(x => string.Equals(x.Rel, rel, StringComparison.OrdinalIgnoreCase));
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var revoke = Btn(Loc.T("setRevoke"), () => { s.Trusted.Remove(t); St.NotifySettingsChanged(); App.Status(Loc.F("setRevoked", rel)); Build(); });
            DockPanel.SetDock(revoke, Dock.Right);
            dp.Children.Add(revoke);
            dp.Children.Add(new TextBlock { Text = rel + (f == null ? "  " + Loc.T("setTrustGone") : ""), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.F("setTrustTip", rel, hash).Replace("`n", "\n"), TextTrimming = TextTrimming.CharacterEllipsis });
            trusted.Children.Add(dp);
        }
        UIElement? clearAll = s.Trusted.Count > 1 ? Btn(Loc.T("setTrustClear"), () =>
        {
            if (!Dlg.Confirm(Loc.T("setTrusted"), Loc.T("setTrustClear"), Loc.T("setTrustClearText"), Loc.T("setTrustClear"), true, Glyphs.Shield)) return;
            s.Trusted.Clear(); St.NotifySettingsChanged(); Build();
        }) : null;
        Card("\uE8D7", Loc.T("setTrusted"), Loc.T("setTrustedNote"), clearAll, trusted);
        BuildSources();

        // History
        Section(Loc.T("setHistory"));
        Card(Glyphs.History, Loc.T("setHistMax"), Loc.F("setHistInfo", St.History.Entries.Count, RunResult.FormatSize(St.History.DiskBytes())),
            Combo(Settings.HistoryMaxChoices.Select(n => (n.ToString(), n.ToString())), s.HistoryMax, v => { s.HistoryMax = v; St.History.Max = s.HistoryMaxValue; St.History.Limit(); St.History.Save(); St.SaveSettings(); App.HistoryChanged(); }, 120));
        Card(Glyphs.Data, Loc.T("setHistKB"), Loc.T("setHistKBNote"),
            Combo(Settings.HistoryKBChoices.Select(n => (n.ToString(), RunResult.FormatSize(n * 1024L))), s.HistoryOutputKB, v => { s.HistoryOutputKB = v; St.SaveSettings(); }, 120));

        // Folders and files
        Section(Loc.T("setFolders"));
        Card(Glyphs.Folder, Loc.T("setScriptsFolder"), St.Paths.Scripts + "\n" + Loc.T("setScriptsNote"), Btn(Loc.T("setOpenFolder"), () => ShellOps.OpenFolder(St.Paths.Scripts)));
        Card(Glyphs.Build, Loc.T("openBuildFolder"), St.Paths.Build, Btn(Loc.T("setOpenFolder"), () => ShellOps.OpenFolder(St.Paths.Build)));
        Card("\uE713", Loc.T("setSettingsFile"), St.Paths.Settings, Row(
            Btn(Loc.T("setOpenFile"), () => { St.SaveSettingsNow(); ShellOps.Open(St.Paths.Settings); }),
            Btn(Loc.T("setOpenFolder"), () => ShellOps.ShowInExplorer(St.Paths.Settings))));
        Card(Glyphs.Image, Loc.T("setIcons"), Loc.F("setIconsNote", System.IO.Path.Combine("assets", "script-icons")), null);

        // Updates
        Section(Loc.T("setUpdates"));
        Card(Glyphs.Update, Loc.T("setUpdateCheck"), Loc.T("setUpdateNote"), Toggle(s.UpdateCheck, on => { s.UpdateCheck = on; St.SaveSettings(); }));

        // About: name, version, credits, links, and the manual update check
        Section(Loc.T("setAbout"));
        var updState = Caption(UpdateChecker.IsRepoSet(AppState.Repo) ? (Updates.Latest != null ? Loc.F("updAvailable", Updates.Latest.Version, AppState.Version) : "") : Loc.T("updNoRepo"));
        updState.Visibility = updState.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var updRow = Row(Btn(Loc.T("setUpdateNow"), async () =>
        {
            updState.Visibility = Visibility.Visible;
            updState.Text = Loc.T("updChecking");
            updState.Text = await Updates.CheckAsync(true);
            if (Updates.Latest != null) Build();
        }));
        if (Updates.Latest != null) updRow.Children.Add(Btn(Loc.T("updDownload"), Updates.OpenDownload, true));
        var links = new WrapPanel();
        if (UpdateChecker.IsRepoSet(AppState.Repo))
        {
            var repo = Link("github.com/" + AppState.Repo, () => ShellOps.Open("https://github.com/" + AppState.Repo));
            repo.ToolTip = Loc.T("aboutRepoTip");
            links.Children.Add(repo);
        }
        var author = Link(Loc.T("aboutAuthor"), () => ShellOps.Open(AppState.AuthorUrl));
        author.ToolTip = Loc.T("aboutAuthorTip") + "\n" + AppState.AuthorUrl;
        links.Children.Add(author);
        // User guide in the UI language: the local copy next to Shelly.exe, else the one on GitHub
        string guide = Loc.Language == "ru" ? "USER_GUIDE_RU.md" : "USER_GUIDE.md";
        string guidePath = System.IO.Path.Combine(St.Paths.Root, "docs", guide);
        string? guideUrl = File.Exists(guidePath) ? guidePath
            : UpdateChecker.IsRepoSet(AppState.Repo) ? $"https://github.com/{AppState.Repo}/blob/HEAD/docs/{guide}" : null;
        if (guideUrl != null) { var g = Link(Loc.T("aboutGuide"), () => ShellOps.Open(guideUrl)); g.ToolTip = Loc.T("aboutGuideTip") + "\n" + guideUrl; links.Children.Add(g); }
        foreach (var (name, tip) in new[] { ("README.md", "aboutReadmeTip"), ("CHANGELOG.md", "aboutChangelogTip"), ("LICENSE", "setLicense") })
        {
            string p = System.IO.Path.Combine(St.Paths.Root, name);
            if (File.Exists(p)) { var l = Link(name, () => ShellOps.Open(p)); l.ToolTip = Loc.T(tip); links.Children.Add(l); }
        }
        links.Children.Add(Link(Loc.T("aboutDisclaimer"), () => DisclaimerDialog.Show(false)));
        if (AppLog.FilePath != null) links.Children.Add(Link(Loc.T("logTitle"), () => { if (File.Exists(AppLog.FilePath)) ShellOps.Open(AppLog.FilePath); }));
        Card("\uE946", "Shelly " + AppState.Version,
            Loc.T("aboutCredit") + "\n" + Loc.T("setAboutText") + "\n" + Loc.F("setAboutHost", Environment.Version.ToString(3), SystemInfo.WindowsBuild),
            updRow, Stack(links, updState));
    }

    static StackPanel Stack(params UIElement[] els)
    {
        var sp = new StackPanel();
        foreach (var e in els) { sp.Children.Add(e); if (e is FrameworkElement fe && sp.Children.Count > 1) fe.Margin = new Thickness(0, 6, 0, 0); }
        return sp;
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

    // ---------- Trusted sources ----------

    /// <summary>Download-and-run from these HTTPS sources is an info notice instead of a danger. Built-ins (marked) can
    /// be turned off; the user's own entries can be removed.</summary>
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
        add.Margin = new Thickness(0, 10, 0, 0);
        list.Children.Add(add);
        Card(Glyphs.Globe, Loc.T("setSources"), Loc.T("setSourcesNote"), null, list);
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
        // One compact control: the box plus icon buttons (turn off, default), each shown only when it changes something
        var off = IconBtn(Glyphs.Cancel, Loc.T("setHotkeyOffTip"), () => { App.Hotkey?.Apply(""); s.Hotkey = ""; St.SaveSettings(); Build(); });
        off.Visibility = s.Hotkey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var def = IconBtn(Glyphs.Rerun, Loc.T("setHotkeyResetTip"), () => { if (App.Hotkey?.Apply("Ctrl+Alt+L") == true) { s.Hotkey = "Ctrl+Alt+L"; St.SaveSettings(); } Build(); });
        def.Visibility = s.Hotkey != "Ctrl+Alt+L" ? Visibility.Visible : Visibility.Collapsed;
        var slot = Row(off, def);
        slot.Width = 72;   // keeps the box in place whichever buttons are shown
        Card(Glyphs.Keyboard, Loc.T("setHotkey"), Loc.T("setHotkeyLabel"), Row(box, slot), state);
    }
}
