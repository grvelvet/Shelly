namespace Shelly.Core;

/// <summary>Adaptive layout rules (pure, so they're tested at 500 / 640 / 800 / 1200 px without a window).
/// Widths are in device-independent pixels.</summary>
public static class Layout
{
    /// <summary>Smallest window: the compact nav (48) + a usable list.</summary>
    public const double MinWindowWidth = 500, MinWindowHeight = 400;
    /// <summary>Below this the pane stays compact (48 px) and opens over the page. WinUI's default is 1008; Shelly's
    /// list still has room at 800 with the pane open (Wintoys is expanded at 953 px). There is no Minimal mode: the
    /// window can't get narrower than 500 px, where the 48 px strip still fits.</summary>
    public const double ExpandedThreshold = 800;
    /// <summary>Open pane 240 px (labels up to "Совместимость" fit; Wintoys uses about 170), compact 48 px.</summary>
    public const double PaneOpen = 240, PaneCompact = 48;
    public const double SearchMax = 340, SearchMin = 160;

    /// <summary>Page header toolbar left of the search box: New script, New folder, a separator (16 incl. margins),
    /// Sort, Collapse/expand all, View, Filter (32 px buttons) and an 8 px gap. In overflow mode the last four go into
    /// a "…" button.</summary>
    public const double ToolbarFull = 2 * 32 + 16 + 4 * 32 + 8, ToolbarCompact = 2 * 32 + 16 + 32 + 8;
    /// <summary>The view buttons move into "…" when the search box would get narrower than this.</summary>
    public const double SearchOverflowAt = 200;

    public enum Nav { Expanded, Compact }

    /// <summary>Pane mode for a window width. Wide windows follow the user's choice; narrow ones are always compact
    /// (the menu button then opens the pane over the content).</summary>
    public static Nav NavFor(double windowWidth, bool userCompact) =>
        windowWidth >= ExpandedThreshold && !userCompact ? Nav.Expanded : Nav.Compact;

    /// <summary>True when opening the pane should overlay the content instead of pushing it.</summary>
    public static bool NavOverlays(double windowWidth) => windowWidth < ExpandedThreshold;

    /// <summary>Search box width from the room left in the page header. Below <see cref="SearchMin"/> it becomes a
    /// search button that opens the box over the header (width 0 then).</summary>
    public static (double Width, bool Collapsed) Search(double room)
    {
        if (room < SearchMin) return (0, true);
        return (Math.Min(SearchMax, Math.Floor(room)), false);
    }

    /// <summary>Header layout for the room between the title and the right-hand buttons: first the search box shrinks
    /// (340 down to 200), then Sort / Collapse / View / Filter go into the "…" overflow menu, then the box shrinks to 160,
    /// and only then does it turn into a search button.</summary>
    public static (bool Overflow, double SearchWidth, bool SearchCollapsed) Header(double room)
    {
        if (room - ToolbarFull >= SearchOverflowAt) return (false, Math.Min(SearchMax, Math.Floor(room - ToolbarFull)), false);
        var (w, c) = Search(room - ToolbarCompact);
        return (true, w, c);
    }

    /// <summary>Which status slots of a script row fit at a list width. They drop out in order: engine mark first,
    /// then the last-run mark, then the warning shield (the most important one stays longest).</summary>
    public static (bool Engine, bool Last, bool Danger) RowSlots(double listWidth) =>
        (listWidth >= 600, listWidth >= 480, listWidth >= 400);

    /// <summary>How one open/close of the pane plays out. Everything derives from <see cref="PaneOpen"/> and
    /// <see cref="PaneCompact"/>. Inline (wide window): the pane has no surface of its own (it is Mica); the content
    /// layer's margin changes once (at the end of an open, at the start of a close) and in between the layer slides
    /// with a render transform whose edge follows the pane's edge, so the page is never relaid out per frame and the
    /// pane never covers it. Overlay (narrow window, or closing an overlay): the content stays at the compact margin
    /// and the pane slides over it on its flyout surface with a shadow.</summary>
    public readonly record struct PanePlan(double From, double To, double MarginStart, double MarginEnd,
        double ShiftFrom, double ShiftTo, bool SurfaceDuring, bool SurfaceAfter, double Labels);

    public static PanePlan Plan(double from, double to, bool overlay, double currentMargin)
    {
        double labels = to >= PaneOpen ? 1 : 0;
        double margin = overlay ? PaneCompact : to;
        if (Math.Abs(margin - currentMargin) < 0.5)
        {
            // The content doesn't move: the pane opens or closes over it (overlay)
            bool covers = Math.Max(from, to) > margin + 0.5;
            return new PanePlan(from, to, margin, margin, 0, 0, covers, to > margin + 0.5, labels);
        }
        if (margin > currentMargin)   // opening inline: slide the layer right, commit the narrower margin at the end
            return new PanePlan(from, to, currentMargin, margin, from - currentMargin, margin - currentMargin, false, false, labels);
        // closing inline: commit the wider margin now, the layer starts shifted right and slides left with the pane edge
        return new PanePlan(from, to, margin, margin, from - margin, 0, false, false, labels);
    }
}
