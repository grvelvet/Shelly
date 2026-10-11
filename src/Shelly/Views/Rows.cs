using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Views;

public abstract class RowBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void On([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        On(n);
        return true;
    }
}

/// <summary>An observable list that can swap all its items with a single Reset notification.</summary>
public sealed class RowCollection : System.Collections.ObjectModel.ObservableCollection<RowBase>
{
    public void ReplaceAll(IEnumerable<RowBase> rows)
    {
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>Group header in the script list (a folder, the root scripts or the favorites).</summary>
public sealed class GroupRow : RowBase
{
    public string Key { get; init; } = "";       // folder rel, "" = root scripts, "*fav" = favorites
    public string Title { get; init; } = "";
    public int Count { get; init; }
    public int Depth { get; init; }
    public bool IsFolder => Key.Length > 0 && Key != "*fav";
    public string Glyph => Key == "*fav" ? "\uE735" : Key.Length == 0 ? "\uE80F" : "\uE8B7";
    bool collapsed;
    public bool Collapsed { get => collapsed; set { if (Set(ref collapsed, value)) On(nameof(Chevron)); } }
    public string Chevron => Collapsed ? "\uE76C" : "\uE70D";
    public Thickness Indent => new(4 + Depth * 20, 0, 0, 0);
    public string CountText => Count.ToString();
}

/// <summary>One script: list row and favorite tile.</summary>
public sealed class ScriptRow : RowBase
{
    public ScriptFile File { get; private set; }
    public ScriptRow(ScriptFile f) { File = f; }
    public string Rel => File.Rel;
    public string Name => File.Name;
    int depth;
    public int Depth { get => depth; set { if (Set(ref depth, value)) { On(nameof(CardMargin)); On(nameof(CompactMargin)); } } }
    /// <summary>Outer margin of the row card: nested folders indent the card under their header.</summary>
    public Thickness CardMargin => new(Math.Max(0, Depth - 1) * 20, 4, 0, 4);   // 8 px between cards, as in Wintoys
    /// <summary>Compact list view: 2 px between the lines, same indent.</summary>
    public Thickness CompactMargin => new(Math.Max(0, Depth - 1) * 20, 1, 0, 1);
    /// <summary>Folder shown under the name in search results.</summary>
    public string FolderHint { get; set; } = "";

    string desc = "";
    public string Desc { get => desc; set { if (Set(ref desc, value)) On(nameof(HasDesc)); } }
    public bool HasDesc => Desc.Length > 0;
    ImageSource? icon;
    public ImageSource? Icon { get => icon; set { if (Set(ref icon, value)) On(nameof(HasIcon)); } }
    public bool HasIcon => Icon != null;
    /// <summary>Default icon glyph: from <c># Shelly-Icon: XXXX</c> in the script header, else the script glyph.</summary>
    string glyph = ScriptHeader.DefaultGlyph;
    public string Glyph { get => glyph; set => Set(ref glyph, value); }
    /// <summary>Quiet engine mark ("PS 5.1"), only when informative: see <see cref="EngineInfo.RowMark"/>.</summary>
    string engineMark = "";
    public string EngineMark { get => engineMark; set { if (Set(ref engineMark, value)) On(nameof(ShowEngine)); } }
    public bool ShowEngine => EngineMark.Length > 0;
    string engineTip = "";
    public string EngineTip { get => engineTip; set => Set(ref engineTip, value); }
    bool engineMissing;
    public bool EngineMissing { get => engineMissing; set => Set(ref engineMissing, value); }
    string lastGlyph = "";
    public string LastGlyph { get => lastGlyph; set { if (Set(ref lastGlyph, value)) On(nameof(HasLast)); } }
    public bool HasLast => LastGlyph.Length > 0;
    string lastKind = "";
    public string LastKind { get => lastKind; set => Set(ref lastKind, value); }
    string lastTip = "";
    public string LastTip { get => lastTip; set => Set(ref lastTip, value); }
    string lastLine = "";
    public string LastLine { get => lastLine; set => Set(ref lastLine, value); }
    string danger = "";
    public string Danger { get => danger; set { if (Set(ref danger, value)) On(nameof(HasDanger)); } }
    public bool HasDanger => Danger.Length > 0;
    string dangerGlyph = "\uEA18";
    /// <summary>Shield (high / medium findings), download arrow (Mark of the Web), clock (PowerShell has not checked yet),
    /// question mark (no PowerShell could check it).</summary>
    public string DangerGlyph { get => dangerGlyph; set => Set(ref dangerGlyph, value); }
    string dangerTip = "";
    public string DangerTip { get => dangerTip; set => Set(ref dangerTip, value); }
    bool fav;
    public bool IsFavorite { get => fav; set => Set(ref fav, value); }
    bool running;
    public bool IsRunning { get => running; set { if (Set(ref running, value)) On(nameof(NotRunning)); } }
    public bool NotRunning => !IsRunning;
    string elapsed = "";
    public string Elapsed { get => elapsed; set => Set(ref elapsed, value); }
    string tip = "";
    public string Tip { get => tip; set => Set(ref tip, value); }

    public void Update(AppState st, ScriptFile f)
    {
        File = f;
        var m = st.Meta(f);
        Desc = m.Desc;
        Icon = IconUtil.Load(st.IconPath(f.Rel));
        Glyph = ScriptHeader.GlyphOf(f);
        var ei = m.Engine;
        var mark = ei.RowMark(st.PwshOk, st.Ps51Ok);
        EngineMark = mark.Text;
        EngineMissing = mark.Problem;
        string et = ei.Auto ? Loc.F("engTipAuto", EngineInfo.Name(ei.Engine), ei.ReasonText()) : Loc.F("engTipManual", EngineInfo.Name(ei.Engine));
        if (EngineMissing && ei.Engine == "7") et = Loc.F("engTipMissing", EngineInfo.Name(ei.Engine));
        if (m.Pending) et += Loc.F("engTipPending", st.PwshOk ? EngineInfo.Name("7") : EngineInfo.Name("5.1"));
        EngineTip = et + "\n" + Loc.T("engMarkTip");
        var lr = st.LastRunOf(f.Rel);
        if (lr != null)
        {
            var v = RunResult.LastRunView(lr);
            LastGlyph = v.Mark; LastKind = v.Kind; LastTip = v.Tip; LastLine = v.Line;
        }
        else { LastGlyph = ""; LastKind = ""; LastTip = ""; LastLine = Loc.T("detNever"); }
        // Info findings never mark the row; the slot shows the most important state
        var shown = m.Danger.Where(d => d.Sev != "low").ToList();
        string findings = shown.Count == 0 ? "" : string.Join("\n", shown.Take(6).Select(d => Loc.F("dzLine", d.Line) + ": " + d.Text)) + (shown.Count > 6 ? "\n" + Loc.F("dzMore", shown.Count - 6) : "");
        string lvl = Safety.Level(shown);
        var tip = new List<string>();
        if (m.Zone?.Blocks == true)
        {
            Danger = "motw"; DangerGlyph = "\uE896";
            tip.Add(Loc.T("motwMark") + (m.Zone.HostUrl.Length > 0 ? "\n" + m.Zone.HostUrl : "") + (m.Zone.ReferrerUrl.Length > 0 && m.Zone.ReferrerUrl != m.Zone.HostUrl ? "\n" + m.Zone.ReferrerUrl : ""));
            tip.Add(Loc.T("motwMarkTip"));
        }
        else if (lvl.Length > 0) { Danger = lvl; DangerGlyph = "\uEA18"; }
        else if (m.Scan == "checking") { Danger = "checking"; DangerGlyph = "\uE823"; }
        else if (m.Scan == "unverified") { Danger = "unverified"; DangerGlyph = "\uE9CE"; }
        else { Danger = ""; DangerGlyph = "\uEA18"; }
        if (m.Scan != "ok") tip.Add(Loc.T(m.Scan == "checking" ? "scanChecking" : "scanUnverified"));
        if (findings.Length > 0) tip.Add(findings);
        if (m.Zone?.Note == true) tip.Add(Loc.F("motwNoteZone", m.Zone.Source));
        DangerTip = string.Join("\n\n", tip);
        IsFavorite = st.Settings.IsFavorite(f.Rel);
        var parts = new List<string> { f.Rel };
        if (m.Params.Count > 0) parts.Add(Loc.F("paramsList", string.Join(", ", m.Params.Select(p => p.Name + (p.Mandatory ? "*" : "")))));
        parts.Add(Loc.F("modified", f.Modified.ToString("g")));
        Tip = string.Join("\n", parts);
        UpdateRunning(st);
    }

    public void UpdateRunning(AppState st)
    {
        var s = st.Runs.LatestOf(Rel);
        IsRunning = s != null;
        Elapsed = s?.ElapsedText ?? "";
    }
}

/// <summary>Container style for the script list: script rows are cards, group headers are plain.</summary>
public sealed class RowStyleSelector : System.Windows.Controls.StyleSelector
{
    /// <summary>Compact list view: script rows are plain lines (CompactRowItem) instead of cards.</summary>
    public bool Compact { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        if (container is not FrameworkElement fe) return null;
        if (item is not GroupRow) return fe.TryFindResource(Compact ? "CompactRowItem" : "CardRowItem") as Style;
        // The first header sits right under the toolbar; the others get a larger top margin so they belong to their cards
        bool first = System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(container) is { } ic && ic.Items.Count > 0 && ReferenceEquals(ic.Items[0], item);
        return fe.TryFindResource(first ? "FirstGroupRowItem" : "GroupRowItem") as Style;
    }
}

/// <summary>Row templates of the script list for one view (Cards or Compact list); the page swaps the selector
/// instance when the view changes, which re-templates the realized rows.</summary>
public sealed class RowTemplateSelector : System.Windows.Controls.DataTemplateSelector
{
    public DataTemplate? Group { get; set; }
    public DataTemplate? Script { get; set; }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is GroupRow ? Group : item is ScriptRow ? Script : null;
}

/// <summary>Glyph size of an IconButton / IconToggle. The symbol font and this size are applied to the button's content
/// only, so tooltips and menus placed on the button keep the UI font.</summary>
public static class Ico
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(double), typeof(Ico), new FrameworkPropertyMetadata(15.0));
    public static double GetSize(DependencyObject d) => (double)d.GetValue(SizeProperty);
    public static void SetSize(DependencyObject d, double v) => d.SetValue(SizeProperty, v);
}

