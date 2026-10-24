using System.Windows;
using System.Windows.Controls;
using Shelly.Core;

namespace Shelly.Dialogs;

/// <summary>"Import folder…": the name of the new folder in Scripts (checked as you type), the scripts found with a tick each
/// (all ticked; unticked ones are copied but not listed), and how many files and bytes will be copied.</summary>
public static class ImportFolderDialog
{
    public sealed record Answer(string Name, List<string> Hidden);

    public static Answer? Show(FolderImport.Plan plan, string scriptsDir, string suggested)
    {
        var d = new DialogWindow(Loc.T("fiTitle"), 540);
        d.AddHeader(Glyphs.Folder, "AccentTextFillColorPrimaryBrush", Path.GetFileName(plan.Source), plan.Source);

        d.Body.Children.Add(DialogWindow.Text(Loc.T("fiName"), "TextFillColorSecondaryBrush", 13));
        var name = new TextBox { Text = suggested, Margin = new Thickness(0, 8, 0, 0), MinHeight = 32, MaxLength = 100 };
        System.Windows.Automation.AutomationProperties.SetName(name, Loc.T("fiName"));
        d.Body.Children.Add(name);
        var nameErr = DialogWindow.Text("", "SystemFillColorCriticalBrush", 12);
        nameErr.Margin = new Thickness(0, 4, 0, 0);
        d.Body.Children.Add(nameErr);

        var head = DialogWindow.Text(Loc.F("fiScripts", plan.Scripts.Count), "TextFillColorSecondaryBrush", 13);
        head.Margin = new Thickness(0, 12, 0, 8);
        d.Body.Children.Add(head);
        var boxes = new List<(CheckBox Box, string Rel)>();
        var list = new StackPanel();
        foreach (var rel in plan.Scripts)
        {
            var cb = new CheckBox { Content = rel, IsChecked = true, Margin = new Thickness(0, 2, 0, 2), MinWidth = 0 };
            boxes.Add((cb, rel));
            list.Children.Add(cb);
        }
        var sv = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Shelly.Views.ScrollGutter.SetEnabled(sv, true);
        d.Body.Children.Add(DialogWindow.Card(sv));

        string summary = Loc.F("fiSummary", plan.Files.Count, FolderImport.SizeText(plan.Bytes));
        if (plan.SkippedFolders + plan.SkippedLinks > 0) summary += " " + Loc.F("fiSkipped", plan.SkippedFolders, plan.SkippedLinks);
        var sum = DialogWindow.Text(summary, "TextFillColorSecondaryBrush", 12);
        sum.Margin = new Thickness(0, 12, 0, 0);
        d.Body.Children.Add(sum);

        var ok = d.AddButton("ok", Loc.T("fiImport"), true, true);
        d.AddButton("cancel", Loc.T("cancel"), false, false, true);
        void Validate()
        {
            string err = FolderImport.CheckName(scriptsDir, name.Text, out _);
            nameErr.Text = err.Length == 0 ? "" : Loc.T(err == "fiExists" ? "fiExists" : err);
            nameErr.Visibility = err.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            bool any = boxes.Any(b => b.Box.IsChecked == true);
            if (err.Length == 0 && !any) { nameErr.Text = Loc.T("fiNoneTicked"); nameErr.Visibility = Visibility.Visible; }
            ok.IsEnabled = err.Length == 0 && any;
        }
        name.TextChanged += (_, _) => Validate();
        foreach (var (b, _) in boxes) { b.Checked += (_, _) => Validate(); b.Unchecked += (_, _) => Validate(); }
        Validate();
        d.ContentRendered += (_, _) => { name.Focus(); name.SelectAll(); };
        if (d.Run() != "ok") return null;
        FolderImport.CheckName(scriptsDir, name.Text, out string clean);
        return new Answer(clean, boxes.Where(b => b.Box.IsChecked != true).Select(b => b.Rel).ToList());
    }
}
