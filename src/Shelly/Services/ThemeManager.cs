using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Light / dark / system theme (the WPF Fluent theme) and the accent color. The Fluent brushes take the accent
/// from SystemAccentColor* as static values, so Shelly replaces those brushes in the application resources: for a custom
/// accent and also for the Windows accent, after making accent text readable (contrast at least 4.5:1).</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static Color Accent { get; private set; }
    public static event Action? Changed;
    static Settings? settings;
    static bool hooked;

    public static void Apply(Settings s)
    {
        settings = s;
        var app = Application.Current;
#pragma warning disable WPF0001
        var mode = s.Theme switch { "light" => ThemeMode.Light, "dark" => ThemeMode.Dark, _ => ThemeMode.System };
        if (app.ThemeMode != mode) app.ThemeMode = mode;
#pragma warning restore WPF0001
        IsDark = s.Theme == "dark" || (s.Theme == "system" && SystemIsDark());
        ApplyAccent(s.Accent);
        ApplyShellyBrushes();
        Shelly.Views.Motion.ApplyResources();
        UpdateTexts();
        if (!hooked)
        {
            hooked = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.VisualStyle) return;
                app.Dispatcher.BeginInvoke(() => { if (settings != null) Apply(settings); });
            };
        }
        Changed?.Invoke();
        Shelly.Views.TitleBar.ApplyThemeToAll();
    }

    public static void UpdateTexts()
    {
        var r = Application.Current.Resources;
        r["ToggleOnText"] = Loc.T("toggleOn");
        r["ToggleOffText"] = Loc.T("toggleOff");
    }

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    static Color SystemAccent()
    {
        try { return SystemColors.AccentColor; } catch { return Color.FromRgb(0x00, 0x67, 0xC0); }
    }

    public static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
    }

    static void ApplyAccent(string accent)
    {
        var r = Application.Current.Resources;
        Dictionary<string, Color> v;
        if (accent == "system" || !accent.StartsWith('#'))
        {
            // The Windows accent: the shades Windows computed (fallback: Shelly's own palette)
            Color b = SystemAccent();
            var own = AccentPalette.Shades(ToRgb(b));
            v = new() { ["Base"] = b };
            v["Light1"] = Safe(() => SystemColors.AccentColorLight1, ToColor(own["Light1"]));
            v["Light2"] = Safe(() => SystemColors.AccentColorLight2, ToColor(own["Light2"]));
            v["Light3"] = Safe(() => SystemColors.AccentColorLight3, ToColor(own["Light3"]));
            v["Dark1"] = Safe(() => SystemColors.AccentColorDark1, ToColor(own["Dark1"]));
            v["Dark2"] = Safe(() => SystemColors.AccentColorDark2, ToColor(own["Dark2"]));
            v["Dark3"] = Safe(() => SystemColors.AccentColorDark3, ToColor(own["Dark3"]));
        }
        else
        {
            // A Shelly swatch: the seven shades the way Windows derives them from one color
            v = AccentPalette.Shades(ToRgb(ParseColor(accent, SystemAccent()))).ToDictionary(kv => kv.Key, kv => ToColor(kv.Value));
        }
        Accent = v["Base"];

        // Accent fills follow WinUI: Light2 with black text in the dark theme, Dark1 with white text in the light theme.
        // The fill shade is nudged (lighter / darker) if the text on it would be under 4.5:1.
        string fillKey = IsDark ? "Light2" : "Dark1";
        var fills = new Dictionary<string, Color>(v)
        {
            [fillKey] = ToColor(AccentPalette.Fill(v.ToDictionary(kv => kv.Key, kv => ToRgb(kv.Value)), IsDark)),
        };

        // Readable accent text: at least 4.5:1 against the window background (Mica / solid base)
        Color bg = IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var text = new Dictionary<string, Color>(v);
        foreach (var key in v.Keys) text[key] = EnsureContrast(v[key], bg, 4.5, IsDark);
        var table = IsDark ? AccentTable.Dark : AccentTable.Light;
        foreach (var (key, variant, opacity) in table)
        {
            bool isText = (key.Contains("Text") && !key.Contains("Selection")) || key.Contains("Hyperlink");
            Color c = (isText ? text : fills).TryGetValue(variant, out var cc) ? cc : v["Base"];
            var brush = new SolidColorBrush(c) { Opacity = opacity };
            brush.Freeze();
            r[key] = brush;
        }
        foreach (var (k, c) in v) r["ShellyAccent" + k] = c;
        r["ShellyAccentFill"] = fills[fillKey];
        r["ShellyAccentTextBrush"] = Frozen(text[IsDark ? "Light3" : "Dark2"]);

        // Text and glyphs on the accent fill (buttons, check marks, radio dots, toggle knobs)
        Color on = ToColor(AccentPalette.OnAccentText(IsDark));
        Color onSecondary = Color.FromArgb(IsDark ? (byte)0x80 : (byte)0xB3, on.R, on.G, on.B);   // WinUI TextOnAccentFillColorSecondary
        foreach (var key in OnAccentPrimary) r[key] = Frozen(on);
        foreach (var key in OnAccentSecondary) r[key] = Frozen(onSecondary);
    }

    static readonly string[] OnAccentPrimary =
    {
        "TextOnAccentFillColorPrimaryBrush", "AccentButtonForeground", "AccentButtonForegroundPointerOver", "CalendarViewTodayForeground",
        "CheckBoxCheckGlyphForeground", "DataGridRowSelectedForegroundThemeBrush", "RadioButtonCheckGlyphFill", "ToggleButtonForegroundChecked",
        "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
    };
    static readonly string[] OnAccentSecondary =
    {
        "TextOnAccentFillColorSecondaryBrush", "AccentButtonForegroundPressed", "CheckBoxCheckGlyphForegroundPressed", "ToggleButtonForegroundCheckedPressed",
    };

    static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);
    static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>Brushes of Shelly's own: the count / engine pill needs a fill that shows on a card and on the
    /// content layer (SubtleFillColorSecondary, 3.5 % black, is invisible there). Secondary text on it stays above 4.5:1.</summary>
    static void ApplyShellyBrushes()
    {
        var r = Application.Current.Resources;
        r["ShellyPillFillBrush"] = Frozen(IsDark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x00, 0x00, 0x00));
        // The content layer of the main window (WinUI NavigationView content grid): LayerFillColorDefault with the card
        // stroke, explicit so it reads as its own plane on Mica whatever the theme dictionary's values are
        r["ShellyLayerFillBrush"] = Frozen(IsDark ? Color.FromArgb(0x4C, 0x3A, 0x3A, 0x3A) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        // Floating nav pane (overlay mode): opaque, the WinUI acrylic fallback colors (AcrylicBackgroundFillColorDefault)
        r["ShellyPaneOverlayBrush"] = Frozen(IsDark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        // ContentDialog button band: a shade darker than the dialog's content area in both themes
        r["ShellyDialogBandBrush"] = Frozen(IsDark ? Color.FromArgb(0x33, 0x00, 0x00, 0x00) : Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
        // "Downloaded from the internet" (Mark of the Web): orange, apart from the yellow caution and the red danger
        r["ShellyMotwBrush"] = Frozen(IsDark ? Color.FromRgb(0xFF, 0x9A, 0x3C) : Color.FromRgb(0xC4, 0x5A, 0x00));
        r["ShellyLayerStrokeBrush"] = Frozen(IsDark ? Color.FromArgb(0x19, 0x00, 0x00, 0x00) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
    }

    static Color Safe(Func<Color> f, Color fallback) { try { return f(); } catch { return fallback; } }
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    static double Lum(Color c)
    {
        static double Ch(byte x) { double s = x / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static Color EnsureContrast(Color c, Color bg, double min, bool lighten)
    {
        for (int i = 0; i < 20 && Contrast(c, bg) < min; i++) c = Mix(c, lighten ? Colors.White : Colors.Black, 0.12);
        return c;
    }
}
