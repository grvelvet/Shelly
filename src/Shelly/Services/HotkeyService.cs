using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Global hotkey (RegisterHotKey) that brings Shelly to the front from anywhere.</summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int Id = 0x5E11;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    readonly IntPtr hwnd;
    bool registered;
    public string Current { get; private set; } = "";
    /// <summary>State text for Settings and Compatibility.</summary>
    // The state is kept as a key and an argument, so the text follows a language switch
    string stateKey = "", stateArg = "";
    public string StateText => stateKey.Length == 0 ? "" : Loc.F(stateKey, stateArg);
    /// <summary>True when the state is worth showing (the hotkey could not be registered or is invalid).</summary>
    public bool StateIsProblem => !Ok && stateKey.Length > 0;
    public bool Ok { get; private set; }

    public HotkeyService(Window w)
    {
        var h = new WindowInteropHelper(w);
        hwnd = h.EnsureHandle();
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }

    IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == WM_HOTKEY && wp.ToInt32() == Id) { App.ShowMain(); handled = true; }
        return IntPtr.Zero;
    }

    /// <summary>"Ctrl+Alt+L" -> (mods, key); null when it is not a valid global hotkey.</summary>
    public static (ModifierKeys Mods, Key Key)? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        ModifierKeys m = 0;
        Key k = Key.None;
        foreach (var raw in text.Split('+'))
        {
            string p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": m |= ModifierKeys.Control; break;
                case "alt": m |= ModifierKeys.Alt; break;
                case "shift": m |= ModifierKeys.Shift; break;
                case "win": case "windows": m |= ModifierKeys.Windows; break;
                default:
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    if (!Enum.TryParse(p, true, out k)) return null;
                    break;
            }
        }
        if (k == Key.None || (m & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return null;
        return (m, k);
    }

    public static string Format(ModifierKeys m, Key k)
    {
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        string ks = k >= Key.D0 && k <= Key.D9 ? ((int)(k - Key.D0)).ToString() : k.ToString();
        parts.Add(ks);
        return string.Join("+", parts);
    }

    /// <summary>Register the hotkey; "" turns it off. Returns false when it is taken (the old one is kept then).</summary>
    public bool Apply(string text, bool quiet = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Unregister();
            Current = "";
            Ok = true;
            (stateKey, stateArg) = ("setHotkeyIsOff", "");
            return true;
        }
        var parsed = Parse(text);
        if (parsed == null) { (stateKey, stateArg) = ("setHotkeyInvalid", ""); return false; }
        var (m, k) = parsed.Value;
        uint mods = MOD_NOREPEAT;
        if (m.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        string name = Format(m, k);
        bool wasRegistered = registered;
        string old = Current;
        Unregister();
        if (RegisterHotKey(hwnd, Id, mods, vk))
        {
            registered = true;
            Current = name;
            Ok = true;
            (stateKey, stateArg) = ("hotkeyOk", name);
            return true;
        }
        Ok = false;
        (stateKey, stateArg) = ("hotkeyBusy", name);
        if (!quiet) App.Status(Loc.F("setHotkeyBusy", name), true);
        if (wasRegistered && old.Length > 0 && old != name) Apply(old, true);
        return false;
    }

    void Unregister()
    {
        if (registered) { UnregisterHotKey(hwnd, Id); registered = false; }
    }

    public void Dispose() => Unregister();
}
