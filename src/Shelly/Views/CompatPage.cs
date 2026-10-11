using System.Windows;
using System.Windows.Controls;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>Compatibility of this PC: Windows, .NET, PowerShell 5.1 and 7 (with install), policy, compiler, hotkey, tray.</summary>
public sealed class CompatPage : UserControl, IPage
{
    readonly StackPanel body = new() { Margin = new Thickness(36, 24, 36, 36), MaxWidth = 1000 };

    public CompatPage()
    {
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loc.LanguageChanged += Refresh;
        App.S.EnginesChanged += Refresh;
        PwshInstall.WatchChanged += Refresh;
    }

    public void Activated() => Refresh();

    public void Refresh()
    {
        body.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var again = new Button { Content = Loc.T("p7Check"), MinWidth = 120 };
        again.Click += (_, _) => { App.S.RecheckEngines(); Refresh(); };
        DockPanel.SetDock(again, Dock.Right);
        head.Children.Add(again);
        var title = new TextBlock { Text = Loc.T("compatTitle") };
        title.SetResourceReference(StyleProperty, "PageTitle");
        head.Children.Add(title);
        body.Children.Add(head);
        var ver = new TextBlock { Text = Loc.F("compatVersion", AppState.Version), Margin = new Thickness(0, 0, 0, 16) };
        ver.SetResourceReference(StyleProperty, "Caption");
        body.Children.Add(ver);

        var items = Compat.Check();
        int warn = items.Count(i => i.Level is "warn" or "error");
        var sum = new TextBlock { Text = warn == 0 ? Loc.T("compatAllOk") : Loc.F("compatSummary", string.Join(", ", items.Where(i => i.Level is "warn" or "error").Select(i => i.Title))), Margin = new Thickness(0, 0, 0, 12) };
        sum.SetResourceReference(StyleProperty, "Body");
        body.Children.Add(sum);

        foreach (var it in items)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = new TextBlock
            {
                Text = it.Level switch { "ok" => "\uEC61", "error" => "\uEB90", "warn" => "\uE814", _ => "\uE946" },
                FontSize = 18, VerticalAlignment = VerticalAlignment.Center
            };
            ic.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            ic.SetResourceReference(TextBlock.ForegroundProperty, it.Level switch
            {
                "ok" => "SystemFillColorSuccessBrush", "error" => "SystemFillColorCriticalBrush", "warn" => "SystemFillColorCautionBrush", _ => "AccentTextFillColorPrimaryBrush"
            });
            g.Children.Add(ic);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            sp.Children.Add(new TextBlock { Text = it.Title, FontSize = 14 });
            var d = new TextBlock { Text = it.Text };
            d.SetResourceReference(StyleProperty, "Caption");
            sp.Children.Add(d);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            if (it.Key == "pwsh" && !App.S.PwshOk)
            {
                var b = new Button { Content = Loc.T("p7Install"), VerticalAlignment = VerticalAlignment.Center };
                b.SetResourceReference(StyleProperty, "AccentButtonStyle");
                b.Click += (_, _) => App.InstallPwsh();
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
            }
            if (it.Key == "hotkey")
            {
                var b = new Button { Content = Loc.T("navSettings"), VerticalAlignment = VerticalAlignment.Center };
                b.Click += (_, _) => App.Win?.Navigate("settings");
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
            }
            var card = new Border { Child = g };
            card.SetResourceReference(StyleProperty, "Card");
            body.Children.Add(card);
        }
    }
}
