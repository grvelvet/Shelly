using System.Windows;
using System.Windows.Controls;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Dialogs;

/// <summary>Settings > Environment > Custom interpreters > Add / Edit (1.3.0): name, extensions, program (Browse…),
/// arguments with {script} / {args} / {dir}, and the working folder. Checked with CustomInterpreters.Validate before it is
/// saved: built-in extensions (.ps1 .py .bat .cmd .exe…) are refused, the program must exist.</summary>
public sealed class CustomInterpreterDialog : DialogWindow
{
    const double Row = 32, Gap = 8;
    static AppState St => App.S;

    readonly CustomInterpreter cur;
    readonly Grid grid = new();
    readonly TextBox nameBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = CustomInterpreter.MaxName };
    readonly TextBox extBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 200 };
    readonly TextBox programBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 1000 };
    readonly TextBox templateBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 1000 };
    readonly CheckBox scriptDirBox = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, Gap, 0, 0) };
    public CustomInterpreter? Saved { get; private set; }

    CustomInterpreterDialog(CustomInterpreter? existing) : base(Loc.T(existing == null ? "ciTitleNew" : "ciTitleEdit"), 560)
    {
        cur = existing?.Clone() ?? new CustomInterpreter { Id = CustomInterpreters.NewId() };
        ContentDialogLayout();
        AddHeader(Glyphs.Script, "AccentTextFillColorPrimaryBrush", Loc.T(existing == null ? "ciTitleNew" : "ciTitleEdit"), existing?.Name ?? "");

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 180 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Body.Children.Add(grid);

        nameBox.Text = cur.Name;
        AddRow(Loc.T("ciName"), WithHint(nameBox, Loc.T("ciNameHint")));
        extBox.Text = cur.ExtText;
        AddRow(Loc.T("ciExt"), WithHint(extBox, Loc.T("ciExtHint")));
        programBox.Text = cur.Program;
        programBox.ToolTip = Loc.T("ciProgramHint");
        var browse = new Button { Content = Loc.T("ciBrowse"), MinWidth = 0, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(Gap, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var prog = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        prog.Children.Add(browse);
        prog.Children.Add(WithHint(programBox, Loc.T("ciProgramHint")));
        AddRow(Loc.T("ciProgram"), prog);
        templateBox.Text = cur.Template;
        templateBox.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        templateBox.ToolTip = Loc.T("ciTemplateHint");
        AddRow(Loc.T("ciTemplate"), templateBox);
        var hint = Text(Loc.T("ciTemplateHint"), "TextFillColorSecondaryBrush", 12);
        hint.Margin = new Thickness(0, 4, 0, 0);
        Grid.SetRow(hint, grid.RowDefinitions.Count);
        Grid.SetColumn(hint, 1);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(hint);
        scriptDirBox.Content = Loc.T("ciScriptDir");
        scriptDirBox.IsChecked = cur.ScriptDir;
        scriptDirBox.Margin = new Thickness(0, 12, 0, 0);
        Body.Children.Add(scriptDirBox);

        // What Shelly does not do for these scripts
        var note = Text(Loc.T("ciNote"), "TextFillColorSecondaryBrush", 12);
        note.Margin = new Thickness(0, 16, 0, 0);
        Body.Children.Add(note);
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        Body.Children.Add(error);

        var save = new Button { Content = Loc.T("ciSave"), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += (_, _) => TrySave();
        Buttons.Children.Add(save);
        AddButton("cancel", Loc.T("cancel"), false, false, true);
        Loaded += (_, _) => nameBox.Focus();
    }

    /// <summary>A text box with a grey placeholder while it is empty.</summary>
    static Grid WithHint(TextBox tb, string hint)
    {
        var ph = new TextBlock { Text = hint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        ph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        void Sync() => ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => Sync();
        Sync();
        return new Grid { Children = { tb, ph } };
    }

    void AddRow(string label, FrameworkElement control)
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
    }

    void Browse()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("ciExeFilter"), Title = Loc.T("ciPick"), CheckFileExists = true };
        try
        {
            string p = programBox.Text.Trim().Trim('"');
            if (p.Length > 0 && Path.GetDirectoryName(p) is { Length: > 0 } dir && Directory.Exists(dir)) d.InitialDirectory = dir;
        }
        catch { }
        if (d.ShowDialog(this) == true) programBox.Text = d.FileName;
    }

    void Fail(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }

    void TrySave()
    {
        var c = cur.Clone();
        c.Name = nameBox.Text.Trim();
        var exts = CustomInterpreters.ParseExtensions(extBox.Text, out string bad);
        if (exts == null) { Fail(Loc.T("ciErrExt") + " " + bad); return; }
        c.Extensions = exts;
        c.Program = programBox.Text.Trim().Trim('"').Trim();
        c.Template = templateBox.Text.Trim().Length == 0 ? CustomInterpreter.DefaultTemplate : templateBox.Text.Trim();
        c.ScriptDir = scriptDirBox.IsChecked == true;
        var others = CustomInterpreters.Load(St.Settings);
        if (others.Count(o => o.Id != c.Id) >= CustomInterpreters.MaxEntries) { Fail(Loc.F("ciErrMax", CustomInterpreters.MaxEntries)); return; }
        var err = CustomInterpreters.Validate(c, others, File.Exists, AnalysisService.PathDirs());
        if (err is { } e)
        {
            // ciErrToken / ciErrNoScript name {script} literally: not a format string
            Fail(e.Key is "ciErrToken" or "ciErrNoScript" or "ciErrTemplate" ? Loc.T(e.Key) : Loc.F(e.Key, e.Detail));
            return;
        }
        Saved = c;
        Result = "save";
        DialogResult = true;
    }

    /// <summary>Opens the dialog (existing = null: a new interpreter) and saves the result; true when something was saved.</summary>
    public static bool Show(CustomInterpreter? existing)
    {
        var d = new CustomInterpreterDialog(existing);
        if (d.Run() != "save" || d.Saved == null) return false;
        CustomInterpreters.Put(St.Settings, d.Saved);
        St.CustomInterpretersChanged();
        App.Status(Loc.F("ciSaved", d.Saved.Name));
        return true;
    }
}
