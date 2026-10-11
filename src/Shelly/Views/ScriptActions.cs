using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>What can be done with a script or a folder: the same actions for list rows, tiles, menus and keys.</summary>
public static class ScriptActions
{
    static AppState St => App.S;

    public static void Run(ScriptFile f, string? mode = null, string origin = "list") => _ = St.Runs.RunAsync(f, new RunOptions { Mode = mode, Origin = origin });
    public static void RunSettings(ScriptFile f) => _ = St.Runs.RunAsync(f, new RunOptions { Ask = true });
    public static void Edit(ScriptFile f) => App.OpenEditor(f.Path);

    public static void Stop(ScriptFile f)
    {
        var s = St.Runs.LatestOf(f.Rel);
        if (s != null) St.Runs.Stop(s);
    }

    public static void ToggleFavorite(ScriptFile f)
    {
        bool on = !St.Settings.IsFavorite(f.Rel);
        St.Settings.SetFavorite(f.Rel, on);
        St.NotifySettingsChanged();
        St.NotifyScriptsChanged();
        JumpLists.Update();
        App.Status(Loc.F(on ? "favAdded" : "favRemoved", f.Name));
    }

    public static void ChangeIcon(ScriptFile f)
    {
        var d = new OpenFileDialog { Filter = Loc.T("iconFilter"), Title = Loc.T("ctxIcon") };
        if (d.ShowDialog(App.OwnerWindow()) != true) return;
        try
        {
            string rel = IconUtil.Import(St.Paths, f.Rel, d.FileName);
            St.Settings.SetIcon(f.Rel, rel);
            St.NotifySettingsChanged();
            St.NotifyScriptsChanged();
            JumpLists.Update();
            App.Status(Loc.F("iconSet", f.Name));
        }
        catch (Exception ex) { App.Status(Loc.F("iconFail", ex.Message), true); }
    }

    public static void ResetIcon(ScriptFile f)
    {
        string? old = St.IconPath(f.Rel);
        St.Settings.SetIcon(f.Rel, null);
        if (old != null && old.StartsWith(St.Paths.IconsDir, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(old); } catch { } }
        St.NotifySettingsChanged();
        St.NotifyScriptsChanged();
        JumpLists.Update();
        App.Status(Loc.F("iconReset", f.Name));
    }

    static void NameError(ScriptCatalog.NameResult r)
    {
        string text = r.Error switch
        {
            "reserved" => Loc.F("nameReserved", r.Name),
            "escape" => Loc.T("nameEscape"),
            "nofolder" => Loc.T("nameNoFolder"),
            _ => Loc.T("nameBad")
        };
        Dlg.Message(Loc.T("nameTitle"), text, Glyphs.Warning, "SystemFillColorCautionBrush");
    }