/// <summary>Two children side by side: the first takes what the second leaves, so a name trims with an ellipsis while
/// a trailing mark stays right after it (a horizontal StackPanel gives infinite width and never trims).</summary>
public sealed class HugPanel : System.Windows.Controls.Panel
{
    protected override System.Windows.Size MeasureOverride(System.Windows.Size avail)
    {
        if (InternalChildren.Count == 0) return default;
        double w2 = 0, h = 0;
        if (InternalChildren.Count > 1)
        {
            var b = InternalChildren[1];
            b.Measure(avail);
            w2 = b.DesiredSize.Width; h = b.DesiredSize.Height;
        }
        var a = InternalChildren[0];
        a.Measure(new System.Windows.Size(Math.Max(0, avail.Width - w2), avail.Height));
        return new System.Windows.Size(a.DesiredSize.Width + w2, Math.Max(h, a.DesiredSize.Height));
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size size)
    {
        if (InternalChildren.Count == 0) return size;
        double w2 = InternalChildren.Count > 1 ? InternalChildren[1].DesiredSize.Width : 0;
        double w1 = Math.Min(InternalChildren[0].DesiredSize.Width, Math.Max(0, size.Width - w2));
        InternalChildren[0].Arrange(new System.Windows.Rect(0, 0, w1, size.Height));
        if (InternalChildren.Count > 1) InternalChildren[1].Arrange(new System.Windows.Rect(w1, 0, w2, size.Height));
        return size;
    }
}
