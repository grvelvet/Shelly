using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Dialogs;

/// <summary>"Schedule…" (1.2.0): when (once, daily, days of the week, at Windows sign-in, every N minutes), which preset,
/// hidden or in a window, on/off. Saved to the settings first (the source of truth), then to Task Scheduler. Scripts that
/// run as administrator can't be scheduled; findings and Mark of the Web are shown, because a scheduled run that would
/// have to ask is skipped.</summary>
public sealed class ScheduleDialog : DialogWindow
{
    const double Row = 32, Gap = 8;
    static AppState St => App.S;

    readonly ScriptFile f;
    readonly Schedule? existing;
    readonly Grid grid = new();
    readonly ComboBox kindBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox dateBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBox timeBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 5 };
    readonly WrapPanel daysPanel = new();
    readonly Dictionary<string, CheckBox> dayBoxes = new();
    readonly TextBox everyBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, Width = 80, MaxLength = 4 };
    readonly ComboBox presetBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox howBox = new() { MinHeight = Row, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox enabledBox = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, Gap, 0, 0) };
    readonly Dictionary<string, UIElement[]> rows = new();
    public Schedule? Saved { get; private set; }

    static CultureInfo Culture => CultureInfo.GetCultureInfo(Loc.Language == "ru" ? "ru-RU" : "en-US");

    ScheduleDialog(ScriptFile f, Schedule? s) : base(Loc.T("schedTitle") + " — " + f.Name, 520)
    {
        this.f = f;
        existing = s;
        ContentDialogLayout();
        AddHeader(Glyphs.Clock, "AccentTextFillColorPrimaryBrush", Loc.T("schedTitle"), f.Name);
        var cur = s?.Clone() ?? new Schedule { Rel = f.Rel, Kind = "daily", Time = "09:00", Enabled = true };

        var meta = St.Meta(f);
        string? explicitMode = Core.RunPreset.Get(St.Settings, f.Rel)?.Mode ?? ScriptHeader.ModeOf(f);
        bool allowed = ScheduleGate.CanSchedule(explicitMode, f.IsPowerShell && meta.Req.Elevation);

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 180 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Body.Children.Add(grid);

        foreach (var (k, key) in new[] { ("once", "schedKindOnce"), ("daily", "schedKindDaily"), ("weekly", "schedKindWeekly"), ("logon", "schedKindLogon"), ("interval", "schedKindInterval") })
            kindBox.Items.Add(RunSettingsWindow.Item(Loc.T(key), k));
        kindBox.SelectedItem = kindBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur.Kind) ?? kindBox.Items[1];
        AddRow("kind", Loc.T("schedWhen"), kindBox);

        var start = cur.Start ?? DateTime.Today.AddDays(1) + (Schedule.TryTime(cur.Time, out var t0) ? t0 : TimeSpan.FromHours(9));
        dateBox.Text = start.ToString("d", Culture);
        dateBox.ToolTip = Loc.F("schedDateHint", DateTime.Today.ToString("d", Culture));
        AddRow("date", Loc.T("schedDate"), dateBox);
        timeBox.Text = cur.Time;
        timeBox.ToolTip = Loc.T("schedTimeHint");
        AddRow("time", Loc.T("schedTime"), timeBox);

        foreach (var d in Schedule.DayKeys)
        {
            var cb = new CheckBox { Content = Culture.DateTimeFormat.GetAbbreviatedDayName(DowOf(d)), IsChecked = cur.Days.Contains(d), Margin = new Thickness(0, 4, 12, 4), MinWidth = 0 };
            dayBoxes[d] = cb;
            daysPanel.Children.Add(cb);
        }
        AddRow("days", Loc.T("schedDays"), daysPanel);

        everyBox.Text = cur.Every.ToString(CultureInfo.InvariantCulture);
        var unit = new TextBlock { Text = Loc.T("schedEveryUnit"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Gap, 0, 0, 0) };
        AddRow("every", Loc.T("schedEvery"), new StackPanel { Orientation = Orientation.Horizontal, Children = { everyBox, unit } });

        presetBox.Items.Add(RunSettingsWindow.Item(Loc.T("schedNoPreset"), ""));
        foreach (var n in Presets.Names(St.Settings, f.Rel)) presetBox.Items.Add(RunSettingsWindow.Item(n, n));
        // A preset that is gone stays visible, so the user sees why the schedule is skipped
        if (cur.Preset.Length > 0 && !Presets.Exists(St.Settings, f.Rel, cur.Preset))
            presetBox.Items.Add(RunSettingsWindow.Item(cur.Preset + " · " + Loc.T("presetGone"), cur.Preset));
        presetBox.SelectedItem = presetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, cur.Preset, StringComparison.OrdinalIgnoreCase)) ?? presetBox.Items[0];
        presetBox.ToolTip = Loc.T("schedNoPresetTip") + "\n\n" + Loc.T("presetTip");
        AddRow("preset", Loc.T("schedPreset"), presetBox);

        howBox.Items.Add(RunSettingsWindow.Item(Loc.T("schedHidden"), "inline"));
        howBox.Items.Add(RunSettingsWindow.Item(Loc.T("schedWindow"), "window"));
        howBox.SelectedIndex = cur.Window ? 1 : 0;
        AddRow("how", Loc.T("schedHow"), howBox);

        enabledBox.SetResourceReference(StyleProperty, "ToggleSwitch");
        enabledBox.IsChecked = cur.Enabled;
        System.Windows.Automation.AutomationProperties.SetName(enabledBox, Loc.T("schedEnabled"));
        AddRow("enabled", Loc.T("schedEnabled"), new Grid { Height = Row, Children = { enabledBox } });

        // What makes scheduled runs of this script skip (they can't ask)
        var notes = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        void Note(string text, string brush, string glyph)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, Gap) };
            var g = new TextBlock { Text = glyph, FontSize = 14, Margin = new Thickness(0, 2, Gap, 0), VerticalAlignment = VerticalAlignment.Top };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            g.SetResourceReference(TextBlock.ForegroundProperty, brush);
            DockPanel.SetDock(g, Dock.Left);
            dp.Children.Add(g);
            dp.Children.Add(Text(text, null, 12));
            notes.Children.Add(dp);
        }
        if (!allowed) Note(Loc.T("schedAdminNo"), "SystemFillColorCriticalBrush", Glyphs.Shield);
        else
        {
            if (Safety.Level(meta.Danger) == "high") Note(Loc.T("schedWarnDanger"), "SystemFillColorCriticalBrush", Glyphs.Error);
            else if (Safety.Needed(St.Settings, meta.Danger, "inline", f.Rel, f.Path, f.Hash)) Note(Loc.T("schedWarnConfirm"), "SystemFillColorCautionBrush", Glyphs.Warning);
            if (meta.Zone is { Blocks: true }) Note(Loc.T("schedWarnMotw"), "SystemFillColorCautionBrush", Glyphs.Warning);
        }
        var info = Text(Loc.T("schedNote") + " " + Loc.T("schedWarnChange"), "TextFillColorSecondaryBrush", 12);
        notes.Children.Add(info);
        Body.Children.Add(notes);
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        Body.Children.Add(error);

        // Buttons: Delete on the left (existing schedules), Save (accent) and Cancel on the right
        if (s != null) AddLeftButton("delete", Loc.T("schedDelete"));
        var save = new Button { Content = Loc.T("schedSave"), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = allowed };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += (_, _) => TrySave();
        Buttons.Children.Add(save);
        AddButton("cancel", Loc.T("cancel"), false, false, true);

        kindBox.SelectionChanged += (_, _) => UpdateRows();
        UpdateRows();
    }

    static DayOfWeek DowOf(string key) => (DayOfWeek)((Array.IndexOf(Schedule.DayKeys, key) + 1) % 7);

    void AddRow(string id, string label, FrameworkElement control)
    {
        int r = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = r == 0 ? 0 : Gap;
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, top, 16, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(l, r);
        grid.Children.Add(l);
        control.Margin = new Thickness(control.Margin.Left, top, control.Margin.Right, control.Margin.Bottom);
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        Grid.SetRow(control, r);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        rows[id] = new UIElement[] { l, control };
    }

    string Kind => (kindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "daily";

    void UpdateRows()
    {
        string k = Kind;
        void Show(string id, bool on) { foreach (var e in rows[id]) e.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
        Show("date", k == "once");
        Show("time", k is "once" or "daily" or "weekly");
        Show("days", k == "weekly");
        Show("every", k == "interval");
        error.Visibility = Visibility.Collapsed;
    }

    Schedule Collect()
    {
        var s = existing?.Clone() ?? new Schedule { Id = Schedule.NewId(), Rel = f.Rel };
        s.Rel = f.Rel;
        s.Kind = Kind;
        s.Time = Schedule.NormalizeTime(timeBox.Text) ?? timeBox.Text.Trim();
        s.Date = DateTime.TryParse(dateBox.Text.Trim(), Culture, DateTimeStyles.AllowWhiteSpaces, out var d) || DateTime.TryParse(dateBox.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        s.Days = Schedule.DayKeys.Where(k => dayBoxes[k].IsChecked == true).ToList();
        s.Every = int.TryParse(everyBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        s.Preset = (presetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        s.Window = howBox.SelectedIndex == 1;
        s.Enabled = enabledBox.IsChecked == true;
        // Only what the kind uses is kept
        if (s.Kind != "once") s.Date = "";
        if (s.Kind is "logon" or "interval") s.Time = s.Kind == "interval" ? "" : s.Time;
        if (s.Kind != "weekly") s.Days = new();
        return s;
    }

    void TrySave()
    {
        var s = Collect();
        string? err = s.Error(DateTime.Now);
        if (err != null)
        {
            error.Text = Loc.T(err);
            error.Visibility = Visibility.Visible;
            return;
        }
        Saved = s;
        Result = "save";
        DialogResult = true;
    }

    /// <summary>Opens the dialog for a new schedule (existing = null) or to edit one, and applies the result.</summary>
    public static async void Show(ScriptFile f, Schedule? existing)
    {
        try
        {
            var d = new ScheduleDialog(f, existing);
            string r = d.Run();
            var svc = App.Sched;
            if (r == "delete" && existing != null)
            {
                if (!Dlg.Confirm(Loc.T("schedTitle"), Loc.T("schedDeleteHead"), Loc.F("schedDeleteText", f.Name, existing.Describe()), Loc.T("schedDelete"), true, Glyphs.Delete)) return;
                await DeleteAsync(existing, f.Name);
                return;
            }
            if (r != "save" || d.Saved == null) return;
            var s = d.Saved;
            ScheduleList.Put(St.Settings, s);
            St.SaveSettingsNow();   // before the task exists: it reads the settings when it starts
            St.NotifySettingsChanged();
            St.NotifyScriptsChanged();
            string? fail = svc != null ? await svc.ApplyAsync(s) : "Task Scheduler";
            if (fail != null)
            {
                Dlg.Message(Loc.T("schedTitle"), Loc.F("schedTaskFail", fail), Glyphs.Warning, "SystemFillColorCautionBrush");
                if (svc != null) _ = svc.RefreshAsync();
            }
            else App.Status(Loc.F("schedSaved", f.Name, s.Describe()));
            St.NotifySettingsChanged();
        }
        catch (Exception ex) { AppLog.Error("schedule", ex); App.Status(Loc.F("unexpected", ex.Message), true); }
    }

    /// <summary>Removes a schedule from the settings and its task from Task Scheduler.</summary>
    public static async Task DeleteAsync(Schedule s, string name)
    {
        ScheduleList.Remove(St.Settings, s.Id);
        St.SaveSettingsNow();
        St.NotifySettingsChanged();
        St.NotifyScriptsChanged();
        string? fail = App.Sched != null ? await App.Sched.DeleteAsync(s.Id) : null;
        if (fail != null) App.Status(Loc.F("schedTaskDelFail", fail), true);
        else App.Status(Loc.F("schedDeleted", name, s.Describe()));
        St.NotifySettingsChanged();
    }
}
