namespace Shelly.Core;

/// <summary>An sRGB color without WPF (so the palette math can be tested).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0), White = new(255, 255, 255);

    internal static Rgb Parse(string hex)
    {
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 8) h = h[2..];   // #AARRGGBB: alpha is ignored
        if (h.Length != 6) throw new FormatException(hex);
        return new(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..], 16));
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>The seven accent shades of Windows 11 (SystemAccentColor, Light1-3, Dark1-3) and the WinUI rules for using
/// them: accent fills are Light2 with black text in the dark theme and Dark1 with white text in the light theme.</summary>
public static class AccentPalette
{
    /// <summary>Lightness steps measured on the Windows default blue (#0078D4: Light1 #0091F8, Light2 #4CC2FF,
    /// Light3 #99EBFF, Dark1 #005FB8, Dark2 #003E92, Dark3 #001A68), applied in HSL so any hue keeps its character; the light shades lose a little saturation so they don't glare.</summary>
    public static Dictionary<string, Rgb> Shades(Rgb b)
    {
        var (h, s, l) = ToHsl(b);
        return new()
        {
            ["Base"] = b,
            ["Light1"] = FromHsl(h, s * 0.95, l + (1 - l) * 0.12),
            ["Light2"] = FromHsl(h, s * 0.8, l + (1 - l) * 0.40),
            ["Light3"] = FromHsl(h, s * 0.75, l + (1 - l) * 0.66),
            ["Dark1"] = FromHsl(h, s, l * 0.87),
            ["Dark2"] = FromHsl(h, s, l * 0.69),
            ["Dark3"] = FromHsl(h, s, l * 0.48),
        };
    }

    /// <summary>The accent fill of the theme (Light2 dark / Dark1 light), made lighter or darker until the text on it
    /// (black on dark theme, white on light theme, as in WinUI) has at least <paramref name="min"/>:1.</summary>
    public static Rgb Fill(IReadOnlyDictionary<string, Rgb> shades, bool dark, double min = 4.5)
    {
        Rgb fill = shades[dark ? "Light2" : "Dark1"];
        Rgb text = OnAccentText(dark);
        for (int i = 0; i < 40 && Contrast(fill, text) < min; i++) fill = Mix(fill, dark ? Rgb.White : Rgb.Black, 0.08);
        return fill;
    }

    public static Rgb OnAccentText(bool dark) => dark ? Rgb.Black : Rgb.White;

    public static Rgb Mix(Rgb a, Rgb b, double t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static double Luminance(Rgb c)
    {
        static double Ch(byte x) { double v = x / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9) { byte v = (byte)Math.Round(l * 255); return new(v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        return new((byte)Math.Round(Hue(p, q, h + 1 / 3.0) * 255), (byte)Math.Round(Hue(p, q, h) * 255), (byte)Math.Round(Hue(p, q, h - 1 / 3.0) * 255));
    }
}
