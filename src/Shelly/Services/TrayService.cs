using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Views;

namespace Shelly.Services;

/// <summary>Tray icon: open, favorites (run in a separate window), running scripts, exit; notifications as
/// balloon tips, which Windows 10 and 11 show as native toasts. The right-click menu is a WPF ContextMenu in the
/// Windows 11 style of the app's other menus; if it can't be shown, the WinForms menu is the fallback.</summary>
public sealed class TrayService : IDisposable
{
    readonly WinForms.NotifyIcon icon;
    Action? balloonClick;
    public bool Available { get; }

    public TrayService()
    {
        icon = new WinForms.NotifyIcon { Text = "Shelly" };
        try
        {
            icon.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
            icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) App.ShowMain(); };
            icon.MouseUp += (_, e) => { if (e.Button == WinForms.MouseButtons.Right) ShowMenu(); };
            icon.BalloonTipClicked += (_, _) => { var a = balloonClick; balloonClick = null; a?.Invoke(); };
            icon.BalloonTipClosed += (_, _) => balloonClick = null;
            icon.Visible = true;
            Available = true;
        }
        catch (Exception ex) { AppLog.Error("tray", ex); Available = false; }
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    ContextMenu? wpfMenu;
    WinForms.ContextMenuStrip? strip;

    void ShowMenu()
    {
        try { ShowWpfMenu(); }
        catch (Exception ex)
        {
            AppLog.Write("tray menu (fallback to WinForms): " + ex.Message);
            ShowStripMenu();
        }
    }

    /// <summary>The WPF menu at the pointer. A popup opened from the tray only gets keyboard focus and closes on an
    /// outside click if its window is the foreground one, hence SetForegroundWindow on the popup's HWND (the usual
    /// tray-menu trick).</summary>
    void ShowWpfMenu()
    {
        if (wpfMenu != null) wpfMenu.IsOpen = false;
        var m = new ContextMenu { Placement = PlacementMode.AbsolutePoint, StaysOpen = false };
        BuildWpf(m);
        GetCursorPos(out var pt);
        // Screen pixels → DIPs of the primary monitor (the tray is there)
        var src = App.Win != null ? PresentationSource.FromVisual(App.Win) : null;
        var m2 = src?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        if (src == null)
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            m2 = new System.Windows.Media.Matrix(96.0 / g.DpiX, 0, 0, 96.0 / g.DpiY, 0, 0);
        }
        var p = m2.Transform(new System.Windows.Point(pt.X, pt.Y));
        m.HorizontalOffset = p.X - 12;   // the template's shadow margin
        m.VerticalOffset = p.Y - 8;
        m.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(m) is HwndSource hs) SetForegroundWindow(hs.Handle);
            if (m.Items.Count > 0 && m.Items[0] is MenuItem first) first.Focus();
        };
        m.Closed += (_, _) => { if (wpfMenu == m) wpfMenu = null; };
        wpfMenu = m;
        m.IsOpen = true;
    }

    void BuildWpf(ContextMenu m)
    {
        var st = App.S;
        m.Items.Add(ScriptActions.Item(Loc.T("trayOpen"), Glyphs.Window, App.ShowMain, null, true));
        m.Items.Add(new Separator());
        var head = new MenuItem { Header = Loc.T("trayFavHead") };
        head.SetResourceReference(FrameworkElement.StyleProperty, "MenuHeaderItem");
        m.Items.Add(head);
        var favs = st.Settings.Favorites.Select(st.Find).Where(f => f != null).ToList();
        if (favs.Count == 0)
        {
            var none = new MenuItem { Header = Loc.T("trayFavNone").Trim(), IsEnabled = false };
            m.Items.Add(none);
        }
        foreach (var f in favs)
        {
            string rel = f!.Rel;
            var it = ScriptActions.Item(f.Name, ScriptHeader.GlyphOf(f), () => App.RunByRel(rel, "tray"));
            it.Header = new TextBlock { Text = f.Name };   // no access keys from underscores in names
            m.Items.Add(it);
        }
        string running = st.Runs.RunningText();
        if (running.Length > 0)
        {
            m.Items.Add(new Separator());
            m.Items.Add(new MenuItem { Header = new TextBlock { Text = running }, IsEnabled = false, Icon = ScriptActions.MenuIcon("\uE916") });
        }
        m.Items.Add(new Separator());
        m.Items.Add(ScriptActions.Item(Loc.T("trayExit"), "\uE7E8", App.Quit));
    }

    void ShowStripMenu()
    {
        strip ??= new WinForms.ContextMenuStrip();
        BuildMenu();
        var h = App.Win != null ? new WindowInteropHelper(App.Win).EnsureHandle() : IntPtr.Zero;
        if (h != IntPtr.Zero) SetForegroundWindow(h);
        strip.Show(WinForms.Cursor.Position);
    }

    void BuildMenu()
    {
        var m = strip!;
        m.Items.Clear();
        var open = new WinForms.ToolStripMenuItem(Loc.T("trayOpen")) { Font = new Font(m.Font, System.Drawing.FontStyle.Bold) };
        open.Click += (_, _) => App.ShowMain();
        m.Items.Add(open);
        m.Items.Add(new WinForms.ToolStripSeparator());
        m.Items.Add(new WinForms.ToolStripMenuItem(Loc.T("trayFavHead")) { Enabled = false });
        var st = App.S;
        var favs = st.Settings.Favorites.Select(st.Find).Where(f => f != null).ToList();
        if (favs.Count == 0) m.Items.Add(new WinForms.ToolStripMenuItem(Loc.T("trayFavNone").Trim()) { Enabled = false });
        foreach (var f in favs)
        {
            var it = new WinForms.ToolStripMenuItem("    " + f!.Name.Replace("&", "&&"));
            string rel = f.Rel;
            it.Click += (_, _) => App.Current.Dispatcher.BeginInvoke(() => App.RunByRel(rel, "tray"));
            m.Items.Add(it);
        }
        string running = st.Runs.RunningText();
        if (running.Length > 0)
        {
            m.Items.Add(new WinForms.ToolStripSeparator());
            m.Items.Add(new WinForms.ToolStripMenuItem(running) { Enabled = false });
        }
        m.Items.Add(new WinForms.ToolStripSeparator());
        var exit = new WinForms.ToolStripMenuItem(Loc.T("trayExit"));
        exit.Click += (_, _) => App.Current.Dispatcher.BeginInvoke(App.Quit);
        m.Items.Add(exit);
    }

    public void UpdateTip()
    {
        if (!Available) return;
        string r = App.S.Runs.RunningText();
        string t = r.Length > 0 ? "Shelly · " + r : "Shelly";
        icon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
    }

    public void Balloon(string title, string text, bool ok, Action? onClick)
    {
        if (!Available) return;
        balloonClick = onClick;
        try { icon.ShowBalloonTip(8000, title, text, ok ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning); } catch { }
    }

    public void Dispose()
    {
        try { if (wpfMenu != null) wpfMenu.IsOpen = false; strip?.Dispose(); icon.Visible = false; icon.Dispose(); } catch { }
    }
}
