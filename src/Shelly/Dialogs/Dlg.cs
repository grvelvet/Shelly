using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shelly.Core;

namespace Shelly.Dialogs;

/// <summary>A small modal dialog in the Fluent style: glyph, heading, text, content and a button row.</summary>
public class DialogWindow : Window
{
    public readonly StackPanel Body = new() { Margin = new Thickness(24, 24, 24, 0) };
    public readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    protected readonly DockPanel Footer = new() { Margin = new Thickness(24, 24, 24, 24), LastChildFill = false };
    /// <summary>Holds the footer; in the ContentDialog layout it is the shaded button band.</summary>
    protected readonly Border FooterBand = new();
    public string Result { get; protected set; } = "cancel";

    public DialogWindow(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var owner = App.OwnerWindow();
        if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else { WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; }
        DockPanel.SetDock(Buttons, Dock.Right);
        Footer.Children.Add(Buttons);
        var root = new DockPanel();
        FooterBand.Child = Footer;
        DockPanel.SetDock(FooterBand, Dock.Bottom);
        root.Children.Add(FooterBand);
        // Grows with the content; scrolls only when the dialog would not fit on the screen
        root.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 180) });
        Content = root;
        Shelly.Views.TitleBar.Attach(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Result = "cancel"; Close(); } };
    }

    /// <summary>WinUI ContentDialog layout: 24 px content padding (12 at the top, under the title row) and the
    /// buttons in a band with a subtle fill, a 1 px top stroke and 24 px padding.</summary>
    public void ContentDialogLayout()
    {
        Body.Margin = new Thickness(24, 12, 24, 24);
        Footer.Margin = new Thickness(24);
        FooterBand.BorderThickness = new Thickness(0, 1, 0, 0);
        FooterBand.SetResourceReference(Border.BackgroundProperty, "ShellyDialogBandBrush");
        FooterBand.SetResourceReference(Border.BorderBrushProperty, "ShellyLayerStrokeBrush");
    }

    public void AddHeader(string glyph, string brushKey, string head, string? sub)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (glyph.Length > 0)
        {
            var ic = new TextBlock { Text = glyph, FontSize = 24, Margin = new Thickness(0, 2, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            ic.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            ic.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            DockPanel.SetDock(ic, Dock.Left);
            dp.Children.Add(ic);
        }
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = head, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(sub))
        {
            var t = new TextBlock { Text = sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            sp.Children.Add(t);
        }
        dp.Children.Add(sp);
        Body.Children.Add(dp);
    }

    public Button AddButton(string id, string text, bool accent = false, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (accent) b.SetResourceReference(StyleProperty, "AccentButtonStyle");
        b.Click += (_, _) => { Result = id; if (id == "cancel") DialogResult = false; else DialogResult = true; };
        Buttons.Children.Add(b);
        return b;
    }

    public Button AddLeftButton(string id, string text)
    {
        var b = new Button { Content = text, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => { Result = id; DialogResult = true; };
        DockPanel.SetDock(b, Dock.Left);
        Footer.Children.Insert(0, b);
        return b;
    }

    public string Run()
    {
        try { ShowDialog(); } catch (InvalidOperationException) { }
        return Result;
    }

    public static TextBlock Text(string s, string? brushKey = null, double size = 14)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, FontSize = size };
        if (brushKey != null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    public static Border Card(UIElement child)
    {
        var b = new Border { Child = child, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return b;
    }
}

public static class Glyphs
{
    public const string Play = "\uE768", Stop = "\uE71A", Settings = "\uE713", Add = "\uE710", NewFolder = "\uE8F4", Refresh = "\uE72C",
        Folder = "\uE8B7", OpenFolder = "\uE838", Search = "\uE721", Menu = "\uE700", Star = "\uE734", StarFill = "\uE735", History = "\uE81C",
        Script = "\uE756", Shield = "\uEA18", Copy = "\uE8C8", Save = "\uE74E", Delete = "\uE74D", Clear = "\uE894", Cancel = "\uE711",
        Wrap = "\uE8E4", Edit = "\uE70F", More = "\uE712", Window = "\uE8A7", Admin = "\uE7EF", Compat = "\uE9D9", Info = "\uE946",
        Warning = "\uE7BA", Error = "\uEA39", Check = "\uE73E", Build = "\uE90F", Link = "\uE71B", Update = "\uE895", Globe = "\uE774",
        Color = "\uE790", Keyboard = "\uE765", Bell = "\uEA8F", ChevronDown = "\uE70D", ChevronRight = "\uE76C", ChevronUp = "\uE70E",
        Back = "\uE72B", Pin = "\uE718", Code = "\uE943", Rerun = "\uE777", Find = "\uE721", Data = "\uE8F1", Image = "\uEB9F", Download = "\uE896",
        Sort = "\uE8CB", CollapseAll = "\uF165", ExpandAll = "\uECCD", Filter = "\uE71C", ViewCards = "\uECA5", ViewList = "\uEA37";
}

public static class Dlg
{
    public static void Message(string title, string text, string glyph = Glyphs.Info, string brushKey = "AccentTextFillColorPrimaryBrush")
    {
        var d = new DialogWindow(title);
        d.AddHeader(glyph, brushKey, title, null);
        d.Body.Children.Add(DialogWindow.Text(text));
        d.AddButton("ok", Loc.T("ok"), true, true, true);
        d.Run();
    }

    public static bool Confirm(string title, string head, string text, string okText, bool danger = false, string glyph = Glyphs.Warning)
    {
        var d = new DialogWindow(title);
        d.AddHeader(glyph, danger ? "SystemFillColorCriticalBrush" : "SystemFillColorCautionBrush", head, null);
        if (text.Length > 0) d.Body.Children.Add(DialogWindow.Text(text));
        d.AddButton("ok", okText, true, !danger);
        var c = d.AddButton("cancel", Loc.T("cancel"), false, danger, true);
        if (danger) d.ContentRendered += (_, _) => c.Focus();
        return d.Run() == "ok";
    }

    public sealed record Choice(string Id, string Text, bool Accent = false);

    public static string Choose(string title, string head, string text, string glyph, string brushKey, params Choice[] choices)
    {
        var d = new DialogWindow(title, 520);
        d.AddHeader(glyph, brushKey, head, null);
        if (text.Length > 0) d.Body.Children.Add(DialogWindow.Text(text));
        foreach (var c in choices) d.AddButton(c.Id, c.Text, c.Accent, c.Accent, c.Id == "cancel");
        return d.Run();
    }

    /// <summary>Text input; null when cancelled. selectUntil: select only the name part (before the last dot).</summary>
    public static string? Input(string title, string prompt, string initial = "", bool selectName = false)
    {
        var d = new DialogWindow(title);
        d.AddHeader("", "", title, null);
        d.Body.Children.Add(DialogWindow.Text(prompt));
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0), MinHeight = 32 };
        d.Body.Children.Add(tb);
        d.AddButton("ok", Loc.T("ok"), true, true);
        d.AddButton("cancel", Loc.T("cancel"), false, false, true);
        d.ContentRendered += (_, _) =>
        {
            tb.Focus();
            int dot = initial.LastIndexOf('.');
            if (selectName && dot > 0) tb.Select(0, dot); else tb.SelectAll();
        };
        return d.Run() == "ok" ? tb.Text : null;
    }
}

