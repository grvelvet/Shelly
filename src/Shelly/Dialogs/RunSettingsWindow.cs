using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Dialogs;

/// <summary>Run settings of a script, as a compact WinUI ContentDialog: the script's icon, name and description; a
/// two-column parameter grid (label | box, defaults as placeholders); engine and mode side by side; the last run in one
/// line; warnings when there are any; Remember + Run / Cancel in the button band.</summary>
public sealed class RunSettingsWindow : DialogWindow
{
    /// <summary>Preset: the parameter preset the values are exactly from (1.2.0; null = none or changed in the form).</summary>
    public sealed record Answer(string Mode, Dictionary<string, object> Values, string? Preset = null);

    public const double DialogWidth = 520;   // WinUI ContentDialog: 320..548
    const double Row = 32, Gap = 8;

    readonly AppState st;
    readonly ScriptFile f;
    ScriptMeta meta;
    readonly Grid paramGrid = new() { Margin = new Thickness(0, 0, 0, 16) };
    readonly StackPanel warnPanel = new();
    /// <summary>1.1.2: the engine, mode and parameter boxes share the dialog width (about 230 px each for engine and
    /// mode), and some Russian texts are longer ("Интерпретатор: .venv · Python 3.12"). The selected text ends with an
    /// ellipsis instead of being cut off; the open list and the tooltip show it in full.</summary>
    static readonly DataTemplate TrimTemplate = MakeTrimTemplate();

