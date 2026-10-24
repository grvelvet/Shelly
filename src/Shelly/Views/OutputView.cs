using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>Output of one run (live while it runs): stop, re-run, copy, save, clear, wrap, find.</summary>
public sealed class OutputView : UserControl
{
    readonly TextEditor ed = new() { IsReadOnly = true, ShowLineNumbers = false, WordWrap = true };
    readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock sub = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock spinner = new() { Margin = new Thickness(10, 0, 0, 0), Visibility = Visibility.Collapsed };
    readonly Button stopB, rerunB, copyB, saveB, clearB, openB;
    readonly ToggleButton wrapB;
    readonly TextBlock placeholder = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
    RunEntry? entry;
    RunSession? session;
    /// <summary>Extra header content on the left (the collapse chevron of the Scripts page).</summary>
    public readonly StackPanel Lead = new() { Orientation = Orientation.Horizontal };
    public bool ShowOpenScript { get; set; }
    public RunEntry? Entry => entry;
    /// <summary>Raised after <see cref="Show"/> (the Scripts page hides the panel while there is nothing to show).</summary>
    public event Action? EntryChanged;
    readonly List<(FrameworkElement El, string Key)> tips = new();

    public OutputView()
    {
        var root = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(4, 4, 4, 4), LastChildFill = true };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        Button B(string g, string tip, Action a)
        {
            var b = new Button { Content = g, ToolTip = Loc.T(tip) };
            tips.Add((b, tip));
            b.SetResourceReference(StyleProperty, "IconButton");
            b.Click += (_, _) => a();
            tools.Children.Add(b);
            return b;
        }
        stopB = B(Glyphs.Stop, "outStopTip", () => { if (session != null) App.S.Runs.Stop(session); });
        stopB.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
        rerunB = B(Glyphs.Rerun, "histRerun", Rerun);
        openB = B(Glyphs.Edit, "histOpen", () => { if (entry != null) { var f = App.S.Find(entry.Rel); if (f != null) App.OpenEditor(f.Path); else App.Status(Loc.F("histScriptGone", entry.Rel), true); } });
        copyB = B(Glyphs.Copy, "outCopyTip", () => { if (ShellOps.CopyText(ed.Text)) App.Status(Loc.T("outCopied")); });
        saveB = B(Glyphs.Save, "outSaveTip", Save);
        clearB = B(Glyphs.Clear, "outClearTip", () => ed.Clear());
        wrapB = new ToggleButton { Content = Glyphs.Wrap, ToolTip = Loc.T("outWrapTip"), IsChecked = App.S.Settings.OutputWrap };
        tips.Add((wrapB, "outWrapTip"));
        wrapB.SetResourceReference(StyleProperty, "IconToggle");
        wrapB.Click += (_, _) => { ed.WordWrap = wrapB.IsChecked == true; App.S.Settings.OutputWrap = ed.WordWrap; App.S.SaveSettings(); };
        tools.Children.Add(wrapB);
        B(Glyphs.Find, "outFindTip", () => ApplicationCommands.Find.Execute(null, ed.TextArea));
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        DockPanel.SetDock(Lead, Dock.Left);
        head.Children.Add(Lead);
        var tp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        title.FontSize = 14;
        tp.Children.Add(title);
        spinner.SetResourceReference(StyleProperty, "Spinner");
        tp.Children.Add(spinner);
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        tp.Children.Add(sub);
        head.Children.Add(tp);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        ed.WordWrap = App.S.Settings.OutputWrap;
        ed.FontSize = 13;
        ed.SetResourceReference(FontFamilyProperty, "MonoFont");
        ed.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        ed.Background = Brushes.Transparent;
        ed.Padding = new Thickness(10, 6, 6, 6);
        ScrollGutter.SetEnabled(ed, true);   // wrapped output must not run under the overlay scroll bar
        ed.Options.EnableHyperlinks = true;
        ed.TextArea.SelectionForeground = null;
        ed.TextArea.SelectionBorder = null;
        ThemeManager.Changed += ApplySelection;
        ApplySelection();
        SearchPanel.Install(ed);
        var grid = new Grid();
        var card = new Border { Child = ed, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        grid.Children.Add(card);
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        grid.Children.Add(placeholder);
        root.Children.Add(grid);
        Content = root;
        App.S.Runs.Finished += s => { if (session == s || (entry != null && s.Entry == entry)) Dispatcher.BeginInvoke(() => Show(entry)); };
        App.S.Runs.Started += s => { if (entry == null) { } };
        Loc.LanguageChanged += () => { foreach (var (el, key) in tips) el.ToolTip = Loc.T(key); Show(entry); };
        Show(null);
    }