/// <summary>The safety check before a run (or build) of a script with flagged commands, and before administrator runs.
/// Danger (red): no "don't warn again", and Run unlocks only after "I understand" and a 3-second pause, every time.</summary>
public static class SafetyDialog
{
    /// <summary>Choice: run | edit | cancel | source (a "Trust this source" button; Source is the pattern).</summary>
    public sealed record Answer(string Choice, bool Trust, string? Source = null);

    public const int RedDelaySeconds = 3;

    static Border Pill(string text, string brushKey)
    {
        var pt = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        pt.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var pill = new Border { Child = pt, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 10, 0) };
        pill.SetResourceReference(Border.BorderBrushProperty, brushKey);
        return pill;
    }

    public static string SevBrush(string sev) => sev == "high" ? "SystemFillColorCriticalBrush" : sev == "medium" ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush";
    public static string SevText(string sev) => sev == "high" ? Loc.T("dzHigh") : sev == "medium" ? Loc.T("dzMedium") : Loc.T("dzLow");

    /// <summary>The findings as a scrolling card (also used by the Unblock and build dialogs).</summary>
    public static UIElement FindingsCard(IReadOnlyList<DangerFinding> findings, double maxHeight = 360)
    {
        var rows = new StackPanel();
        foreach (var x in findings.Take(30))
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
            var hd = new StackPanel { Orientation = Orientation.Horizontal };
            hd.Children.Add(Pill(SevText(x.Sev), SevBrush(x.Sev)));
            var ln = DialogWindow.Text(Loc.F("dzLine", x.Line), "TextFillColorSecondaryBrush", 12);
            ln.VerticalAlignment = VerticalAlignment.Center;
            hd.Children.Add(ln);
            sp.Children.Add(hd);
            var ex = DialogWindow.Text(x.Text);
            ex.Margin = new Thickness(0, 6, 0, 6);
            sp.Children.Add(ex);
            if (x.Code.Length > 0)
            {
                var ct = new TextBlock { Text = x.Code, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                ct.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                var cb = new Border { Child = ct, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
                cb.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
                cb.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                sp.Children.Add(cb);
            }
            rows.Children.Add(sp);
        }
        if (findings.Count > 30) rows.Children.Add(DialogWindow.Text(Loc.F("dzMore", findings.Count - 30), "TextFillColorSecondaryBrush"));
        return DialogWindow.Card(new ScrollViewer { Content = rows, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    /// <summary>scan: ok | checking | unverified (the findings come from the built-in scanner only).</summary>
    public static Answer Show(string name, IReadOnlyList<DangerFinding> findings, string action, string scan = "ok", IReadOnlyList<string>? sources = null)
    {
        var d = new DialogWindow(Loc.T("dzTitle"), 580);
        bool high = Safety.Level(findings) == "high";
        bool asks = Safety.Blocking(findings).Count > 0;
        if (!asks)
        {
            d.AddHeader(Glyphs.Shield, "SystemFillColorCautionBrush", Loc.F("adminHead", name), Loc.T("adminText"));
            if (findings.Count > 0) d.Body.Children.Add(FindingsCard(findings, 240));
        }
        else
        {
            string sub = high ? Loc.T("dzSubHigh") : Loc.T("dzSubMed");
            if (action == "admin") sub += " " + Loc.T("dzAdminNote");
            d.AddHeader(Glyphs.Warning, high ? "SystemFillColorCriticalBrush" : "SystemFillColorCautionBrush", Loc.F("dzHead", name), sub);
            d.Body.Children.Add(FindingsCard(findings));
        }
        if (scan != "ok")
        {
            var n = DialogWindow.Text(Loc.T(scan == "checking" ? "dzChecking" : "dzUnverified"), "SystemFillColorCautionBrush", 12);
            n.Margin = new Thickness(0, 10, 0, 0);
            d.Body.Children.Add(n);
        }
        var guard = DialogWindow.Text(Loc.T("dzGuardrail"), "TextFillColorSecondaryBrush", 12);
        guard.Margin = new Thickness(0, 10, 0, 0);
        d.Body.Children.Add(guard);
        string? picked = null;
        if (sources != null && sources.Count > 0 && action != "build")
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var src in sources)
            {
                var b = new Button { Content = Loc.F("dzTrustSource", src), Margin = new Thickness(0, 0, 8, 4), ToolTip = Loc.T("dzTrustSourceTip") };
                string p = src;
                b.Click += (_, _) => { picked = p; d.DialogResult = true; };
                wp.Children.Add(b);
            }
            d.Body.Children.Add(wp);
        }
        var understand = new CheckBox { Content = Loc.T("dzUnderstand"), Margin = new Thickness(0, 16, 0, 0) };
        var trust = new CheckBox { Content = Loc.T("dzTrust"), Margin = new Thickness(0, 10, 0, 0) };
        if (high) d.Body.Children.Add(understand);
        // Red scripts cannot be trusted; builds never trust; unverified findings are not worth remembering
        bool canTrust = !high && asks && action != "build" && scan == "ok";
        if (canTrust) d.Body.Children.Add(trust);
        else if (high)
        {
            var nt = DialogWindow.Text(Loc.T("dzNoTrustRed"), "TextFillColorSecondaryBrush", 12);
            nt.Margin = new Thickness(0, 10, 0, 0);
            d.Body.Children.Add(nt);
        }
        d.AddLeftButton("edit", Loc.T("dzEdit"));
        string runText = action == "admin" ? Loc.T("runAdmin") : action == "build" ? Loc.T("dzReviewed") : Loc.T("dzRun");
        var run = d.AddButton("run", runText, true);
        var cancel = d.AddButton("cancel", Loc.T("cancel"), false, true, true);
        if (high)
        {
            // A deliberate action every time: tick "I understand", and Run unlocks after a short pause
            int left = RedDelaySeconds;
            run.IsEnabled = false;
            run.Content = runText + " (" + left + ")";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void Update() { run.Content = left > 0 ? runText + " (" + left + ")" : runText; run.IsEnabled = left <= 0 && understand.IsChecked == true; }
            timer.Tick += (_, _) => { left--; Update(); if (left <= 0) timer.Stop(); };
            understand.Click += (_, _) => Update();
            d.ContentRendered += (_, _) => timer.Start();
            d.Closed += (_, _) => timer.Stop();
        }
        d.ContentRendered += (_, _) => cancel.Focus();
        string r = d.Run();
        if (picked != null) return new Answer("source", false, picked);
        return new Answer(r, canTrust && trust.IsChecked == true);
    }
}

/// <summary>A deliberate confirmation before building an .exe from a flagged script: the user types the script's name or BUILD.</summary>
public static class BuildConfirmDialog
{
    public static bool Show(string name, IReadOnlyList<DangerFinding> findings, ZoneInfo? zone, string scan)
    {
        var d = new DialogWindow(Loc.T("buildExe"), 580);
        d.AddHeader(Glyphs.Build, "SystemFillColorCautionBrush", Loc.F("bcHead", name), Loc.T("bcSub"));
        if (zone != null) d.Body.Children.Add(DialogWindow.Text(Loc.F("bcZone", zone.Source.Length > 0 ? zone.Source : Loc.T("motwNoUrl")), "SystemFillColorCautionBrush"));
        if (scan != "ok") d.Body.Children.Add(DialogWindow.Text(Loc.T(scan == "checking" ? "dzChecking" : "dzUnverified"), "SystemFillColorCautionBrush", 12));
        var blocking = findings.Where(f => f.Sev != "low").ToList();
        if (blocking.Count > 0) { var c = SafetyDialog.FindingsCard(blocking, 220); ((FrameworkElement)c).Margin = new Thickness(0, 8, 0, 0); d.Body.Children.Add(c); }
        var note = DialogWindow.Text(Loc.T("bcNote"), "TextFillColorSecondaryBrush", 12);
        note.Margin = new Thickness(0, 12, 0, 0);
        d.Body.Children.Add(note);
        string bare = name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        var prompt = DialogWindow.Text(Loc.F("bcType", bare));
        prompt.Margin = new Thickness(0, 12, 0, 0);
        d.Body.Children.Add(prompt);
        var tb = new TextBox { Margin = new Thickness(0, 8, 0, 0), MinHeight = 32 };
        d.Body.Children.Add(tb);
        var ok = d.AddButton("ok", Loc.T("dzBuild"), true);
        d.AddButton("cancel", Loc.T("cancel"), false, true, true);
        ok.IsEnabled = false;
        tb.TextChanged += (_, _) => ok.IsEnabled = Matches(tb.Text, name);
        d.ContentRendered += (_, _) => tb.Focus();
        return d.Run() == "ok" && Matches(tb.Text, name);
    }

    /// <summary>The typed text: the script's name (with or without .ps1) or BUILD.</summary>
    public static bool Matches(string typed, string name)
    {
        string t = (typed ?? "").Trim();
        string bare = name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return t == "BUILD" || string.Equals(t, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t, bare, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>"Downloaded from the internet": the script (or a local file it loads) carries Mark of the Web.
/// It does not run or build until the user unblocks it here.</summary>
public static class MotwDialog
{
    /// <summary>unblock | edit | cancel</summary>
    public static string Show(string name, ZoneInfo zone, IReadOnlyList<(string Path, ZoneInfo Zone)> others, IReadOnlyList<DangerFinding> findings, string scan)
    {
        var d = new DialogWindow(Loc.T("motwTitle"), 580);
        d.ContentDialogLayout();
        d.AddHeader(Glyphs.Download, "SystemFillColorCautionBrush", Loc.F("motwHead", name), Loc.T("motwSub"));
        var grid = new StackPanel();
        void Field(string label, string value)
        {
            if (value.Length == 0) return;
            grid.Children.Add(DialogWindow.Text(label, "TextFillColorSecondaryBrush", 12));
            var v = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            v.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
            grid.Children.Add(v);
        }
        Field(Loc.T("motwHost"), zone.HostUrl);
        Field(Loc.T("motwReferrer"), zone.ReferrerUrl);
        if (zone.Source.Length == 0) grid.Children.Add(DialogWindow.Text(Loc.T("motwNoUrl"), "TextFillColorSecondaryBrush", 12));
        foreach (var (p, z) in others) Field(Loc.T("motwAlso"), p + (z.Source.Length > 0 ? "\n" + z.Source : ""));
        d.Body.Children.Add(DialogWindow.Card(grid));
        var scanHead = DialogWindow.Text(Loc.T("motwScan"), null, 14);
        scanHead.Margin = new Thickness(0, 16, 0, 6);
        scanHead.FontWeight = FontWeights.SemiBold;
        d.Body.Children.Add(scanHead);
        if (findings.Count > 0) d.Body.Children.Add(SafetyDialog.FindingsCard(findings, 240));
        else d.Body.Children.Add(DialogWindow.Text(Loc.T(scan == "ok" ? "motwScanClean" : scan == "checking" ? "dzChecking" : "dzUnverified")));
        var note = DialogWindow.Text(Loc.T("motwNote"), "TextFillColorSecondaryBrush", 12);
        note.Margin = new Thickness(0, 12, 0, 0);
        d.Body.Children.Add(note);
        d.AddLeftButton("edit", Loc.T("dzEdit"));
        d.AddButton("unblock", Loc.T("motwUnblock"));
        var cancel = d.AddButton("cancel", Loc.T("cancel"), true, true, true);
        d.ContentRendered += (_, _) => cancel.Focus();
        return d.Run();
    }
}

/// <summary>The one-time disclaimer at the first start (and from About). firstRun: I understand / Exit.</summary>
public static class DisclaimerDialog
{
    public static bool Show(bool firstRun)
    {
        var d = new DialogWindow(Loc.T("discTitle"), 520);
        d.ContentDialogLayout();
        d.AddHeader(Glyphs.Shield, "AccentTextFillColorPrimaryBrush", Loc.T("discHead"), null);
        foreach (var k in new[] { "discRights", "discSandbox", "discCheck", "discTrust", "discLiability" })
        {
            var t = DialogWindow.Text("•  " + Loc.T(k));
            t.Margin = new Thickness(0, 0, 0, 8);
            d.Body.Children.Add(t);
        }
        if (firstRun)
        {
            d.AddButton("ok", Loc.T("discAccept"), true, true);
            d.AddButton("cancel", Loc.T("discExit"), false, false, true);
            return d.Run() == "ok";
        }
        d.AddButton("ok", Loc.T("close"), true, true, true);
        d.Run();
        return true;
    }
}