    static DataTemplate MakeTrimTemplate()
    {
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding());
        tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var t = new DataTemplate { VisualTree = tb };
        t.Seal();
        return t;
    }

    internal static ComboBoxItem Item(string text, string tag) => new() { Content = text, Tag = tag, ContentTemplate = TrimTemplate };

    readonly ComboBox engineBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox modeBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(Gap, 0, 0, 0) };
    readonly CheckBox remember = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox presetBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button presetDelete = new();
    readonly Grid presetRow = new() { Margin = new Thickness(0, 0, 0, 16) };
    readonly TextBlock presetNote = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    bool presetLoading;
    readonly Dictionary<string, Func<object?>> readers = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, object> current = new(StringComparer.OrdinalIgnoreCase);
    bool loading;

    RunSettingsWindow(AppState st, ScriptFile f) : base(Loc.T("rsTitle") + " — " + f.Name, DialogWidth)
    {
        this.st = st;
        this.f = f;
        meta = st.Meta(f);
        ContentDialogLayout();
        var preset = RunPreset.Get(st.Settings, f.Rel);
        if (preset != null) foreach (var kv in preset.Values) current[kv.Key] = kv.Value;

        Body.Children.Add(Header());
        Body.Children.Add(PresetRow());

        // Parameters: no card, no title; absent when the script has none
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 180 });
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Body.Children.Add(paramGrid);

        // Engine | Mode
        var em = new Grid();
        em.ColumnDefinitions.Add(new ColumnDefinition());
        em.ColumnDefinitions.Add(new ColumnDefinition());
        em.Children.Add(engineBox);
        Grid.SetColumn(modeBox, 1);
        em.Children.Add(modeBox);
        Body.Children.Add(em);
        engineBox.SelectionChanged += (_, _) => OnEngine();
        foreach (var (tag, key) in new[] { ("inline", "rsModeNormal"), ("window", "rsModeWindow"), ("admin", "rsModeAdmin") })
            modeBox.Items.Add(Item(Loc.F("rsModeLabel", Loc.T(key)), tag));
        modeBox.ToolTip = Loc.T("rsModeTip");
        // The remembered mode, else the script's own line, else the default of Settings (1.2.0: a separate window)
        string mode = preset?.Mode ?? ScriptHeader.ModeOf(f) ?? st.Settings.DefaultMode;
        modeBox.SelectedIndex = mode == "admin" ? 2 : mode == "window" ? 1 : 0;

        // Last run: one secondary line, only when there is one
        var last = st.LastRunOf(f.Rel);
        var entry = st.History.LastOf(f.Rel);
        string lastText = last != null ? RunResult.LastRunView(last).Line : entry != null ? Loc.F("rsLastEntry", entry.Line2) : "";
        if (lastText.Length > 0)
        {
            var lt = new TextBlock { FontSize = 12, Margin = new Thickness(0, Gap, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            lt.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            lt.Inlines.Add(new Run(lastText));
            if (entry != null)
            {
                var hl = new Hyperlink(new Run(Loc.T("rsShowHistory")));
                hl.Click += (_, _) => { Result = "cancel"; Close(); App.Win?.ShowHistory(entry); };
                lt.Inlines.Add(new Run("  ·  "));
                lt.Inlines.Add(hl);
            }
            lt.ToolTip = lastText;
            Body.Children.Add(lt);
        }

        Body.Children.Add(warnPanel);

        // Button band: Remember on the left, Run (accent, default) and Cancel on the right
        remember.Content = Loc.T("rsRememberShort");
        remember.ToolTip = Loc.T("rsRemember") + "\n" + Loc.T("rsRememberTip");
        remember.IsChecked = preset != null;
        DockPanel.SetDock(remember, Dock.Left);
        Footer.Children.Insert(0, remember);
        AddButton("run", Loc.T("run"), true, true).ToolTip = Loc.T("rsRunTip");
        AddButton("cancel", Loc.T("cancel"), false, false, true);

        FillEngines();
        BuildParams();
        BuildWarnings();
        FillPresets(null);
    }

    // ---------- Parameter presets (1.2.0) ----------

    /// <summary>Preset [combo] [Save as…] [Delete]: named sets of the values below. The row is there for scripts with
    /// parameters (Python: the arguments line).</summary>
    UIElement PresetRow()
    {
        var sp = new StackPanel();
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 180 });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = Loc.T("presetLabel"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), ToolTip = Loc.T("presetTip") };
        presetRow.Children.Add(label);
        presetBox.ToolTip = Loc.T("presetTip");
        System.Windows.Automation.AutomationProperties.SetName(presetBox, Loc.T("presetLabel"));
        Grid.SetColumn(presetBox, 1);
        presetRow.Children.Add(presetBox);
        var save = new Button { Content = Loc.T("presetSaveAs"), MinHeight = Row, Margin = new Thickness(Gap, 0, 0, 0), ToolTip = Loc.T("presetTip") };
        save.Click += (_, _) => SavePreset();
        Grid.SetColumn(save, 2);
        presetRow.Children.Add(save);
        presetDelete.Content = Glyphs.Delete;
        presetDelete.ToolTip = Loc.T("presetDeleteTip");
        presetDelete.Margin = new Thickness(4, 0, 0, 0);
        presetDelete.SetResourceReference(StyleProperty, "IconButton");
        System.Windows.Automation.AutomationProperties.SetName(presetDelete, Loc.T("presetDeleteTip"));
        presetDelete.Click += (_, _) => DeletePreset();
        Grid.SetColumn(presetDelete, 3);
        presetRow.Children.Add(presetDelete);
        presetBox.SelectionChanged += (_, _) => OnPreset();
        sp.Children.Add(presetRow);
        presetNote.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        presetNote.Visibility = Visibility.Collapsed;
        presetRow.Margin = new Thickness(0);
        sp.Margin = new Thickness(0, 0, 0, 16);
        sp.Children.Add(presetNote);
        // No parameters: nothing to keep in a preset
        sp.Visibility = f.UsesArgs || meta.Params.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return sp;
    }

    string? SelectedPreset => (presetBox.SelectedItem as ComboBoxItem)?.Tag is string s && s.Length > 0 ? s : null;

    void FillPresets(string? select)
    {
        presetLoading = true;
        presetBox.Items.Clear();
        presetBox.Items.Add(Item(Loc.T("presetNone"), ""));
        foreach (var n in Presets.Names(st.Settings, f.Rel)) presetBox.Items.Add(Item(n, n));
        presetBox.SelectedItem = presetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, select ?? "", StringComparison.OrdinalIgnoreCase)) ?? presetBox.Items[0];
        presetBox.IsEnabled = presetBox.Items.Count > 1;
        presetDelete.IsEnabled = SelectedPreset != null;
        presetLoading = false;
    }

    /// <summary>A preset was picked: the form shows its values over the script's defaults (what was typed before goes);
    /// values of parameters the script no longer has are named in a note.</summary>
    void OnPreset()
    {
        presetDelete.IsEnabled = SelectedPreset != null;
        if (presetLoading) return;
        presetNote.Visibility = Visibility.Collapsed;
        if (SelectedPreset is not { } name || Presets.Get(st.Settings, f.Rel, name) is not { } p) return;
        var (values, ignored) = Presets.Apply(meta.Params, f.UsesArgs, p.Values);
        current = Presets.Merge(null, values);
        BuildParams();
        if (ignored.Count > 0)
        {
            presetNote.Text = Loc.F("presetIgnored", p.Name, string.Join(", ", ignored));
            presetNote.Visibility = Visibility.Visible;
        }
    }

    void SavePreset()
    {
        string? name = Dlg.Input(Loc.T("presetNameTitle"), Loc.T("presetNamePrompt"), SelectedPreset ?? "");
        name = Presets.CleanName(name);
        if (name == null) return;
        var existing = Presets.Get(st.Settings, f.Rel, name);
        if (existing != null && !Dlg.Confirm(Loc.T("presetNameTitle"), Loc.F("presetOverwrite", existing.Name), "", Loc.T("presetReplace"))) return;
        Presets.Save(st.Settings, f.Rel, existing?.Name ?? name, Values());
        st.NotifySettingsChanged();
        FillPresets(existing?.Name ?? name);
        presetNote.Visibility = Visibility.Collapsed;
        App.Status(Loc.F("presetSaved", existing?.Name ?? name));
    }

    void DeletePreset()
    {
        if (SelectedPreset is not { } name) return;
        int used = ScheduleList.UsingPreset(st.Settings, f.Rel, name).Count;
        if (!Dlg.Confirm(Loc.T("presetLabel"), Loc.F("presetDeleteHead", name), used > 0 ? Loc.F("presetDeleteSched", used) : "", Loc.T("delete"), true)) return;
        Presets.Delete(st.Settings, f.Rel, name);
        st.NotifySettingsChanged();
        FillPresets(null);
        presetNote.Visibility = Visibility.Collapsed;
        App.Status(Loc.F("presetDeleted", name));
    }

    /// <summary>The preset the values are exactly from, if any (a preset picked and then changed in the form is not it).</summary>
    string? PresetOf(Dictionary<string, object> vals)
    {
        if (SelectedPreset is not { } name || Presets.Get(st.Settings, f.Rel, name) is not { } p) return null;
        var (applied, _) = Presets.Apply(meta.Params, f.UsesArgs, p.Values);
        return RunPreset.ToLine(f.Rel, "inline", Normalize(applied)) == RunPreset.ToLine(f.Rel, "inline", vals) ? p.Name : null;
    }

    /// <summary>A preset's values as Values() would give them back from the form (strings of switches become bools, a
    /// switch at its default is left out).</summary>
    Dictionary<string, object> Normalize(Dictionary<string, object> applied)
    {
        var keep = current;
        current = Presets.Merge(null, applied);
        var saved = new Dictionary<string, Func<object?>>(readers, StringComparer.OrdinalIgnoreCase);
        readers.Clear();
        var res = Values();
        foreach (var kv in saved) readers[kv.Key] = kv.Value;
        current = keep;
        return res;
    }

    /// <summary>The script's own icon (its picture, else its glyph) at 24 px, the name (Subtitle, 20 semibold) and the
    /// description on one secondary line (trimmed; the full text in the tooltip).</summary>
    UIElement Header()
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var iconHost = new Grid { Width = 24, Height = 24, Margin = new Thickness(0, 4, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        var pic = IconUtil.Load(st.IconPath(f.Rel));
        if (pic != null) iconHost.Children.Add(new Image { Source = pic, Width = 24, Height = 24 });
        else
        {
            var g = new TextBlock { Text = ScriptHeader.GlyphOf(f), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            g.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            iconHost.Children.Add(g);
        }
        DockPanel.SetDock(iconHost, Dock.Left);
        dp.Children.Add(iconHost);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = f.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (meta.Desc.Length > 0)
        {
            var d = new TextBlock { Text = meta.Desc, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0), ToolTip = meta.Desc };
            d.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            sp.Children.Add(d);
        }
        dp.Children.Add(sp);
        return dp;
    }

    void FillEngines()
    {
        loading = true;
        engineBox.Items.Clear();
        if (f.IsPython)
        {
            // One interpreter, chosen in Settings: shown, not picked here
            // (the script's virtual environment first, like the run itself)
            var venv = st.VenvFor(f);
            var py = venv is { Broken: false } ? PythonInfo.FromVenv(venv) : venv == null ? st.Python : null;
            string label = venv is { Broken: true } ? Loc.F("pyVenvBrokenShort", venv.Name) : py != null ? py.RunLabel : Loc.T("pyMissingTitle");
            string pyText = Loc.F("rsPyEngine", label);
            engineBox.Items.Add(Item(pyText, "py"));
            engineBox.SelectedIndex = 0;
            engineBox.IsEnabled = false;
            engineBox.ToolTip = pyText + "\n" + (venv is { Broken: true } ? venv.Root : py != null ? py.Path : Loc.T("pyMarkMissing")) + "\n\n" + Loc.T("pyNoScan");
            ToolTipService.SetShowOnDisabled(engineBox, true);
            loading = false;
            return;
        }
        if (!f.IsPowerShell)
        {
            // Batch: cmd.exe; custom: the interpreter from Settings > Environment. Shown, not picked here
            var ci = f.IsCustom ? ScriptKinds.CustomFor(f.Path) : null;
            string text = Loc.F("rsPyEngine", f.IsBatch ? "cmd.exe" : ci?.Name ?? Loc.T("engCustom"));
            engineBox.Items.Add(Item(text, f.Lang));
            engineBox.SelectedIndex = 0;
            engineBox.IsEnabled = false;
            engineBox.ToolTip = f.IsBatch ? Loc.T("batMarkTip") + "\n\n" + Loc.T("batScanNote")
                : (ci == null ? Loc.F("customGone", f.Name) : ci.Program) + "\n\n" + Loc.T("customNoScan");
            ToolTipService.SetShowOnDisabled(engineBox, true);
            loading = false;
            return;
        }
        var auto = st.Meta(f, "auto");
        engineBox.Items.Add(Item(Loc.F("rsEngLabel", Loc.F("rsEngAutoShort", EngineShort(auto.Engine.Engine))), "auto"));
        engineBox.Items.Add(Item(Loc.F("rsEngLabel", "5.1") + (st.Ps51Ok ? "" : "  ·  " + Loc.T("p7None")), "5.1"));
        engineBox.Items.Add(Item(Loc.F("rsEngLabel", "7") + (st.PwshOk ? "" : "  ·  " + Loc.T("p7None")), "7"));
        string choice = st.Settings.GetEngineChoice(f.Rel);
        engineBox.SelectedIndex = choice == "5.1" ? 1 : choice == "7" ? 2 : 0;
        loading = false;
        UpdateEngineTip();
    }

    static string EngineShort(string engine) => engine == "7" ? "7" : "5.1";

    /// <summary>Why Auto picks what it picks, and the installed versions: a tooltip instead of a text line.</summary>
    void UpdateEngineTip()
    {
        if (!f.IsPowerShell) return;
        var ei = meta.Engine;
        string why = ei.Auto ? Loc.F("rsEngAuto", EngineInfo.Name(ei.Engine), ei.ReasonText()) : Loc.F("rsEngFixed", EngineInfo.Name(ei.Engine));
        string versions = Loc.T("eng7") + ": " + (st.PwshOk ? st.Pwsh!.Label : Loc.T("p7None")) + "\n" + Loc.T("eng51") + ": " + (st.Ps51Ok ? "✓" : Loc.T("p7None"));
        engineBox.ToolTip = why + "\n\n" + versions;
    }

    void OnEngine()
    {
        if (loading || engineBox.SelectedItem is not ComboBoxItem it) return;
        Collect();
        st.Settings.SetEngineChoice(f.Rel, (string)it.Tag);
        st.NotifySettingsChanged();
        st.NotifyScriptsChanged();
        App.Status(Loc.F("engSet", f.Name, it.Content));
        meta = st.Meta(f);
        UpdateEngineTip();
        BuildParams();
        BuildWarnings();
    }

    /// <summary>Label | box rows, 32 px controls 8 px apart. The default (or "set by the script", or the type) is the
    /// box's placeholder; a required parameter has an asterisk; type, default and help are in the tooltip.</summary>
    void BuildParams()
    {
        paramGrid.Children.Clear();
        paramGrid.RowDefinitions.Clear();
        readers.Clear();
        if (f.UsesArgs) { BuildPythonArgs(); return; }
        paramGrid.Visibility = meta.Params.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        int row = 0;
        foreach (var p in meta.Params)
        {
            paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = row == 0 ? 0 : Gap;
            string tip = RunForm.Tip(p);
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, top, 16, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tip };
            label.Inlines.Add(new Run(p.Name));
            if (p.Mandatory)
            {
                var star = new Run(" *") { ToolTip = Loc.T("rsRequired") };
                star.SetResourceReference(TextElement.ForegroundProperty, "SystemFillColorCriticalBrush");
                label.Inlines.Add(star);
            }
            Grid.SetRow(label, row);
            paramGrid.Children.Add(label);

            FrameworkElement cell;
            current.TryGetValue(p.Name, out var v);
            string name = p.Name;
            if (p.Kind == "switch" || p.Kind == "bool")
            {
                bool on = v != null ? IsTrue(v) : p.DefaultBool;
                var cb = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
                cell = new Grid { Height = Row, Children = { cb } };
                readers[name] = () => cb.IsChecked == true;
            }
            else if (p.SetValues.Count > 0)
            {
                var combo = new ComboBox { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = tip };
                combo.Items.Add(Item(p.Default != null ? Loc.F("detDefault", p.Default) : Loc.T("rsDefault"), ""));
                foreach (var sv in p.SetValues) combo.Items.Add(Item(sv, sv));
                string cur = Convert.ToString(v) ?? "";
                combo.SelectedIndex = Math.Max(0, p.SetValues.FindIndex(x => string.Equals(x, cur, StringComparison.OrdinalIgnoreCase)) + 1);
                cell = combo;
                readers[name] = () => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            }
            else
            {
                var tb = new TextBox { Text = Convert.ToString(v) ?? "", MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
                var ph = new TextBlock { Text = RunForm.Placeholder(p) ?? "", IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                ph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
                void Sync() => ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                tb.TextChanged += (_, _) => Sync();
                Sync();
                cell = new Grid { Children = { tb, ph } };
                readers[name] = () => tb.Text;
            }
            cell.Margin = new Thickness(0, top, 0, 0);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, 1);
            paramGrid.Children.Add(cell);
            row++;
        }
    }

    /// <summary>A Python script, a batch file or a custom interpreter's script has no param() block: one free text box whose
    /// text goes to sys.argv / %1 %2… / {args} (split like a command line).</summary>
    void BuildPythonArgs()
    {
        paramGrid.Visibility = Visibility.Visible;
        paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string tip = Loc.T(f.IsBatch ? "rsBatArgsTip" : f.IsCustom ? "rsCustomArgsTip" : "rsPyArgsTip");
        var label = new TextBlock { Text = Loc.T("rsPyArgs"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), ToolTip = tip };
        paramGrid.Children.Add(label);
        current.TryGetValue(PythonRun.ArgsKey, out var v);
        var tb = new TextBox { Text = Convert.ToString(v) ?? "", MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip, MaxLength = PythonRun.MaxArgsLength };
        System.Windows.Automation.AutomationProperties.SetName(tb, Loc.T("rsPyArgs"));
        var ph = new TextBlock { Text = Loc.T(f.IsBatch ? "rsBatArgsHint" : f.IsCustom ? "rsCustomArgsHint" : "rsPyArgsHint"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        ph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        void Sync() => ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => Sync();
        Sync();
        var cell = new Grid { Children = { tb, ph } };
        Grid.SetColumn(cell, 1);
        paramGrid.Children.Add(cell);
        readers[PythonRun.ArgsKey] = () => tb.Text.Trim();
        Loaded += (_, _) => tb.Focus();
    }

    /// <summary>Safety and compatibility notes, compact (an icon and one line each; line links into the editor).</summary>
    void BuildWarnings()
    {
        warnPanel.Children.Clear();
        var items = new List<(string Sev, string Text, int Line)>();
        foreach (var i in meta.Issues) items.Add((i.Level, i.Text(), 0));
        foreach (var d in meta.Danger) items.Add((d.Sev, d.Text, d.Line));
        warnPanel.Margin = new Thickness(0, items.Count > 0 ? Gap : 0, 0, 0);
        foreach (var (sev, text, line) in items)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var g = new TextBlock { Text = sev is "high" or "error" ? Glyphs.Error : sev == "info" ? Glyphs.Info : Glyphs.Warning, FontSize = 14, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            g.SetResourceReference(TextBlock.ForegroundProperty, sev is "high" or "error" ? "SystemFillColorCriticalBrush" : sev == "info" ? "AccentTextFillColorPrimaryBrush" : "SystemFillColorCautionBrush");
            DockPanel.SetDock(g, Dock.Left);
            dp.Children.Add(g);
            if (line > 0)
            {
                var hl = new Hyperlink(new Run(Loc.F("detLine", line))) { ToolTip = Loc.T("detLineTip") };
                int l = line;
                hl.Click += (_, _) => App.OpenEditor(f.Path, l);
                var lt = new TextBlock(hl) { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, FontSize = 12 };
                DockPanel.SetDock(lt, Dock.Right);
                dp.Children.Add(lt);
            }
            dp.Children.Add(Text(text, null, 12));
            warnPanel.Children.Add(dp);
        }
    }

    static bool IsTrue(object v) => v is bool b ? b : string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase);

    void Collect()
    {
        foreach (var (name, read) in readers)
        {
            var v = read();
            if (v is string s && s.Length == 0) current.Remove(name);
            else if (v != null) current[name] = v;
        }
    }

    Dictionary<string, object> Values()
    {
        Collect();
        var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (f.UsesArgs)
        {
            string a = PythonRun.ArgsFrom(current);
            if (a.Length > 0) res[PythonRun.ArgsKey] = a;
            return res;
        }
        foreach (var p in meta.Params)
        {
            if (!current.TryGetValue(p.Name, out var v)) continue;
            if (p.Kind is "switch" or "bool")
            {
                bool b = IsTrue(v);
                // A switch that stays at its default is not passed
                if (p.Kind == "switch" && b == p.DefaultBool) continue;
                res[p.Name] = b;
            }
            else res[p.Name] = v;
        }
        return res;
    }

    string Mode => (modeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "inline";

    /// <summary>Shows the dialog; null when cancelled. Saves (or forgets) the remembered settings.</summary>
    public static Answer? Ask(AppState st, ScriptFile f)
    {
        var w = new RunSettingsWindow(st, f);
        while (true)
        {
            string r = w.Run();
            if (r == "forget")
            {
                RunPreset.Remove(st.Settings, f.Rel);
                st.NotifySettingsChanged();
                App.Status(Loc.F("rsForgot", f.Name));
                return null;
            }
            if (r != "run") return null;
            var vals = w.Values();
            if (f.UsesArgs && !PythonRun.IsValidArgs(PythonRun.ArgsFrom(vals)))
            {
                Dlg.Message(Loc.T("paramTitle"), Loc.F("rsPyArgsBad", PythonRun.MaxArgsLength), Glyphs.Warning, "SystemFillColorCautionBrush");
                return null;
            }
            // Batch files (and custom interpreters that are batch files): " % ^ can't be passed through cmd.exe intact (see BatchRun.ArgError)
            if (!f.IsPython && ScriptLangs.ArgsError(f.Lang, PythonRun.ArgsFrom(vals), ScriptKinds.CustomFor(f.Path)) is { } bad)
            {
                Dlg.Message(Loc.T("paramTitle"), Loc.F(bad, PythonRun.MaxArgsLength), Glyphs.Warning, "SystemFillColorCautionBrush");
                return null;
            }
            var missing = RunArgs.MissingMandatory(w.meta.Params, vals);
            if (missing.Count > 0)
            {
                Dlg.Message(Loc.T("paramTitle"), Loc.F("paramMissing", string.Join(", ", missing)), Glyphs.Warning, "SystemFillColorCautionBrush");
                var again = new RunSettingsWindow(st, f);
                foreach (var kv in vals) again.current[kv.Key] = kv.Value;
                again.BuildParams();
                again.modeBox.SelectedIndex = w.modeBox.SelectedIndex;
                again.remember.IsChecked = w.remember.IsChecked;
                again.FillPresets(w.SelectedPreset);
                w = again;
                continue;
            }
            if (w.remember.IsChecked == true)
            {
                RunPreset.Set(st.Settings, f.Rel, w.Mode, vals);
                App.Status(Loc.F("rsSaved", f.Name));
            }
            else RunPreset.Remove(st.Settings, f.Rel);
            st.NotifySettingsChanged();
            return new Answer(w.Mode, vals, w.PresetOf(vals));
        }
    }
}
