using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>The built-in script editor: AvalonEdit with PowerShell/Python/Batch/custom highlighting, live linter with
/// squiggly underlines and Problems panel, line numbers, find, go to line, code completion, F5 to save and run.</summary>
public sealed class EditorWindow : Window
{
    static readonly List<EditorWindow> open = new();
    readonly TextEditor ed = new();
    readonly TextBlock posText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    readonly TextBlock encText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    readonly TextBlock scanText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand };
    readonly TextBlock dirtyText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    readonly DispatcherTimer lintTimer;
    readonly LintUnderlineRenderer underlineRenderer;
    readonly ToolTip hoverTip = new();
    Border? problemsPanel;
    TextBlock? problemsTitle;
    ListBox? problemsList;
    List<LintIssue> currentIssues = new();
    LintIssue? pyAsyncSyntaxIssue;
    CancellationTokenSource? pySyntaxCts;
    int issueCycleIndex = -1;
    CompletionWindow? completion;
    string path;
    bool dirty;
    int firstIssueLine;
    Border? findBar;
    TextBox? findBox;

    /// <summary>A .py file: Python colors, completion, linter, saved as UTF-8 without BOM.</summary>
    bool IsPython => ScriptKinds.IsPython(path);
    /// <summary>The PowerShell features (colors, completion, parser checks) are for .ps1.</summary>
    bool IsPowerShell => ScriptKinds.Lang(path) == ScriptLangs.PowerShell;
    bool IsBatch => ScriptKinds.IsBatch(path);

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

        lintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        lintTimer.Tick += (_, _) => { lintTimer.Stop(); RunLinter(triggerAsyncPy: true); };
        underlineRenderer = new LintUnderlineRenderer(ed);
        ed.TextArea.TextView.BackgroundRenderers.Add(underlineRenderer);

        var root = new DockPanel();
        var bar = Toolbar();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var status = new DockPanel { Height = 30, LastChildFill = true };
        foreach (var t in new[] { posText, encText, dirtyText }) { t.FontSize = 12; t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); DockPanel.SetDock(t, Dock.Right); }
        scanText.FontSize = 12;
        scanText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        scanText.MouseLeftButtonUp += (_, _) => ToggleProblemsPanel();
        status.Children.Add(posText); status.Children.Add(encText); status.Children.Add(dirtyText); status.Children.Add(scanText);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var probBar = BuildProblemsPanel();
        DockPanel.SetDock(probBar, Dock.Bottom);
        root.Children.Add(probBar);

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
        var findBarEl = BuildFindBar();
        DockPanel.SetDock(findBarEl, Dock.Top);
        root.Children.Add(findBarEl);
        var host = new Border { Child = ed, BorderThickness = new Thickness(0, 1, 0, 1) };
        host.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        host.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
        root.Children.Add(host);
        Content = root;
        TitleBar.Attach(this);

        LoadHighlighting();
        ThemeManager.Changed += LoadHighlighting;
        Load();
        ed.TextChanged += (_, _) =>
        {
            SetDirty(true);
            lintTimer.Stop();
            lintTimer.Start();
        };
        ed.TextArea.Caret.PositionChanged += (_, _) => UpdatePos();
        ed.TextArea.TextEntered += OnTextEntered;
        ed.TextArea.TextEntering += OnTextEntering;
        ed.MouseHover += OnEditorMouseHover;
        ed.MouseHoverStopped += (_, _) => hoverTip.IsOpen = false;
        App.S.MetaChanged += UpdateScan;
        Loc.LanguageChanged += Relocalize;
        InputBindings.Add(new KeyBinding(new Relay(() => Save()), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => _ = SaveAndRun()), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Relay(ShowFind), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => FindNext(true)), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Relay(() => FindNext(false)), Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new Relay(NextIssue), Key.F8, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Relay(GoToPrompt), Key.G, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => ShowCompletion("")), Key.Space, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => ed.FontSize = Math.Min(32, ed.FontSize + 1)), Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => ed.FontSize = Math.Max(9, ed.FontSize - 1)), Key.OemMinus, ModifierKeys.Control));
        Closing += (_, e) => { if (!App.Exiting && !ConfirmClose()) e.Cancel = true; };
        Closed += (_, _) =>
        {
            lintTimer.Stop();
            pySyntaxCts?.Cancel();
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
        RunLinter(triggerAsyncPy: false);
    }

    static string EncLabel(string enc) => enc switch
    {
        "utf8bom" => Loc.T("encUtf8Bom"), "utf8" => Loc.T("encUtf8"), "ascii" => Loc.T("encAscii"),
        "utf16le" => Loc.F("encUtf16", "LE"), "utf16be" => Loc.F("encUtf16", "BE"),
        "oem" => "OEM " + ConsoleText.Oem().CodePage,
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

    UIElement BuildProblemsPanel()
    {
        problemsPanel = new Border
        {
            Visibility = Visibility.Collapsed,
            Height = 160,
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        problemsPanel.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        problemsPanel.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");

        var dock = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(12, 4, 8, 4), LastChildFill = true };
        var closeBtn = new Button { Content = "\uE711", ToolTip = Loc.T("close") };
        closeBtn.SetResourceReference(StyleProperty, "IconButton");
        tips.Add((closeBtn, () => Loc.T("close")));
        closeBtn.Click += (_, _) => problemsPanel.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(closeBtn, Dock.Right);
        header.Children.Add(closeBtn);

        var nextBtn = new Button { Content = "\uE74B", ToolTip = Loc.T("lintNextTip"), Margin = new Thickness(0, 0, 4, 0) };
        nextBtn.SetResourceReference(StyleProperty, "IconButton");
        tips.Add((nextBtn, () => Loc.T("lintNextTip")));
        nextBtn.Click += (_, _) => NextIssue();
        DockPanel.SetDock(nextBtn, Dock.Right);
        header.Children.Add(nextBtn);

        problemsTitle = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        problemsTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        header.Children.Add(problemsTitle);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        problemsList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 0, 4, 4)
        };
        problemsList.SelectionChanged += (_, _) =>
        {
            if (problemsList.SelectedItem is ListBoxItem { Tag: LintIssue issue })
                GoTo(issue.Line, issue.Column);
        };
        dock.Children.Add(problemsList);
        problemsPanel.Child = dock;
        return problemsPanel;
    }

    void ToggleProblemsPanel()
    {
        if (problemsPanel == null) return;
        if (problemsPanel.Visibility == Visibility.Visible)
        {
            problemsPanel.Visibility = Visibility.Collapsed;
        }
        else if (currentIssues.Count > 0)
        {
            problemsPanel.Visibility = Visibility.Visible;
            if (firstIssueLine > 0) GoTo(firstIssueLine);
        }
    }

    void NextIssue()
    {
        if (currentIssues.Count == 0) return;
        if (problemsPanel != null && problemsPanel.Visibility != Visibility.Visible)
            problemsPanel.Visibility = Visibility.Visible;
        issueCycleIndex = (issueCycleIndex + 1) % currentIssues.Count;
        var issue = currentIssues[issueCycleIndex];
        if (problemsList != null && issueCycleIndex < problemsList.Items.Count)
            problemsList.SelectedIndex = issueCycleIndex;
        GoTo(issue.Line, issue.Column);
    }

    void ShowFind()
    {
        if (findBar == null || findBox == null) return;
        findBar.Visibility = Visibility.Visible;
        if (ed.TextArea.Selection.Length > 0) findBox.Text = ed.TextArea.Selection.GetText();
        findBox.Focus();
        findBox.SelectAll();
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
        B("\uE8A7", () => Loc.T("edExternalTip"), () => { if (Save()) ShellOps.ExternalEditor(path, ed.TextArea.Caret.Line); });
        B(Glyphs.Settings, () => Loc.T("edChoiceBtnTip"), () => EditorChoiceDialog.ShowDialog(this));
        return sp;
    }

    // ---------- Highlighting ----------

    static readonly Dictionary<string, string> DarkColors = new()
    {
        ["Comment"] = "#6A9955", ["String"] = "#CE9178", ["Variable"] = "#9CDCFE", ["Keyword"] = "#C586C0", ["Command"] = "#DCDCAA",
        ["Parameter"] = "#A8A8A8", ["Number"] = "#B5CEA8", ["Type"] = "#4EC9B0", ["Operator"] = "#B4B4B4", ["Requires"] = "#569CD6",
        ["Label"] = "#4EC9B0"
    };

    /// <summary>Dark-theme colors for the names in AvalonEdit's Python definition.</summary>
    static readonly Dictionary<string, string> PythonDarkColors = new()
    {
        ["Comment"] = "#6A9955", ["String"] = "#CE9178", ["MethodCall"] = "#DCDCAA", ["NumberLiteral"] = "#B5CEA8", ["Keywords"] = "#C586C0",
        ["Builtin"] = "#4EC9B0", ["Decorator"] = "#DCDCAA"
    };

    /// <summary>Dark-theme colors for AvalonEdit's built-in definitions used by custom scripts (.js, .json, .xml, .cs, etc.).</summary>
    public static readonly Dictionary<string, string> GenericDarkColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Comment"] = "#6A9955", ["LineComment"] = "#6A9955", ["BlockComment"] = "#6A9955", ["DocComment"] = "#6A9955", ["XmlDoc"] = "#6A9955",
        ["String"] = "#CE9178", ["Char"] = "#CE9178", ["Character"] = "#CE9178", ["Literal"] = "#CE9178", ["Regex"] = "#D16969",
        ["Number"] = "#B5CEA8", ["NumberLiteral"] = "#B5CEA8", ["Digits"] = "#B5CEA8",
        ["Keyword"] = "#C586C0", ["Keywords"] = "#C586C0", ["JavaScriptKeyWords"] = "#C586C0", ["ControlKeywords"] = "#C586C0",
        ["TrueFalse"] = "#569CD6", ["NullOrValueKeywords"] = "#569CD6", ["Boolean"] = "#569CD6",
        ["Type"] = "#4EC9B0", ["ReferenceTypes"] = "#4EC9B0", ["ValueTypes"] = "#4EC9B0", ["KnownType"] = "#4EC9B0", ["ClassName"] = "#4EC9B0",
        ["MethodCall"] = "#DCDCAA", ["Function"] = "#DCDCAA", ["JavaScriptFunctions"] = "#DCDCAA",
        ["Variable"] = "#9CDCFE", ["Parameter"] = "#9CDCFE", ["Property"] = "#9CDCFE", ["FieldName"] = "#9CDCFE",
        ["XmlTag"] = "#569CD6", ["AttributeName"] = "#9CDCFE", ["AttributeValue"] = "#CE9178", ["Entity"] = "#B5CEA8",
        ["Preprocessor"] = "#C586C0", ["Operator"] = "#B4B4B4", ["Punctuation"] = "#D4D4D4"
    };

    void LoadHighlighting()
    {
        try
        {
            IHighlightingDefinition? def;
            if (IsPython)
            {
                using var ps = typeof(HighlightingManager).Assembly.GetManifestResourceStream("ICSharpCode.AvalonEdit.Highlighting.Resources.Python-Mode.xshd");
                if (ps == null) { ed.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python"); return; }
                using var pr = XmlReader.Create(ps);
                def = HighlightingLoader.Load(pr, HighlightingManager.Instance);
                EnhancePythonHighlighting(def, ThemeManager.IsDark);
                if (ThemeManager.IsDark)
                    foreach (var c in def.NamedHighlightingColors)
                        if (PythonDarkColors.TryGetValue(c.Name, out var hex))
                            c.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            else if (IsBatch)
            {
                using var bs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shelly.Batch.xshd")!;
                using var br = XmlReader.Create(bs);
                def = HighlightingLoader.Load(br, HighlightingManager.Instance);
                if (ThemeManager.IsDark)
                    foreach (var c in def.NamedHighlightingColors)
                        if (DarkColors.TryGetValue(c.Name, out var hex))
                            c.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            else if (!IsPowerShell)
            {
                def = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path));
                if (def != null && ThemeManager.IsDark)
                {
                    foreach (var c in def.NamedHighlightingColors)
                    {
                        if (GenericDarkColors.TryGetValue(c.Name, out var hex))
                            c.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
                    }
                }
            }
            else
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shelly.PowerShell.xshd")!;
                using var r = XmlReader.Create(s);
                def = HighlightingLoader.Load(r, HighlightingManager.Instance);
                if (ThemeManager.IsDark)
                    foreach (var c in def.NamedHighlightingColors)
                        if (DarkColors.TryGetValue(c.Name, out var hex))
                            c.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            ed.SyntaxHighlighting = def;
            ed.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(ThemeManager.IsDark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0));
            ed.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
            ed.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            ed.TextArea.SelectionForeground = null;
            ed.TextArea.SelectionBorder = null;
            ed.TextArea.TextView.InvalidateVisual();
        }
        catch (Exception ex) { App.Status(Loc.F("edNoColors", ex.Message), true); }
    }

    static void EnhancePythonHighlighting(IHighlightingDefinition def, bool dark)
    {
        var rules = def.MainRuleSet.Rules;
        var kwColor = def.GetNamedColor("Keywords") ?? new HighlightingColor { Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(dark ? "#C586C0" : "#AF00DB")) };
        var builtinColor = new HighlightingColor { Name = "Builtin", Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(dark ? "#4EC9B0" : "#267F99")) };
        var decColor = new HighlightingColor { Name = "Decorator", Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(dark ? "#DCDCAA" : "#795E26")) };

        rules.Insert(0, new HighlightingRule
        {
            Color = decColor,
            Regex = new Regex(@"^\s*@[A-Za-z_][A-Za-z0-9_.]*", RegexOptions.Compiled)
        });
        rules.Add(new HighlightingRule
        {
            Color = kwColor,
            Regex = new Regex(@"\b(async|await|match|case|nonlocal|True|False|None)\b", RegexOptions.Compiled)
        });
        rules.Add(new HighlightingRule
        {
            Color = builtinColor,
            Regex = new Regex(@"\b(print|len|range|open|int|str|float|bool|list|dict|set|tuple|isinstance|enumerate|zip|map|filter|sorted|sum|min|max|abs|any|all|input|super|type|Exception|ValueError|TypeError|KeyError|RuntimeError|OSError|FileNotFoundError)\b", RegexOptions.Compiled)
        });
    }

    // ---------- File ----------

    void Load()
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            string text = TextFile.Decode(bytes, out enc);
            // A batch file that is not UTF-8 is in the console's OEM code page (cp866 on Russian Windows), not ANSI
            if (IsBatch && enc != "utf8bom" && enc != "ascii" && enc != "utf8" && !enc.StartsWith("utf16")) { text = BatchRun.Decode(bytes); enc = "oem"; }
            ed.Document = new TextDocument(text);
            encText.Text = EncLabel(enc);
            if (enc != "utf8bom" && enc != "ascii" && !(!IsPowerShell && enc == "utf8")) encText.ToolTip = Loc.F("edEncNote", encText.Text);
        }
        catch (Exception ex)
        {
            Dlg.Message(Loc.T("edit"), Loc.F("edOpenFail", ex.Message), Glyphs.Error, "SystemFillColorCriticalBrush");
            Dispatcher.BeginInvoke(Close);
            return;
        }
        SetDirty(false);
        UpdatePos();
        RunLinter(triggerAsyncPy: true);
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
            // PowerShell 5.1 needs the BOM for non-English text; Python files are saved as plain UTF-8 (what Python expects);
            // cmd.exe would run a BOM as part of the first line, so batch files (and custom ones) get no BOM either
            bool bom = IsPowerShell;
            MotW.KeepWhile(path, () => File.WriteAllText(path, ed.Text, new UTF8Encoding(bom)));
            string was = encText.Text;
            if (encText.ToolTip != null) { App.Status(Loc.F("edConverted", was)); encText.ToolTip = null; }
            enc = bom ? "utf8bom" : "utf8";
            encText.Text = EncLabel(enc);
            SetDirty(false);
            App.Status(Loc.F("edSaved", Path.GetFileName(path)));
            App.S.RefreshScripts();
            RunLinter(triggerAsyncPy: true);
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

    void GoTo(int line, int col = 1)
    {
        if (ed.Document.LineCount == 0) return;
        line = Math.Clamp(line, 1, ed.Document.LineCount);
        var l = ed.Document.GetLineByNumber(line);
        int offset = l.Offset + Math.Clamp(col - 1, 0, l.Length);
        ed.TextArea.Caret.Offset = offset;
        ed.Select(l.Offset, l.Length);
        ed.ScrollToLine(line);
        ed.Focus();
    }

    void GoToPrompt()
    {
        var s = Dlg.Input(Loc.T("edGoto"), Loc.F("edGotoPrompt", ed.Document.LineCount), ed.TextArea.Caret.Line.ToString());
        if (s != null && int.TryParse(s.Trim(), out int n)) GoTo(n);
    }

    // ---------- Linter & Analysis in the status bar ----------

    void UpdateScan() => RunLinter(triggerAsyncPy: false);

    void RunLinter(bool triggerAsyncPy)
    {
        var f = App.S.Scripts.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        var m = f != null ? App.S.Meta(f) : null;
        bool hasBom = !IsPowerShell && enc == "utf8bom";
        var issues = ScriptLinter.Lint(path, ed.Text, m, hasBom);

        if (IsPython)
        {
            if (pyAsyncSyntaxIssue != null)
                issues = ScriptLinter.SortAndDedupe(issues.Append(pyAsyncSyntaxIssue));
            if (triggerAsyncPy)
                _ = CheckPythonSyntaxAsync(ed.Text);
        }

        ApplyLintResults(issues, m);
    }

    async Task CheckPythonSyntaxAsync(string snapshot)
    {
        var py = App.S.Python;
        if (py == null || string.IsNullOrEmpty(py.Path)) return;
        pySyntaxCts?.Cancel();
        var cts = new CancellationTokenSource();
        pySyntaxCts = cts;
        var issue = await ScriptLinter.CheckPythonSyntaxAsync(py.Path, py.Prefix, snapshot, cts.Token);
        if (cts.IsCancellationRequested) return;
        pyAsyncSyntaxIssue = issue;
        RunLinter(triggerAsyncPy: false);
    }

    void ApplyLintResults(List<LintIssue> issues, ScriptMeta? m)
    {
        currentIssues = issues;
        underlineRenderer.Issues = issues;
        ed.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

        firstIssueLine = issues.Count > 0 ? issues[0].Line : 0;

        // Populate Problems panel
        if (problemsTitle != null)
            problemsTitle.Text = Loc.F("lintProblemsTitle", issues.Count);
        if (problemsList != null)
        {
            problemsList.Items.Clear();
            foreach (var iss in issues)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                string glyph = iss.Severity == "error" ? Glyphs.Error : iss.Severity == "warning" ? Glyphs.Warning : Glyphs.Info;
                string brush = iss.Severity == "error" ? "SystemFillColorCriticalBrush" :
                               iss.Severity == "warning" ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush";
                var icon = new TextBlock { Text = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
                row.Children.Add(icon);

                var lineTxt = new TextBlock
                {
                    Text = Loc.F("dzLine", iss.Line),
                    FontSize = 12,
                    MinWidth = 64,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                lineTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                row.Children.Add(lineTxt);

                var codeTxt = new TextBlock
                {
                    Text = "[" + iss.Code + "]",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                codeTxt.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                codeTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                row.Children.Add(codeTxt);

                var msgTxt = new TextBlock
                {
                    Text = iss.Message,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                msgTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                row.Children.Add(msgTxt);

                problemsList.Items.Add(new ListBoxItem { Content = row, Tag = iss, Padding = new Thickness(6, 3, 6, 3) });
            }
            if (issues.Count == 0 && problemsPanel != null && problemsPanel.Visibility == Visibility.Visible)
                problemsPanel.Visibility = Visibility.Collapsed;
        }

        // Status bar summary
        int errs = issues.Count(i => i.Severity == "error");
        int warns = issues.Count(i => i.Severity == "warning");
        int infos = issues.Count(i => i.Severity == "info");

        var parts = new List<string>();
        if (m?.Zone?.Blocks == true) parts.Add(Loc.T("motwMark"));
        if (errs > 0 && warns > 0) parts.Add(Loc.F("lintSummary", errs, warns));
        else if (errs > 0) parts.Add(Loc.F("lintSummaryErr", errs));
        else if (warns > 0) parts.Add(Loc.F("lintSummaryWarn", warns));
        else if (infos > 0) parts.Add(Loc.F("lintSummaryInfo", infos));
        else parts.Add(IsPowerShell ? Loc.T("edScanOk") : Loc.T("lintClean"));

        scanText.Text = string.Join("   ·   ", parts);
        scanText.ToolTip = issues.Count > 0
            ? string.Join("\n", issues.Take(12).Select(i => $"{Loc.F("dzLine", i.Line)} [{i.Code}]: {i.Message}")) + "\n" + Loc.T("lintToggleTip")
            : Loc.T("lintToggleTip");
        scanText.SetResourceReference(TextBlock.ForegroundProperty,
            errs > 0 ? "SystemFillColorCriticalBrush" :
            warns > 0 || m?.Zone?.Blocks == true ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
    }

    void OnEditorMouseHover(object? sender, MouseEventArgs e)
    {
        if (currentIssues.Count == 0) return;
        var pos = ed.GetPositionFromPoint(e.GetPosition(ed));
        if (pos == null) return;
        int line = pos.Value.Line;
        var matches = currentIssues.Where(i => i.Line == line).ToList();
        if (matches.Count == 0) return;
        hoverTip.PlacementTarget = ed.TextArea;
        hoverTip.Content = string.Join("\n", matches.Select(m => $"[{m.Code}] {m.Message}"));
        hoverTip.IsOpen = true;
        e.Handled = true;
    }

    sealed class LintUnderlineRenderer(TextEditor editor) : IBackgroundRenderer
    {
        public List<LintIssue> Issues { get; set; } = new();
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Issues.Count == 0 || !textView.VisualLinesValid) return;
            var doc = editor.Document;
            foreach (var issue in Issues)
            {
                if (issue.Line < 1 || issue.Line > doc.LineCount) continue;
                var docLine = doc.GetLineByNumber(issue.Line);
                if (docLine.Length == 0) continue;

                int startCol = Math.Clamp(issue.Column - 1, 0, Math.Max(0, docLine.Length - 1));
                int startOff = docLine.Offset + startCol;
                int len = issue.Length > 0 ? Math.Min(issue.Length, docLine.Length - startCol) : docLine.Length - startCol;
                if (len <= 0) len = docLine.Length;

                // Trim leading whitespace if underlining the whole line
                if (issue.Length <= 0 && startCol == 0)
                {
                    string lineText = doc.GetText(docLine);
                    int ws = lineText.Length - lineText.TrimStart().Length;
                    if (ws < lineText.Length) { startOff += ws; len -= ws; }
                }

                var seg = new TextSegment { StartOffset = startOff, Length = Math.Max(1, len) };
                var color = issue.Severity == "error" ? Color.FromRgb(0xE8, 0x11, 0x23) :
                            issue.Severity == "warning" ? Color.FromRgb(0xF7, 0xA8, 0x00) :
                            Color.FromRgb(0x00, 0x78, 0xD4);
                var pen = new Pen(new SolidColorBrush(color), 1.3);
                pen.Freeze();

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                {
                    DrawSquiggle(drawingContext, pen, rect.BottomLeft, rect.BottomRight);
                }
            }
        }

        static void DrawSquiggle(DrawingContext dc, Pen pen, Point start, Point end)
        {
            double width = end.X - start.X;
            if (width < 3) return;
            const double step = 3.0;
            const double amp = 1.5;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(start, false, false);
                int count = Math.Max(2, (int)(width / step));
                for (int i = 1; i <= count; i++)
                {
                    double x = start.X + i * step;
                    if (x > end.X) x = end.X;
                    double y = start.Y + ((i % 2 == 1) ? -amp : amp);
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geom.Freeze();
            dc.DrawGeometry(null, pen, geom);
        }
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

    static readonly string[] PyKeywordsAndBuiltins =
    {
        "async", "await", "break", "case", "class", "continue", "def", "del", "elif", "else", "except", "False", "finally",
        "for", "from", "global", "if", "import", "in", "is", "lambda", "match", "None", "nonlocal", "not", "or", "pass",
        "raise", "return", "True", "try", "while", "with", "yield",
        "abs", "all", "any", "bool", "dict", "enumerate", "Exception", "FileNotFoundError", "filter", "float", "input",
        "int", "isinstance", "KeyError", "len", "list", "map", "max", "min", "open", "OSError", "print", "range",
        "RuntimeError", "set", "sorted", "str", "sum", "super", "tuple", "type", "TypeError", "ValueError", "zip"
    };

    static readonly string[] BatchKeywordsAndCommands =
    {
        "call", "cd", "chcp", "chdir", "choice", "cls", "cmd", "color", "copy", "defined", "del", "dir", "do",
        "echo", "else", " EnableDelayedExpansion", "endlocal", "equ", "erase", "errorlevel", "exist", "exit", "find",
        "findstr", "for", "geq", "goto", "gtr", "icacls", "if", "in", "leq", "lss", "md", "mkdir", "mklink", "more",
        "move", "neq", "net", "not", "pause", "popd", "powershell", "pushd", "pwsh", "rd", "reg", "rem", "ren",
        "rename", "rmdir", "robocopy", "sc", "set", "setlocal", "shift", "sort", "start", "taskkill", "tasklist",
        "timeout", "title", "type", "ver", "where", "whoami", "xcopy"
    };

    void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (completion != null && e.Text.Length > 0 && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '-' && e.Text[0] != '_' && e.Text[0] != ':')
            completion.CompletionList.RequestInsertion(e);
    }

    void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (completion != null || e.Text.Length != 1) return;
        if (!IsPowerShell && !IsPython && !IsBatch) return;
        char c = e.Text[0];
        string word = WordBefore();
        if (IsPowerShell)
        {
            if (c == '$' || (c == '-' && word == "-" && IsAfterSpace()) || (char.IsLetter(c) && word.Length == 3 && !word.StartsWith("$") && !word.StartsWith("-")))
                ShowCompletion(word);
        }
        else if (IsPython || IsBatch)
        {
            if (char.IsLetter(c) && word.Length == 3)
                ShowCompletion(word);
        }
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
        if (!IsPowerShell && !IsPython && !IsBatch) return;
        if (word.Length == 0) word = WordBefore();
        IEnumerable<string> items;
        if (IsPowerShell)
        {
            if (word.StartsWith("$"))
            {
                var vars = Regex.Matches(ed.Text, @"\$[\w:]+").Select(m => m.Value)
                    .Concat(new[] { "$true", "$false", "$null", "$_", "$PSScriptRoot", "$PSCommandPath", "$args", "$env:", "$LASTEXITCODE", "$ErrorActionPreference", "$Host", "$PSVersionTable" });
                items = vars.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            }
            else if (word.StartsWith("-")) items = Parameters;
            else items = Commands.Concat(Keywords).Concat(Regex.Matches(ed.Text, @"(?im)^\s*function\s+([\w-]+)").Select(m => m.Groups[1].Value));
        }
        else if (IsPython)
        {
            var defs = Regex.Matches(ed.Text, @"(?m)^\s*(?:async\s+)?(?:def|class)\s+([A-Za-z_][A-Za-z0-9_]*)").Select(m => m.Groups[1].Value);
            items = PyKeywordsAndBuiltins.Concat(defs).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var labels = Regex.Matches(ed.Text, @"(?m)^\s*(:[A-Za-z0-9_.\-]+)").Select(m => m.Groups[1].Value);
            items = BatchKeywordsAndCommands.Select(s => s.Trim()).Concat(labels).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
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