    void ApplySelection()
    {
        var a = ThemeManager.Accent;
        ed.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, a.R, a.G, a.B));
    }

    void Rerun()
    {
        if (entry == null) return;
        var f = App.S.Find(entry.Rel);
        if (f == null) { App.Status(Loc.F("histScriptGone", entry.Rel), true); return; }
        _ = App.S.Runs.RunAsync(f, new RunOptions { RawArgs = entry.Args, Mode = entry.Mode, Engine = entry.Engine, Origin = "rerun" });
    }

    void Save()
    {
        var d = new SaveFileDialog { Filter = Loc.T("outSaveFilter"), FileName = (entry?.Name ?? "output") + " " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ".txt" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(d.FileName, ed.Text, new System.Text.UTF8Encoding(true)); App.Status(Loc.F("outSaved", d.FileName)); }
        catch (Exception ex) { App.Status(Loc.F("outSaveFail", ex.Message), true); }
    }

    /// <summary>Show a run (live when it is running); null = nothing.</summary>
    public void Show(RunEntry? e)
    {
        ShowCore(e);
        EntryChanged?.Invoke();
    }

    void ShowCore(RunEntry? e)
    {
        if (session != null) { session.Appended -= OnAppended; session.WindowOutputChanged -= OnWindowOutput; session.PropertyChanged -= OnTick; }
        session = null;
        entry = e;
        if (e == null)
        {
            ed.Text = "";
            title.Text = Loc.T("output");
            sub.Text = "";
            placeholder.Text = Loc.T("viewNone");
            placeholder.Visibility = Visibility.Visible;
            spinner.Visibility = Visibility.Collapsed;
            foreach (var b in new[] { stopB, rerunB, copyB, saveB, clearB, openB }) b.Visibility = Visibility.Collapsed;
            return;
        }
        placeholder.Visibility = Visibility.Collapsed;
        session = App.S.Runs.SessionOf(e.Id);
        title.Text = e.Name;
        if (session != null && session.IsInline)
        {
            ed.Text = session.Text.ToString();
            session.Appended += OnAppended;
            session.PropertyChanged += OnTick;
            ScrollEnd();
        }
        else if (session != null)
        {
            ed.Text = session.WindowOutput.Length > 0 ? session.WindowOutput : Loc.T("histNoOutput");
            session.WindowOutputChanged += OnWindowOutput;
            session.PropertyChanged += OnTick;
            if (session.WindowOutput.Length > 0) ScrollEnd();
        }
        else
        {
            string text = e.HasOutput ? App.S.History.ReadOutput(e) : "";
            if (text.Length == 0) text = e.Kind == "interrupted" ? Loc.T("histNoOutputLost") : e.Mode != "inline" ? Loc.T("histNoOutput") : "";
            ed.Text = text;
            ScrollEnd();
        }
        UpdateHeader();
    }

    void OnTick(object? s, System.ComponentModel.PropertyChangedEventArgs a) => UpdateHeader();

    void UpdateHeader()
    {
        if (entry == null) return;
        bool live = session != null && !session.Ended;
        spinner.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        stopB.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        rerunB.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        copyB.Visibility = saveB.Visibility = clearB.Visibility = Visibility.Visible;
        openB.Visibility = ShowOpenScript ? Visibility.Visible : Visibility.Collapsed;
        sub.Text = live
            ? session!.ElapsedText + "  ·  " + RunResult.ModeText(entry.Mode) + "  ·  " + EngineInfo.Name(entry.Engine)
            : entry.Line2 + "  ·  " + EngineInfo.Name(entry.Engine) + (entry.Preset.Length > 0 ? "  ·  " + Loc.F("schedPresetPart", entry.Preset) : "") + (entry.Args.Length > 0 ? "  ·  " + entry.Args.Trim() : "");
        var tip = new List<string>
        {
            Loc.F("histTipStart", entry.Start.ToString("g")),
            Loc.F("histTipMode", RunResult.ModeText(entry.Mode), RunResult.OriginText(entry.Origin)),
            Loc.F("histTipEngine", EngineInfo.Name(entry.Engine))
        };
        if (!live && entry.Kind != "started") tip.Add(Loc.F("histTipDuration", RunResult.FormatDuration(entry.Ms)));
        if (entry.Preset.Length > 0) tip.Add(Loc.F("schedPresetPart", entry.Preset));
        if (entry.Args.Length > 0) tip.Add(Loc.F("histTipArgs", entry.Args.Trim()));
        var res = RunResult.Format(entry.Kind, entry.Code);
        if (res.Tip.Length > 0) tip.Add(res.Tip);
        sub.ToolTip = string.Join("\n", tip);
    }

    void OnWindowOutput(string text)
    {
        ed.Text = text;
        ScrollEnd();
    }

    void OnAppended(string chunk)
    {
        bool atEnd = ed.TextArea.Caret.Offset >= ed.Document.TextLength - 1 || ed.VerticalOffset + ed.ViewportHeight >= ed.ExtentHeight - 4;
        ed.Document.Insert(ed.Document.TextLength, chunk);
        if (atEnd) ScrollEnd();
    }

    void ScrollEnd()
    {
        ed.TextArea.Caret.Offset = ed.Document.TextLength;
        ed.ScrollToEnd();
    }

    public void Refresh()
    {
        if (entry != null && App.S.History.Get(entry.Id) == null && App.S.Runs.SessionOf(entry.Id) == null) Show(null);
        else UpdateHeader();
    }
}
