using System.Windows;
using System.Windows.Controls;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Dialogs;

/// <summary>Compact Windows 11 card-style dialog for choosing the default editor and the external editor program (1.4.0 / 1.4.1).</summary>
public sealed class EditorChoiceDialog : DialogWindow
{
    const double Row = 32, Gap = 8, ComboMinWidth = 190;
    static AppState St => App.S;

    readonly ComboBox defCombo = new() { MinHeight = Row, MinWidth = ComboMinWidth, MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox extCombo = new() { MinHeight = Row, MinWidth = ComboMinWidth, MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel customPanel = new() { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBox pathBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 1000 };
    readonly TextBox argsBox = new() { MinHeight = Row, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 500, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    readonly TextBlock error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, Gap, 0, 0) };
    public bool Saved { get; private set; }

    EditorChoiceDialog() : base(Loc.T("edChoiceTitle"), 500)
    {
        ContentDialogLayout();
        AddHeader(Glyphs.Edit, "AccentTextFillColorPrimaryBrush", Loc.T("edChoiceTitle"), Loc.T("setEditorNote"));

        // 1. Default editor card row
        defCombo.Items.Add(RunSettingsWindow.Item(Loc.T("edChoiceBuiltin"), "builtin"));
        defCombo.Items.Add(RunSettingsWindow.Item(Loc.T("edChoiceExternal"), "external"));
        SelectTag(defCombo, St.Settings.DefaultEditor);
        System.Windows.Automation.AutomationProperties.SetName(defCombo, Loc.T("edChoiceDefault"));
        Body.Children.Add(OptionCard(Loc.T("edChoiceDefault"), Loc.T("edChoiceDefaultNote"), defCombo, null));

        // 2. External editor program card row (+ collapsible custom panel inside the card)
        var detected = ExternalEditors.DetectAll();
        var autoItem = RunSettingsWindow.Item(Loc.T("edAuto"), "auto");
        autoItem.ToolTip = Loc.T("edChoiceProgramNote");
        extCombo.Items.Add(autoItem);
        foreach (var ed in detected)
        {
            string status = ed.Installed ? Loc.T("edDetected") : Loc.T("edNotInstalled");
            var item = RunSettingsWindow.Item(ed.Name + " (" + status + ")", ed.Id);
            if (ed.Installed && ed.ExePath.Length > 0) item.ToolTip = ed.ExePath;
            extCombo.Items.Add(item);
        }
        extCombo.Items.Add(RunSettingsWindow.Item(Loc.T("edCustom"), "custom"));
        SelectTag(extCombo, St.Settings.ExternalEditor);
        System.Windows.Automation.AutomationProperties.SetName(extCombo, Loc.T("edChoiceProgram"));
        extCombo.SelectionChanged += (_, _) => SyncCustomVisibility();

        // 3. Custom executable + args inside the second card
        var pathLbl = Text(Loc.T("edCustomPath"), "TextFillColorSecondaryBrush", 12);
        pathLbl.Margin = new Thickness(0, 0, 0, 4);
        customPanel.Children.Add(pathLbl);

        pathBox.Text = St.Settings.ExternalEditorPath;
        var browse = new Button { Content = Loc.T("ciBrowse"), MinWidth = 0, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(Gap, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var progDock = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        progDock.Children.Add(browse);
        progDock.Children.Add(pathBox);
        customPanel.Children.Add(progDock);

        var argsLbl = Text(Loc.T("edCustomArgs"), "TextFillColorSecondaryBrush", 12);
        argsLbl.Margin = new Thickness(0, 8, 0, 4);
        customPanel.Children.Add(argsLbl);

        argsBox.Text = St.Settings.ExternalEditorArgs.Length > 0 ? St.Settings.ExternalEditorArgs : "{file}";
        argsBox.ToolTip = Loc.T("edCustomArgsHint");
        customPanel.Children.Add(argsBox);

        var argsHint = Text(Loc.T("edCustomArgsHint"), "TextFillColorSecondaryBrush", 12);
        argsHint.Margin = new Thickness(0, 4, 0, 0);
        customPanel.Children.Add(argsHint);

        Body.Children.Add(OptionCard(Loc.T("edChoiceProgram"), Loc.T("edChoiceProgramNote"), extCombo, customPanel));
        SyncCustomVisibility();

        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        Body.Children.Add(error);

        var save = new Button { Content = Loc.T("ciSave"), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += (_, _) => TrySave();
        Buttons.Children.Add(save);
        AddButton("cancel", Loc.T("cancel"), false, false, true);
    }

    static Border OptionCard(string title, string desc, UIElement right, UIElement? below)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        var d = new TextBlock { Text = desc, Margin = new Thickness(0, 2, 0, 0) };
        d.SetResourceReference(StyleProperty, "Caption");
        sp.Children.Add(d);
        Grid.SetColumn(sp, 0);
        g.Children.Add(sp);

        Grid.SetColumn(right, 1);
        g.Children.Add(right);

        if (below != null)
        {
            Grid.SetRow(below, 1);
            Grid.SetColumn(below, 0);
            Grid.SetColumnSpan(below, 2);
            g.Children.Add(below);
        }

        var card = new Border { Child = g, Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(StyleProperty, "Card");
        return card;
    }

    static void SelectTag(ComboBox cb, string tag)
    {
        foreach (ComboBoxItem item in cb.Items)
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                cb.SelectedItem = item;
                return;
            }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    static string SelectedTag(ComboBox cb, string fallback) =>
        (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    void SyncCustomVisibility()
    {
        bool isCustom = SelectedTag(extCombo, "auto") == "custom";
        customPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        error.Visibility = Visibility.Collapsed;
    }

    void Browse()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("ciExeFilter"), Title = Loc.T("ciPick"), CheckFileExists = true };
        try
        {
            string p = pathBox.Text.Trim().Trim('"');
            if (p.Length > 0 && Path.GetDirectoryName(p) is { Length: > 0 } dir && Directory.Exists(dir)) d.InitialDirectory = dir;
        }
        catch { }
        if (d.ShowDialog(this) == true) pathBox.Text = d.FileName;
    }

    void TrySave()
    {
        string defChoice = SelectedTag(defCombo, "builtin");
        string extChoice = SelectedTag(extCombo, "auto");
        string customPath = pathBox.Text.Trim().Trim('"').Trim();
        string customArgs = argsBox.Text.Trim();

        if (extChoice == "custom")
        {
            if (customPath.Length == 0 || !File.Exists(customPath))
            {
                error.Text = Loc.T("ciErrProgram");
                error.Visibility = Visibility.Visible;
                return;
            }
        }

        St.Settings.DefaultEditor = defChoice;
        St.Settings.ExternalEditor = extChoice;
        St.Settings.ExternalEditorPath = customPath;
        St.Settings.ExternalEditorArgs = customArgs;
        St.NotifySettingsChanged();
        Saved = true;
        Close();
    }

    public static bool ShowDialog(Window? owner = null)
    {
        var d = new EditorChoiceDialog();
        if (owner != null && owner.IsLoaded) d.Owner = owner;
        ((Window)d).ShowDialog();
        return d.Saved;
    }
}
