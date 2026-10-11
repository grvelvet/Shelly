namespace Shelly.Core;

/// <summary>Texts of the compact Run settings dialog (UI-free, so they are tested).</summary>
public static class RunForm
{
    /// <summary>A default worth showing literally: short, one line, not code.</summary>
    public static bool IsSimpleDefault(string v) =>
        v.Length <= 40 && !v.Contains('\n') && !v.Contains('$') && !v.StartsWith("(") && !v.StartsWith("@(") && !v.StartsWith("[");

    /// <summary>Placeholder of a parameter's text box: the literal default ("30"), "set by the script" for a computed
    /// default, otherwise the type ("number", "text"). Null for switches, booleans and ValidateSet lists, which are
    /// not text boxes. An empty box means "use the script's default", which the placeholder shows.</summary>
    public static string? Placeholder(ParamInfo p)
    {
        if (p.Kind is "switch" or "bool" || p.SetValues.Count > 0) return null;
        if (p.Default != null && IsSimpleDefault(p.Default)) return p.Default.Trim('\'', '"');
        if (p.Default != null || p.DefaultText.Length > 0) return Loc.T("phComputed");
        return p.FriendlyType();
    }

    /// <summary>Tooltip of a parameter (label and box): name · type, required, the default as written, the help text.</summary>
    public static string Tip(ParamInfo p)
    {
        var parts = new List<string> { p.Name + "  ·  " + p.FriendlyType() };
        if (p.Mandatory) parts.Add(Loc.T("tipMandatory"));
        string raw = p.DefaultText.Length > 0 ? p.DefaultText : p.Default ?? "";
        if (raw.Length > 0 && p.Kind is not ("switch" or "bool")) parts.Add(Loc.F("hintDefault", raw.Length > 200 ? raw[..200] + "…" : raw));
        if (p.Help.Length > 0) parts.Add(p.Help);
        return string.Join("\n", parts);
    }

    /// <summary>Rough height of the dialog in DIPs for a script (the layout used by RunSettingsWindow): 32 title row,
    /// 12 top padding, 56 header, 16 gap, params rows (32 each, 8 apart) + 16, engine/mode row 32, optional last-run
    /// line 8 + 20, warnings 28 each + 8, 24 bottom padding, 80 button band.</summary>
    public static double EstimateHeight(int paramRows, bool lastRun, int warnings = 0)
    {
        double h = 32 + 12 + 56 + 16;
        if (paramRows > 0) h += paramRows * 32 + (paramRows - 1) * 8 + 16;
        h += 32;
        if (lastRun) h += 8 + 20;
        if (warnings > 0) h += 8 + warnings * 28;
        return h + 24 + 80;
    }
}
