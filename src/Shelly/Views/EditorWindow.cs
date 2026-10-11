using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>The built-in script editor: AvalonEdit with PowerShell highlighting, line numbers, find, go to line,
/// basic completion (commands, keywords, variables), save as UTF-8 with BOM, F5 to save and run.</summary>
public sealed class EditorWindow : Window
{
    static readonly List<EditorWindow> open = new();
    readonly TextEditor ed = new();
    readonly TextBlock posText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    readonly TextBlock encText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    readonly TextBlock scanText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand };
    readonly TextBlock dirtyText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    CompletionWindow? completion;
    string path;
    bool dirty;
    int firstIssueLine;
    Border? findBar;
    TextBox? findBox;
    int findIndex = -1;

    public string FilePath => path;

    public static void Open(string path, int line)
    {
        var w = open.FirstOrDefault(x => string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase));
        if (w == null)
        {
            w = new EditorWindow(path);
            open.Add(w);
            w.Show();
        }
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        if (line > 0) w.GoTo(line);
    }

    EditorWindow(string path)
    {
        this.path = path;
        Width = 1000; Height = 720; MinWidth = 520; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Shelly;component/Assets/shelly.ico")); } catch { }

        var root = new DockPanel();
        var bar = Toolbar();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var status = new DockPanel { Height = 30, LastChildFill = true };
        foreach (var t in new[] { posText, encText, dirtyText }) { t.FontSize = 12; t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); DockPanel.SetDock(t, Dock.Right); }
        scanText.FontSize = 12;
        scanText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        scanText.MouseLeftButtonUp += (_, _) => { if (firstIssueLine > 0) GoTo(firstIssueLine); };
        status.Children.Add(posText); status.Children.Add(encText); status.Children.Add(dirtyText); status.Children.Add(scanText);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        ed.ShowLineNumbers = true;
        ed.FontSize = 14;
        ed.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        ed.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        ed.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "TextFillColorSecondaryBrush");
        ed.Background = Brushes.Transparent;
        ed.Options.ConvertTabsToSpaces = true;
        ed.Options.IndentationSize = 4;
        ed.Options.EnableHyperlinks = false;
        ed.Options.EnableEmailHyperlinks = false;
        ed.Options.HighlightCurrentLine = true;
        ed.WordWrap = false;
        ed.Padding = new Thickness(4, 0, 0, 0);
        // Custom find bar instead of AvalonEdit SearchPanel (its default template looks broken under the Fluent theme).
        var findBar = BuildFindBar();
        DockPanel.SetDock(findBar, Dock.Top);
        root.Children.Add(findBar);
        var host = new Border { Child = ed, BorderThickness = new Thickness(0, 1, 0, 1) };
        host.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        host.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
        root.Children.Add(host);
        Content = root;
        TitleBar.Attach(this);

        LoadHighlighting();
        ThemeManager.Changed += LoadHighlighting;
        Load();
        ed.TextChanged += (_, _) => SetDirty(true);
        ed.TextArea.Caret.PositionChanged += (_, _) => UpdatePos();
        ed.TextArea.TextEntered += OnTextEntered;
        ed.TextArea.TextEntering += OnTextEntering;
        App.S.MetaChanged += UpdateScan;
        Loc.LanguageChanged += Relocalize;
        InputBindings.Add(new KeyBinding(new Relay(() => Save()), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => _ = SaveAndRun()), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Relay(ShowFind), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => FindNext(true)), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Relay(() => FindNext(false)), Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new Relay(GoToPrompt), Key.G, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => ShowCompletion("")), Key.Space, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => ed.FontSize = Math.Min(32, ed.FontSize + 1)), Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => ed.FontSize = Math.Max(9, ed.FontSize - 1)), Key.OemMinus, ModifierKeys.Control));
        Closing += (_, e) => { if (!App.Exiting && !ConfirmClose()) e.Cancel = true; };
        Closed += (_, _) =>
        {
            open.Remove(this);
            ThemeManager.Changed -= LoadHighlighting;
            App.S.MetaChanged -= UpdateScan;
            Loc.LanguageChanged -= Relocalize;
        };
        ContentRendered += (_, _) => ed.Focus();
    }

    sealed class Relay(Action a) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => a();
    }

    /// <summary>Texts set in code, re-read after a language switch.</summary>
    readonly List<(FrameworkElement El, Func<string> Text)> tips = new();
    string enc = "utf8bom";

    void Relocalize()
    {
        foreach (var (el, text) in tips) el.ToolTip = text();
        encText.Text = EncLabel(enc);
        if (encText.ToolTip != null) encText.ToolTip = Loc.F("edEncNote", encText.Text);
        SetDirty(dirty);
        UpdatePos();
        UpdateScan();
    }

    static string EncLabel(string enc) => enc switch
    {
        "utf8bom" => Loc.T("encUtf8Bom"), "utf8" => Loc.T("encUtf8"), "ascii" => Loc.T("encAscii"),
        "utf16le" => Loc.F("encUtf16", "LE"), "utf16be" => Loc.F("encUtf16", "BE"),
        _ => Loc.F("encAnsi", "windows-" + System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage)
    };

    UIElement BuildFindBar()
    {
        findBar = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(8, 4, 8, 4) };
        findBar.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        findBar.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
        findBar.BorderThickness = new Thickness(0, 0, 0, 1);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        findBox = new TextBox { MinWidth = 180, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), FontSize = 13 };
        findBox.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        findBox.SetResourceReference(Control.BackgroundProperty, "ControlFillColorDefaultBrush");
        findBox.SetResourceReference(Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindNext(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { HideFind(); e.Handled = true; }
        };
        row.Children.Add(findBox);

        Button Btn(string glyph, Action a, string tip)
        {
            var b = new Button { Content = glyph, Margin = new Thickness(4, 0, 0, 0), ToolTip = tip };
            b.SetResourceReference(StyleProperty, "IconButton");
            b.Click += (_, _) => a();
            return b;
        }
        row.Children.Add(Btn("\uE74B", () => FindNext(true), Loc.T("edFindNext")));
        row.Children.Add(Btn("\uE74A", () => FindNext(false), Loc.T("edFindPrev")));
        row.Children.Add(Btn("\uE711", HideFind, Loc.T("edFindClose")));
        findBar.Child = row;
        return findBar;
    }

    void ShowFind()
    {
        if (findBar == null || findBox == null) return;
        findBar.Visibility = Visibility.Visible;
        if (ed.TextArea.Selection.Length > 0) findBox.Text = ed.TextArea.Selection.GetText();
        findBox.Focus();
        findBox.SelectAll();
        findIndex = -1;
    }

    void HideFind()
    {
        if (findBar != null) findBar.Visibility = Visibility.Collapsed;
        ed.Focus();
    }

    void FindNext(bool forward)
    {
        if (findBox == null || findBox.Text.Length == 0) return;
        string q = findBox.Text;
        string text = ed.Text;
        int start = forward
            ? (ed.SelectionStart + ed.SelectionLength)
            : Math.Max(0, ed.SelectionStart - 1);
        int idx = forward
            ? text.IndexOf(q, Math.Min(start, text.Length), StringComparison.OrdinalIgnoreCase)
            : text.LastIndexOf(q, Math.Min(start, text.Length - 1), StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            idx = forward ? text.IndexOf(q, StringComparison.OrdinalIgnoreCase) : text.LastIndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) { App.Status(Loc.T("findNone"), true); return; }
        ed.Select(idx, q.Length);
        ed.TextArea.Caret.BringCaretToView();
    }

    UIElement Toolbar()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
        Button B(string glyph, Func<string> tip, Action a)
        {
            var b = new Button { Content = glyph, ToolTip = tip() };
            tips.Add((b, tip));
            b.SetResourceReference(StyleProperty, "IconButton");
            b.Click += (_, _) => a();
            sp.Children.Add(b);
            return b;
        }
        void Sep() { var r = new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0) }; r.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush"); sp.Children.Add(r); }
        B(Glyphs.Save, () => Loc.T("edSaveTip"), () => Save());
        var run = B(Glyphs.Play, () => Loc.T("edRunTip"), () => _ = SaveAndRun());
        run.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        Sep();
        B(Glyphs.Find, () => Loc.T("edFindTip"), ShowFind);
        B("\uE8AD", () => Loc.T("edGotoTip"), GoToPrompt);
        Sep();
        var wrap = new ToggleButton { Content = Glyphs.Wrap, ToolTip = Loc.T("edWrap"), IsChecked = false };
        wrap.SetResourceReference(StyleProperty, "IconToggle");
        tips.Add((wrap, () => Loc.T("edWrap")));
        wrap.Click += (_, _) => ed.WordWrap = wrap.IsChecked == true;
        sp.Children.Add(wrap);
        var nums = new ToggleButton { Content = "\uE9D5", ToolTip = Loc.T("edLineNumbers"), IsChecked = true };
        nums.SetResourceReference(StyleProperty, "IconToggle");
        tips.Add((nums, () => Loc.T("edLineNumbers")));
        nums.Click += (_, _) => ed.ShowLineNumbers = nums.IsChecked == true;
        sp.Children.Add(nums);
        B("\uE8E7", () => Loc.T("edFont") + " −  (Ctrl+−)", () => ed.FontSize = Math.Max(9, ed.FontSize - 1));
        B("\uE8E8", () => Loc.T("edFont") + " +  (Ctrl++)", () => ed.FontSize = Math.Min(32, ed.FontSize + 1));
        Sep();
        B("\uE8A7", () => Loc.T("edExternalTip"), () => { if (Save()) ShellOps.ExternalEditor(path); });
        return sp;
    }

    // ---------- Highlighting ----------

    static readonly Dictionary<string, string> DarkColors = new()
    {
        ["Comment"] = "#6A9955", ["String"] = "#CE9178", ["Variable"] = "#9CDCFE", ["Keyword"] = "#C586C0", ["Command"] = "#DCDCAA",
        ["Parameter"] = "#A8A8A8", ["Number"] = "#B5CEA8", ["Type"] = "#4EC9B0", ["Operator"] = "#B4B4B4", ["Requires"] = "#569CD6"
    };

    void LoadHighlighting()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shelly.PowerShell.xshd")!;
            using var r = XmlReader.Create(s);
            var def = HighlightingLoader.Load(r, HighlightingManager.Instance);
            if (ThemeManager.IsDark)
                foreach (var c in def.NamedHighlightingColors)
                    if (DarkColors.TryGetValue(c.Name, out var hex))
                        c.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
            ed.SyntaxHighlighting = def;
            ed.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(ThemeManager.IsDark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0));
            ed.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
            ed.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            ed.TextArea.SelectionForeground = null;
            ed.TextArea.SelectionBorder = null;
        }
        catch (Exception ex) { App.Status(Loc.F("edNoColors", ex.Message), true); }
    }

    // ---------- File ----------

    void Load()
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            string text = TextFile.Decode(bytes, out enc);
            ed.Document = new TextDocument(text);
            encText.Text = EncLabel(enc);
            if (enc != "utf8bom" && enc != "ascii") encText.ToolTip = Loc.F("edEncNote", encText.Text);
        }
        catch (Exception ex)
        {
            Dlg.Message(Loc.T("edit"), Loc.F("edOpenFail", ex.Message), Glyphs.Error, "SystemFillColorCriticalBrush");
            Dispatcher.BeginInvoke(Close);
            return;
        }
        SetDirty(false);
        UpdatePos();
        UpdateScan();
    }

    void SetDirty(bool d)
    {
        dirty = d;
        dirtyText.Text = d ? "● " + Loc.T("edUnsaved") : "";
        UpdateTitle();
    }

    void UpdateTitle() => Title = (dirty ? "● " : "") + Loc.F("editorTitle", Path.GetFileName(path));

    void UpdatePos() => posText.Text = Loc.F("edPos", ed.TextArea.Caret.Line, ed.TextArea.Caret.Column);

    public bool Save()
    {
        try
        {
            // A downloaded script stays marked as downloaded after edits (only Unblock removes the mark)
            MotW.KeepWhile(path, () => File.WriteAllText(path, ed.Text, new UTF8Encoding(true)));
            string was = encText.Text;
            if (encText.ToolTip != null) { App.Status(Loc.F("edConverted", was)); encText.ToolTip = null; }
            enc = "utf8bom";
            encText.Text = EncLabel(enc);
            SetDirty(false);
            App.Status(Loc.F("edSaved", Path.GetFileName(path)));
            App.S.RefreshScripts();
            return true;
        }
        catch (Exception ex)
        {
            Dlg.Message(Loc.T("edSave"), Loc.F("edSaveFail", ex.Message), Glyphs.Error, "SystemFillColorCriticalBrush");
            return false;
        }
    }

    async Task SaveAndRun()
    {
        if (!Save()) return;
        var f = App.S.Scripts.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (f == null) { App.Status(Loc.T("edRunNotFound"), true); return; }
        App.ShowMain();
        await App.S.Runs.RunAsync(f, new RunOptions { Origin = "editor" });
    }

    public bool ConfirmClose()
    {
        if (!dirty) return true;
        Activate();
        string r = Dlg.Choose(Loc.T("edCloseTitle"), Loc.T("edCloseTitle"), Loc.F("edCloseText", Path.GetFileName(path)), Glyphs.Save, "AccentTextFillColorPrimaryBrush",
            new("save", Loc.T("edSave"), true), new("discard", Loc.T("edDontSave")), new("cancel", Loc.T("cancel")));
        if (r == "save") return Save();
        if (r == "discard") { dirty = false; return true; }
        return false;
    }

    void GoTo(int line)
    {
        line = Math.Clamp(line, 1, ed.Document.LineCount);
        var l = ed.Document.GetLineByNumber(line);
        ed.TextArea.Caret.Offset = l.Offset;
        ed.Select(l.Offset, l.Length);
        ed.ScrollToLine(line);
        ed.Focus();
    }

    void GoToPrompt()
    {
        var s = Dlg.Input(Loc.T("edGoto"), Loc.F("edGotoPrompt", ed.Document.LineCount), ed.TextArea.Caret.Line.ToString());
        if (s != null && int.TryParse(s.Trim(), out int n)) GoTo(n);
    }

    // ---------- Analysis in the status bar ----------

    void UpdateScan()
    {
        var f = App.S.Scripts.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (f == null) { scanText.Text = ""; return; }
        var m = App.S.Meta(f);
        firstIssueLine = 0;
        var parts = new List<string>();
        var errs = m.Engine.Errors ?? new List<ParseError>();
        if (errs.Count > 0)
        {
            parts.Add(Loc.F("edErrLine", errs[0].Line, 1, errs[0].Message) + (errs.Count > 1 ? "  (+" + (errs.Count - 1) + ")" : ""));
            firstIssueLine = errs[0].Line;
        }
        var flagged = Safety.Blocking(m.Danger);
        if (flagged.Count > 0)
        {
            parts.Add(Loc.F("edScanDanger", flagged.Count, Safety.Level(flagged) == "high" ? Loc.T("sevHigh") : Loc.T("sevMedium")));
            if (firstIssueLine == 0) firstIssueLine = flagged[0].Line;
        }
        if (m.Zone?.Blocks == true) parts.Add(Loc.T("motwMark"));
        var compat = m.Issues.Where(i => i.Kind != "parse" && i.Level != "info").ToList();
        if (compat.Count > 0) parts.Add(Loc.F("edScanCompat", compat.Count));
        if (m.Scan == "checking") parts.Add(Loc.T("scanChecking"));
        else if (m.Scan == "unverified") parts.Add(Loc.T("scanUnverified"));
        else if (m.Pending) parts.Add(Loc.T("analyzing"));
        // "Nothing found" only after PowerShell's parser has checked the script
        scanText.Text = parts.Count > 0 ? string.Join("   ·   ", parts) : Loc.T("edScanOk");
        scanText.ToolTip = string.Join("\n", m.Danger.Take(10).Select(d => Loc.F("dzLine", d.Line) + ": " + d.Text).Concat(compat.Select(i => i.Text())));
        scanText.SetResourceReference(TextBlock.ForegroundProperty, errs.Count > 0 || m.DangerLevel == "high" ? "SystemFillColorCriticalBrush" :
            m.Danger.Count > 0 || compat.Count > 0 ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
    }

    // ---------- Completion ----------

    static readonly string[] Keywords = { "begin", "break", "catch", "class", "continue", "do", "else", "elseif", "end", "enum", "exit", "filter", "finally",
        "for", "foreach", "function", "if", "param", "process", "return", "switch", "throw", "trap", "try", "until", "using", "while" };

    static readonly string[] Commands =
    {
        "Add-Content", "Add-Member", "Add-Type", "Clear-Content", "Clear-Host", "Compare-Object", "Compress-Archive", "ConvertFrom-Csv", "ConvertFrom-Json",
        "ConvertFrom-StringData", "ConvertTo-Csv", "ConvertTo-Html", "ConvertTo-Json", "ConvertTo-SecureString", "Copy-Item", "Expand-Archive", "Export-Clixml",
        "Export-Csv", "ForEach-Object", "Format-List", "Format-Table", "Get-Acl", "Get-Alias", "Get-ChildItem", "Get-CimInstance", "Get-Clipboard",
        "Get-Command", "Get-ComputerInfo", "Get-Content", "Get-Credential", "Get-Date", "Get-Disk", "Get-EventLog", "Get-FileHash", "Get-Help", "Get-History",
        "Get-Host", "Get-Item", "Get-ItemProperty", "Get-ItemPropertyValue", "Get-Location", "Get-Member", "Get-Module", "Get-NetAdapter", "Get-NetIPAddress",
        "Get-PSDrive", "Get-Process", "Get-Random", "Get-ScheduledTask", "Get-Service", "Get-Unique", "Get-Variable", "Get-Volume", "Get-WinEvent",
        "Group-Object", "Import-Clixml", "Import-Csv", "Import-Module", "Invoke-Command", "Invoke-Item", "Invoke-RestMethod", "Invoke-WebRequest",
        "Join-Path", "Measure-Command", "Measure-Object", "Move-Item", "New-Item", "New-ItemProperty", "New-Object", "New-TimeSpan", "Out-File", "Out-GridView",
        "Out-Host", "Out-Null", "Out-String", "Pop-Location", "Push-Location", "Read-Host", "Register-ScheduledTask", "Remove-Item", "Remove-ItemProperty",
        "Rename-Item", "Resolve-Path", "Restart-Service", "Resume-Service", "Select-Object", "Select-String", "Select-Xml", "Set-Clipboard", "Set-Content",
        "Set-Item", "Set-ItemProperty", "Set-Location", "Set-Service", "Set-StrictMode", "Set-Variable", "Sort-Object", "Split-Path", "Start-Job",
        "Start-Process", "Start-Service", "Start-Sleep", "Start-Transcript", "Stop-Process", "Stop-Service", "Stop-Transcript", "Tee-Object", "Test-Connection",
        "Test-NetConnection", "Test-Path", "Wait-Job", "Wait-Process", "Where-Object", "Write-Debug", "Write-Error", "Write-Host", "Write-Information",
        "Write-Output", "Write-Progress", "Write-Verbose", "Write-Warning"
    };

    static readonly string[] Parameters = { "-Path", "-LiteralPath", "-Filter", "-Include", "-Exclude", "-Recurse", "-Force", "-Name", "-Value", "-ErrorAction",
        "-WarningAction", "-Verbose", "-WhatIf", "-Confirm", "-PassThru", "-NoNewline", "-ForegroundColor", "-BackgroundColor", "-Encoding", "-Property",
        "-First", "-Last", "-Unique", "-Descending", "-Raw", "-Destination", "-ArgumentList", "-FilePath", "-Wait", "-Verb", "-Uri", "-Method", "-Body",
        "-Headers", "-OutFile", "-Seconds", "-Milliseconds", "-Object", "-InputObject", "-Depth", "-Append", "-NoTypeInformation", "-Delimiter", "-Prompt" };

    void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (completion != null && e.Text.Length > 0 && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '-' && e.Text[0] != '_' && e.Text[0] != ':')
            completion.CompletionList.RequestInsertion(e);
    }

    void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (completion != null || e.Text.Length != 1) return;
        char c = e.Text[0];
        string word = WordBefore();
        if (c == '$' || (c == '-' && word == "-" && IsAfterSpace()) || (char.IsLetter(c) && word.Length == 3 && !word.StartsWith("$") && !word.StartsWith("-")))
            ShowCompletion(word);
    }

    bool IsAfterSpace()
    {
        int o = ed.CaretOffset - 2;
        return o >= 0 && char.IsWhiteSpace(ed.Document.GetCharAt(o));
    }

    string WordBefore()
    {
        int end = ed.CaretOffset, start = end;
        while (start > 0)
        {
            char ch = ed.Document.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '$' || ch == ':') start--; else break;
        }
        return ed.Document.GetText(start, end - start);
    }

    void ShowCompletion(string word)
    {
        if (word.Length == 0) word = WordBefore();
        IEnumerable<string> items;
        if (word.StartsWith("$"))
        {
            var vars = System.Text.RegularExpressions.Regex.Matches(ed.Text, @"\$[\w:]+").Select(m => m.Value)
                .Concat(new[] { "$true", "$false", "$null", "$_", "$PSScriptRoot", "$PSCommandPath", "$args", "$env:", "$LASTEXITCODE", "$ErrorActionPreference", "$Host", "$PSVersionTable" });
            items = vars.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        else if (word.StartsWith("-")) items = Parameters;
        else items = Commands.Concat(Keywords).Concat(System.Text.RegularExpressions.Regex.Matches(ed.Text, @"(?im)^\s*function\s+([\w-]+)").Select(m => m.Groups[1].Value));
        var list = items.Where(x => x.StartsWith(word, StringComparison.OrdinalIgnoreCase) && !string.Equals(x, word, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        if (list.Count == 0) return;
        completion = new CompletionWindow(ed.TextArea) { StartOffset = ed.CaretOffset - word.Length, CloseWhenCaretAtBeginning = true, MinWidth = 240 };
        completion.SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        completion.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        foreach (var it in list) completion.CompletionList.CompletionData.Add(new Item(it));
        completion.Closed += (_, _) => completion = null;
        completion.Show();
        completion.CompletionList.SelectItem(word);
    }

    sealed class Item(string text) : ICompletionData
    {
        public ImageSource? Image => null;
        public string Text => text;
        public object Content => text;
        public object? Description => null;
        public double Priority => 0;
        public void Complete(TextArea area, ISegment seg, EventArgs e) => area.Document.Replace(seg, text);
    }
}
