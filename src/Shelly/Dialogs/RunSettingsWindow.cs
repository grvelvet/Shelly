using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Dialogs;

/// <summary>Run settings of a script, as a compact WinUI ContentDialog: the script's icon, name and description; a
/// two-column parameter grid (label | box, defaults as placeholders); engine and mode side by side; the last run in one
/// line; warnings when there are any; Remember + Run / Cancel in the button band.</summary>
public sealed class RunSettingsWindow : DialogWindow
{
    public sealed record Answer(string Mode, Dictionary<string, object> Values);

    public const double DialogWidth = 520;   // WinUI ContentDialog: 320..548
    const double Row = 32, Gap = 8;

    readonly AppState st;
    readonly ScriptFile f;
    ScriptMeta meta;
    readonly Grid paramGrid = new() { Margin = new Thickness(0, 0, 0, 16) };
    readonly StackPanel warnPanel = new();
    readonly ComboBox engineBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox modeBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(Gap, 0, 0, 0) };
    readonly CheckBox remember = new() { VerticalAlignment = VerticalAlignment.Center };
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
            modeBox.Items.Add(new ComboBoxItem { Content = Loc.F("rsModeLabel", Loc.T(key)), Tag = tag });
        modeBox.ToolTip = Loc.T("rsModeTip");
        string mode = preset?.Mode ?? "inline";
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
        var auto = st.Meta(f, "auto");
        engineBox.Items.Add(new ComboBoxItem { Content = Loc.F("rsEngLabel", Loc.F("rsEngAutoShort", EngineShort(auto.Engine.Engine))), Tag = "auto" });
        engineBox.Items.Add(new ComboBoxItem { Content = Loc.F("rsEngLabel", "5.1") + (st.Ps51Ok ? "" : "  ·  " + Loc.T("p7None")), Tag = "5.1" });
        engineBox.Items.Add(new ComboBoxItem { Content = Loc.F("rsEngLabel", "7") + (st.PwshOk ? "" : "  ·  " + Loc.T("p7None")), Tag = "7" });
        string choice = st.Settings.GetEngineChoice(f.Rel);
        engineBox.SelectedIndex = choice == "5.1" ? 1 : choice == "7" ? 2 : 0;
        loading = false;
        UpdateEngineTip();
    }

    static string EngineShort(string engine) => engine == "7" ? "7" : "5.1";

    /// <summary>Why Auto picks what it picks, and the installed versions: a tooltip instead of a text line.</summary>
    void UpdateEngineTip()
    {
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
                combo.Items.Add(new ComboBoxItem { Content = p.Default != null ? Loc.F("detDefault", p.Default) : Loc.T("rsDefault"), Tag = "" });
                foreach (var sv in p.SetValues) combo.Items.Add(new ComboBoxItem { Content = sv, Tag = sv });
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
            var missing = RunArgs.MissingMandatory(w.meta.Params, vals);
            if (missing.Count > 0)
            {
                Dlg.Message(Loc.T("paramTitle"), Loc.F("paramMissing", string.Join(", ", missing)), Glyphs.Warning, "SystemFillColorCautionBrush");
                var again = new RunSettingsWindow(st, f);
                foreach (var kv in vals) again.current[kv.Key] = kv.Value;
                again.BuildParams();
                again.modeBox.SelectedIndex = w.modeBox.SelectedIndex;
                again.remember.IsChecked = w.remember.IsChecked;
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
            return new Answer(w.Mode, vals);
        }
    }
}