    public static void NewScript(string folderRel = "")
    {
        string prompt = folderRel.Length > 0 ? Loc.F("newHerePrompt", folderRel) : Loc.T("newPrompt");
        string? answer = Dlg.Input(Loc.T("newTitle"), prompt);
        if (answer == null) return;
        if (folderRel.Length > 0) answer = folderRel + "\\" + answer;
        var r = ScriptCatalog.ResolveNewScript(St.Paths.Scripts, answer);
        if (r == null) return;
        if (r.Error.Length > 0) { NameError(r); return; }
        string path = Path.Combine(r.Dir!, r.Name + ".ps1");
        if (File.Exists(path)) { Dlg.Message(Loc.T("newTitle"), Loc.F("exists", r.Name), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        try
        {
            Directory.CreateDirectory(r.Dir!);
            File.WriteAllText(path, ScriptCatalog.Template(r.Name), new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) { App.Status(Loc.F("createFail", ex.Message), true); return; }
        St.RefreshScripts();
        App.Win?.SelectScript(St.Paths.Rel(path));
        App.Status(Loc.F("created", r.Name));
        App.OpenEditor(path);
    }

    public static void Rename(ScriptFile f)
    {
        if (St.Runs.IsRunning(f.Rel)) { App.Status(Loc.F("fileLocked", f.Name), true); return; }
        string? answer = Dlg.Input(Loc.T("renameTitle"), Loc.F("renamePrompt", f.Name), f.Name);
        if (answer == null) return;
        var r = ScriptCatalog.CheckRename(answer);
        if (r == null || r.Name == f.Name) return;
        if (r.Error.Length > 0) { NameError(r); return; }
        string dir = Path.GetDirectoryName(f.Path)!;
        string np = Path.Combine(dir, r.Name + ".ps1");
        bool caseOnly = string.Equals(np, f.Path, StringComparison.OrdinalIgnoreCase);
        if (File.Exists(np) && !caseOnly) { Dlg.Message(Loc.T("renameTitle"), Loc.F("exists", r.Name), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        try
        {
            if (caseOnly) { string tmp = np + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 6); File.Move(f.Path, tmp); File.Move(tmp, np); }
            else File.Move(f.Path, np);
        }
        catch (Exception ex) { App.Status(Loc.F("renameFail", ex.Message), true); return; }
        string newRel = St.Paths.Rel(np);
        St.Settings.MoveScript(f.Rel, newRel);
        St.MoveLastRun(f.Rel, newRel);
        St.History.MoveRel(f.Rel, newRel);
        St.NotifySettingsChanged();
        St.RefreshScripts();
        JumpLists.Update();
        App.Win?.SelectScript(newRel);
        App.Status(Loc.F("renamed", f.Name, r.Name));
    }

    public static void Delete(ScriptFile f)
    {
        if (St.Runs.IsRunning(f.Rel)) { App.Status(Loc.F("fileLocked", f.Name), true); return; }
        if (!Dlg.Confirm(Loc.T("deleteTitle"), Loc.F("deletePrompt", f.Name), "", Loc.T("delete"), true, Glyphs.Delete)) return;
        try { ShellOps.RecycleFile(f.Path); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { App.Status(Loc.F("deleteFail", ex.Message), true); return; }
        St.Settings.MoveScript(f.Rel, null);
        St.MoveLastRun(f.Rel, null);
        St.NotifySettingsChanged();
        St.RefreshScripts();
        JumpLists.Update();
        App.Status(Loc.F("deleted", f.Name));
    }

    public static async void Build(ScriptFile f)
    {
        if (ExeBuilder.FindCsc() == null) { Dlg.Message(Loc.T("buildExe"), Loc.T("cscMissing"), Glyphs.Error, "SystemFillColorCriticalBrush"); return; }
        var meta = St.Meta(f);
        if (meta.Pending) await St.Analysis.WaitAsync(f, TimeSpan.FromSeconds(30));
        meta = St.Meta(f);
        // Downloaded scripts are unblocked first; red scripts go through the full danger review (no trust is used or
        // given: a built .exe runs without Shelly's checks); anything flagged needs the typed confirmation
        if (!St.Runs.MotwGate(f, meta)) { App.Status(Loc.T("buildCancelled")); return; }
        if (Safety.Level(meta.Danger) == "high")
        {
            var review = SafetyDialog.Show(f.Name, meta.Danger, "build", meta.Scan);
            if (review.Choice == "edit") { App.OpenEditor(f.Path); return; }
            if (review.Choice != "run") { App.Status(Loc.T("buildCancelled")); return; }
        }
        if (Safety.Blocking(meta.Danger).Count > 0 || meta.Zone != null || !meta.Verified)
        {
            if (!BuildConfirmDialog.Show(f.Name, meta.Danger, meta.Zone, meta.Scan)) { App.Status(Loc.T("buildCancelled")); return; }
        }
        string outPath = ExeBuilder.OutPath(St.Paths, f);
        if (File.Exists(outPath))
        {
            if (!Dlg.Confirm(Loc.T("overwriteTitle"), Loc.T("overwriteTitle"), Loc.F("overwriteText", outPath), Loc.T("overwrite"))) { App.Status(Loc.T("buildCancelled")); return; }
            if (!ExeBuilder.IsWritable(outPath)) { App.Status(Loc.F("fileLocked", Path.GetFileName(outPath)), true); return; }
        }
        string choice = St.Settings.GetEngineChoice(f.Rel);
        string engine = choice == "auto" ? meta.Engine.Engine : choice;
        App.Status(Loc.F("buildExeWait", f.Name));
        string? icon = IconUtil.IcoForBuild(St.IconPath(f.Rel)) ?? (File.Exists(St.Paths.AppIcon) ? St.Paths.AppIcon : null);
        var r = await Task.Run(() => ExeBuilder.Build(f.Path, outPath, engine, icon, "1.0.0.0"));
        AppLog.Write(Loc.F("buildHeader", f.Name, EngineInfo.Name(engine)) + (r.Log.Length > 0 ? "\n" + r.Log : ""));
        if (r.Ok)
        {
            App.Status(Loc.F("built", St.Paths.Rel(outPath, St.Paths.Root)));
            ShellOps.ShowInExplorer(outPath);
        }
        else Dlg.Message(Loc.T("buildExe"), Loc.T("buildFailed").TrimStart('!', ' ') + (r.Log.Length > 0 ? "\n\n" + r.Log : ""), Glyphs.Error, "SystemFillColorCriticalBrush");
    }

    public static void ShowRuns(ScriptFile f)
    {
        var e = St.History.LastOf(f.Rel);
        if (e != null) App.Win?.ShowHistory(e);
        else App.Status(Loc.T("histEmpty"));
    }

    // ---------- Folders ----------

    public static void NewFolder(string parentRel = "")
    {
        string where = parentRel.Length > 0 ? parentRel : "Scripts";
        string? name = Dlg.Input(Loc.T("folderNewTitle"), Loc.F("folderNewPrompt", where));
        if (name == null) return;
        string err = ScriptCatalog.CheckFolderName(name, out var clean);
        if (err.Length > 0) { Dlg.Message(Loc.T("folderNewTitle"), Loc.F(err, err == "fnLong" ? 100 : clean), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        string dir = Path.Combine(parentRel.Length > 0 ? St.Paths.FullFromRel(parentRel) : St.Paths.Scripts, clean);
        if (Directory.Exists(dir) || File.Exists(dir)) { Dlg.Message(Loc.T("folderNewTitle"), Loc.F("fnExists", clean), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { App.Status(Loc.F("folderFail", ex.Message), true); return; }
        St.RefreshScripts();
        App.Status(Loc.F("folderCreated", St.Paths.Rel(dir)));
    }

    static bool FolderBusy(string folderRel) =>
        St.Runs.Active.Any(r => r.Script.Rel.StartsWith(folderRel + "\\", StringComparison.OrdinalIgnoreCase));

    public static void RenameFolder(string folderRel)
    {
        if (FolderBusy(folderRel)) { App.Status(Loc.F("folderBusy", folderRel), true); return; }
        string oldName = Path.GetFileName(folderRel);
        string? name = Dlg.Input(Loc.T("folderRenameTitle"), Loc.F("folderRenamePrompt", oldName), oldName);
        if (name == null) return;
        string err = ScriptCatalog.CheckFolderName(name, out var clean);
        if (clean == oldName) return;
        if (err.Length > 0) { Dlg.Message(Loc.T("folderRenameTitle"), Loc.F(err, err == "fnLong" ? 100 : clean), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        string src = St.Paths.FullFromRel(folderRel);
        string dst = Path.Combine(Path.GetDirectoryName(src)!, clean);
        bool caseOnly = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (Directory.Exists(dst) || File.Exists(dst))) { Dlg.Message(Loc.T("folderRenameTitle"), Loc.F("fnExists", clean), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        try
        {
            if (caseOnly) { string tmp = dst + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 6); Directory.Move(src, tmp); Directory.Move(tmp, dst); }
            else Directory.Move(src, dst);
        }
        catch (Exception ex) { App.Status(Loc.F("folderOpFail", ex.Message), true); return; }
        string newRel = St.Paths.Rel(dst);
        St.Settings.MoveFolder(folderRel, newRel);
        St.ReloadLastRuns();
        St.History.MoveRel(folderRel + "\\", newRel + "\\");
        St.NotifySettingsChanged();
        St.RefreshScripts();
        JumpLists.Update();
        App.Status(Loc.F("folderRenamed", folderRel, newRel));
    }

    public static void DeleteFolder(string folderRel)
    {
        if (FolderBusy(folderRel)) { App.Status(Loc.F("folderBusy", folderRel), true); return; }
        string dir = St.Paths.FullFromRel(folderRel);
        if (!Directory.Exists(dir)) { St.RefreshScripts(); return; }
        bool empty = !Directory.EnumerateFileSystemEntries(dir).Any();
        int count = St.Scripts.Count(s => s.Rel.StartsWith(folderRel + "\\", StringComparison.OrdinalIgnoreCase));
        string text = empty ? Loc.F("folderDeleteEmpty", folderRel) : Loc.F("folderDeleteFull", folderRel, count);
        if (!Dlg.Confirm(Loc.T("folderDeleteTitle"), text, "", Loc.T("delete"), true, Glyphs.Delete)) return;
        try
        {
            if (empty) Directory.Delete(dir);
            else ShellOps.RecycleFolder(dir);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { App.Status(Loc.F("folderOpFail", ex.Message), true); return; }
        foreach (var s in St.Scripts.Where(s => s.Rel.StartsWith(folderRel + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            St.Settings.MoveScript(s.Rel, null);
            St.MoveLastRun(s.Rel, null);
        }
        St.NotifySettingsChanged();
        St.RefreshScripts();
        JumpLists.Update();
        App.Status(Loc.F(empty ? "folderDeleted" : "folderRecycled", folderRel));
    }

    // ---------- Menus ----------

    /// <summary>Icon of a menu item: the symbol font is set on this TextBlock only, the item keeps the UI font.</summary>
    public static TextBlock MenuIcon(string glyph)
    {
        var ic = new TextBlock { Text = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        ic.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return ic;
    }

    public static MenuItem Item(string text, string glyph, Action a, string? gesture = null, bool bold = false)
    {
        var mi = new MenuItem { Header = text, Icon = MenuIcon(glyph), InputGestureText = gesture ?? "" };
        if (bold) mi.FontWeight = FontWeights.SemiBold;
        mi.Click += (_, _) => a();
        return mi;
    }

    public static ContextMenu ScriptMenu(ScriptFile f)
    {
        var m = new ContextMenu();
        bool running = St.Runs.IsRunning(f.Rel);
        if (running) m.Items.Add(Item(Loc.T("stop"), Glyphs.Stop, () => Stop(f)));
        m.Items.Add(Item(Loc.T("ctxRun"), Glyphs.Play, () => Run(f), "Enter", true));
        m.Items.Add(Item(Loc.T("ctxRunWindow"), Glyphs.Window, () => Run(f, "window")));
        m.Items.Add(Item(Loc.T("ctxRunAdmin"), Glyphs.Shield, () => Run(f, "admin")));
        m.Items.Add(Item(Loc.T("rsMenu"), Glyphs.Settings, () => RunSettings(f), "Ctrl+Enter"));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("edit"), Glyphs.Edit, () => Edit(f), "Ctrl+E"));
        m.Items.Add(Item(Loc.T("ctxExternal"), Glyphs.Window, () => ShellOps.ExternalEditor(f.Path), "Ctrl+Shift+E"));
        m.Items.Add(Item(Loc.T("buildExe"), Glyphs.Build, () => Build(f), "Ctrl+B"));
        m.Items.Add(new Separator());
        bool fav = St.Settings.IsFavorite(f.Rel);
        m.Items.Add(Item(Loc.T(fav ? "favRemove" : "favAdd"), fav ? Glyphs.StarFill : Glyphs.Star, () => ToggleFavorite(f), "Ctrl+D"));
        m.Items.Add(Item(Loc.T("ctxIcon"), Glyphs.Image, () => ChangeIcon(f)));
        if (St.Settings.GetIcon(f.Rel) != null) m.Items.Add(Item(Loc.T("ctxIconReset"), Glyphs.Script, () => ResetIcon(f)));
        m.Items.Add(Item(Loc.T("ctxRuns"), Glyphs.History, () => ShowRuns(f)));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("rename"), Glyphs.Edit, () => Rename(f), "F2"));
        m.Items.Add(Item(Loc.T("ctxOpenFolder"), Glyphs.OpenFolder, () => ShellOps.ShowInExplorer(f.Path)));
        m.Items.Add(Item(Loc.T("ctxCopyPath"), Glyphs.Copy, () => { if (ShellOps.CopyText(f.Path)) App.Status(f.Path); }));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("delete"), Glyphs.Delete, () => Delete(f), "Del"));
        return m;
    }

    public static ContextMenu FolderMenu(string folderRel)
    {
        var m = new ContextMenu();
        m.Items.Add(Item(Loc.T("grpNewScript"), Glyphs.Add, () => NewScript(folderRel)));
        m.Items.Add(Item(Loc.T("grpNewFolder"), Glyphs.NewFolder, () => NewFolder(folderRel)));
        if (folderRel.Length > 0)
        {
            m.Items.Add(new Separator());
            m.Items.Add(Item(Loc.T("grpRename"), Glyphs.Edit, () => RenameFolder(folderRel)));
            m.Items.Add(Item(Loc.T("grpDelete"), Glyphs.Delete, () => DeleteFolder(folderRel)));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("grpOpen"), Glyphs.OpenFolder, () => ShellOps.OpenFolder(folderRel.Length > 0 ? St.Paths.FullFromRel(folderRel) : St.Paths.Scripts)));
        return m;
    }
}
