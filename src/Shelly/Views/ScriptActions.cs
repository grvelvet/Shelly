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
    public static void RunPreset(ScriptFile f, string preset) => _ = St.Runs.RunAsync(f, new RunOptions { Origin = "list", Preset = preset });

    /// <summary>The mode a plain Run uses for this script (remembered, its # Shelly-Mode: line, else the default of Settings).</summary>
    public static string EffectiveMode(ScriptFile f) =>
        RunModes.Resolve(null, "list", Core.RunPreset.Get(St.Settings, f.Rel)?.Mode, ScriptHeader.ModeOf(f), St.Settings.DefaultMode);

    /// <summary>Scripts that are gone (deleted, hidden, their folder deleted): their schedules and tasks go too.</summary>
    static void ForgetSchedules(IEnumerable<string> rels)
    {
        var gone = new List<Schedule>();
        foreach (var rel in rels) gone.AddRange(ScheduleList.ForScript(St.Settings, rel));
        if (gone.Count == 0) return;
        foreach (var s in gone)
        {
            ScheduleList.Remove(St.Settings, s.Id);
            if (App.Sched is { } svc) _ = svc.DeleteAsync(s.Id);
        }
        AppLog.Write("Schedules deleted with their scripts: " + string.Join(", ", gone.Select(s => s.Rel + " " + s.Id)));
        App.Status(Loc.F("schedForgotten", gone.Count), false);
    }
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

    public static void RevokeTrust(ScriptFile f)
    {
        if (!Safety.RemoveTrust(St.Settings, f.Rel)) return;
        St.NotifySettingsChanged();
        St.NotifyScriptsChanged();
        App.Status(Loc.F("setRevoked", f.Name));
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
        string path = Path.Combine(r.Dir!, r.Name + r.Ext);
        if (File.Exists(path)) { Dlg.Message(Loc.T("newTitle"), Loc.F("exists", r.Name + r.Ext), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        try
        {
            Directory.CreateDirectory(r.Dir!);
            // PowerShell 5.1 needs the BOM for non-English text; Python reads UTF-8 without it, and cmd.exe can't take a BOM
            File.WriteAllText(path, ScriptCatalog.Template(r.Name, r.Ext), new System.Text.UTF8Encoding(r.Ext == ScriptKinds.Ps1));
        }
        catch (Exception ex) { App.Status(Loc.F("createFail", ex.Message), true); return; }
        St.RefreshScripts();
        App.Win?.SelectScript(St.Paths.Rel(path));
        App.Status(Loc.F("created", r.Name));
        App.OpenEditor(path);
    }

    /// <summary>"Add scripts…": the same as dropping the chosen script files on the Scripts page.</summary>
    public static void PickFiles()
    {
        // *.ps1;*.py;*.bat;*.cmd and the custom interpreters' extensions
        string pattern = string.Join(";", ScriptKinds.AllExtensions.Select(e => "*" + e));
        var d = new OpenFileDialog { Filter = Loc.F("scriptsFilter", pattern), Title = Loc.T("addScriptsTitle"), Multiselect = true };
        if (d.ShowDialog(App.OwnerWindow()) != true) return;
        AddFiles(d.FileNames);
    }

    /// <summary>Files dropped on the Scripts page: the .ps1 and .py files are COPIED into the Scripts folder (never overwritten, a taken name
    /// gets " (2)"); other files are skipped. Nothing is run; a copy keeps the Mark of the Web, so the usual unblock rule applies.</summary>
    public static void AddFiles(IEnumerable<string> dropped)
    {
        // A dropped folder is imported with its files (Import folder…); the files go on as before
        var all = dropped.ToList();
        var dirs = all.Where(p => { try { return Directory.Exists(p); } catch { return false; } }).ToList();
        var files = all.Except(dirs).ToList();
        if (files.Count > 0) AddFileList(files);
        if (dirs.Count > 0)
        {
            if (dirs.Count > 1) App.Status(Loc.F("fiOneAtATime", Path.GetFileName(Path.TrimEndingDirectorySeparator(dirs[0]))));
            ImportFolder(dirs[0]);
        }
    }

    static bool importing;

    /// <summary>"Import folder…" (and a folder dropped on the Scripts page): reads the folder in the background, warns about
    /// a large one (offers to add it as an external folder), asks for the name and the scripts to show, then copies it
    /// (temporary folder + one rename, in the background) and selects the first script.</summary>
    public static async void ImportFolder(string? source = null)
    {
        if (importing) { App.Status(Loc.T("fiReading")); return; }
        if (source == null)
        {
            var pick = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("fiPickTitle") };
            if (pick.ShowDialog(App.OwnerWindow()) != true) return;
            source = pick.FolderName;
        }
        string scripts = St.Paths.Scripts;
        string err = FolderImport.CheckSource(scripts, source);
        if (err.Length > 0) { Dlg.Message(Loc.T("fiTitle"), Loc.T(err), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        importing = true;
        try
        {
            App.Status(Loc.T("fiReading"));
            FolderImport.Plan plan;
            try { plan = await Task.Run(() => FolderImport.Scan(source)); }
            catch (Exception ex) { App.Status(Loc.F("fiFail", ex.Message), true); return; }
            App.Status("");
            string title = Path.GetFileName(plan.Source);
            if (plan.Scripts.Count == 0) { Dlg.Message(Loc.T("fiTitle"), Loc.T("fiNoScripts"), Glyphs.Info); return; }
            if (plan.IsLarge)
            {
                string count = plan.Truncated ? Loc.F("fiMoreThan", FolderImport.ScanLimit) : plan.Files.Count.ToString();
                string c = Dlg.Choose(Loc.T("fiLargeTitle"), title, Loc.F("fiLargeText", title, count, FolderImport.SizeText(plan.Bytes)), Glyphs.Warning, "SystemFillColorCautionBrush",
                    new Dlg.Choice("external", Loc.T("fiAddExternal"), true), new Dlg.Choice("import", Loc.T("fiImportAnyway")), new Dlg.Choice("cancel", Loc.T("cancel")));
                if (c == "external") { SettingsPage.TryAddExternalFolder(plan.Source); return; }
                if (c != "import") return;
            }
            FolderImport.CleanupStale(scripts, TimeSpan.FromHours(1));
            var answer = ImportFolderDialog.Show(plan, scripts, FolderImport.SuggestName(scripts, plan.Source));
            if (answer == null) return;
            int total = plan.Files.Count, last = -1;
            var progress = new Progress<int>(n =>
            {
                int pct = total == 0 ? 100 : n * 100 / total;
                if (pct / 5 != last) { last = pct / 5; App.Status(Loc.F("fiCopying", answer.Name, n, total)); }
            });
            string target;
            try { target = await Task.Run(() => FolderImport.Copy(plan, scripts, answer.Name, answer.Hidden, default, progress)); }
            catch (OperationCanceledException) { App.Status(Loc.T("fiCancelled")); return; }
            catch (Exception ex)
            {
                string msg = ex.Message is "fiExists" ? Loc.T("fiExists") : ex.Message;
                AppLog.Error("import folder", ex);
                App.Status(Loc.F("fiFail", msg), true);
                return;
            }
            AppLog.Write("Imported folder " + plan.Source + " -> " + target + " (" + plan.Files.Count + " files)");
            St.RefreshScripts();
            var first = St.Scripts.FirstOrDefault(f => string.Equals(f.ProjectRoot, target, StringComparison.OrdinalIgnoreCase));
            if (first != null) App.Win?.SelectScript(first.Rel);
            App.Status(Loc.F("fiDone", answer.Name, plan.Scripts.Count - answer.Hidden.Count));
        }
        finally { importing = false; }
    }

    static void AddFileList(IEnumerable<string> dropped)
    {
        var plan = ScriptCatalog.PlanDrop(St.Paths.Scripts, dropped);
        var done = new List<string>();
        string? fail = null;
        foreach (var (src, dst) in plan.Copy)
        {
            try { Directory.CreateDirectory(St.Paths.Scripts); File.Copy(src, dst, false); done.Add(dst); }
            catch (Exception ex) { fail ??= Loc.F("dropFail", Path.GetFileName(src), ex.Message); }
        }
        if (done.Count > 0)
        {
            St.RefreshScripts();
            App.Win?.SelectScript(St.Paths.Rel(done[0]));
        }
        var parts = new List<string>();
        if (done.Count == 1) parts.Add(Loc.F("dropAdded", Path.GetFileNameWithoutExtension(done[0])));
        else if (done.Count > 1) parts.Add(Loc.F("dropAddedN", done.Count));
        if (plan.Already > 0) parts.Add(Loc.F("dropAlready", plan.Already));
        if (plan.Ignored > 0) parts.Add(Loc.F("dropIgnored", plan.Ignored));
        if (fail != null) parts.Add(fail);
        if (parts.Count > 0) App.Status(string.Join("  ", parts), plan.Ignored > 0 || fail != null);
    }

    /// <summary>Scripts of an added folder stay where they are: Shelly does not rename, delete or build them.</summary>
    static bool ReadOnly(ScriptFile f)
    {
        if (!f.IsExternal) return false;
        App.Status(Loc.T("extReadOnly"), true);
        return true;
    }

    public static void Rename(ScriptFile f)
    {
        if (ReadOnly(f)) return;
        if (St.Runs.IsRunning(f.Rel)) { App.Status(Loc.F("fileLocked", f.Name), true); return; }
        string? answer = Dlg.Input(Loc.T("renameTitle"), Loc.F("renamePrompt", f.Name), f.Name);
        if (answer == null) return;
        var r = ScriptCatalog.CheckRename(answer);
        if (r == null || r.Name == f.Name) return;
        if (r.Error.Length > 0) { NameError(r); return; }
        string dir = Path.GetDirectoryName(f.Path)!;
        // The extension stays: a rename never turns a PowerShell script into a Python one or back
        string np = Path.Combine(dir, r.Name + Path.GetExtension(f.Path));
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
        if (ReadOnly(f)) return;
        if (St.Runs.IsRunning(f.Rel)) { App.Status(Loc.F("fileLocked", f.Name), true); return; }
        if (f.InProject) { DeleteFromProject(f); return; }
        if (!Dlg.Confirm(Loc.T("deleteTitle"), Loc.F("deletePrompt", f.Name), "", Loc.T("delete"), true, Glyphs.Delete)) return;
        try { ShellOps.RecycleFile(f.Path); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { App.Status(Loc.F("deleteFail", ex.Message), true); return; }
        bool hadSchedules = ScheduleList.Has(St.Settings, f.Rel);
        ForgetSchedules(new[] { f.Rel });
        St.Settings.MoveScript(f.Rel, null);
        St.MoveLastRun(f.Rel, null);
        St.NotifySettingsChanged();
        St.RefreshScripts();
        JumpLists.Update();
        if (!hadSchedules) App.Status(Loc.F("deleted", f.Name));
    }

    /// <summary>A script of an imported folder: deleting it deletes the folder (its files belong to it). With other scripts in
    /// the folder the user can instead hide just this one. The folder goes to the Recycle Bin, only when it is an imported
    /// folder strictly inside Scripts.</summary>
    static void DeleteFromProject(ScriptFile f)
    {
        string root = f.ProjectRoot;
        string folderName = Path.GetFileName(root);
        var inFolder = St.Scripts.Where(s => string.Equals(s.ProjectRoot, root, StringComparison.OrdinalIgnoreCase)).ToList();
        var (files, bytes) = FolderImport.Measure(root);
        string size = FolderImport.SizeText(bytes);
        string choice;
        if (inFolder.Count > 1)
            choice = Dlg.Choose(Loc.T("projDelTitle"), f.Name, Loc.F("projDelTextMany", f.Name, folderName, files, size, inFolder.Count), Glyphs.Delete, "SystemFillColorCriticalBrush",
                new Dlg.Choice("folder", Loc.T("projDelFolder")), new Dlg.Choice("hide", Loc.T("projHideOne"), true), new Dlg.Choice("cancel", Loc.T("cancel")));
        else
            choice = Dlg.Confirm(Loc.T("projDelTitle"), f.Name, Loc.F("projDelText", f.Name, folderName, files, size), Loc.T("projDelFolder"), true, Glyphs.Delete) ? "folder" : "cancel";
        if (choice == "hide")
        {
            try { FolderImport.Hide(root, f.Path); }
            catch (Exception ex) { App.Status(Loc.F("deleteFail", ex.Message), true); return; }
            ForgetSchedules(new[] { f.Rel });
            St.Settings.MoveScript(f.Rel, null);
            St.MoveLastRun(f.Rel, null);
            St.NotifySettingsChanged();
            St.RefreshScripts();
            JumpLists.Update();
            App.Status(Loc.F("projHidden", f.Name));
            return;
        }
        if (choice != "folder") return;
        if (!FolderImport.CanDelete(St.Paths.Scripts, root)) { Dlg.Message(Loc.T("projDelTitle"), Loc.T("projDelUnsafe"), Glyphs.Warning, "SystemFillColorCautionBrush"); return; }
        if (inFolder.Any(s => St.Runs.IsRunning(s.Rel))) { App.Status(Loc.F("folderBusy", folderName), true); return; }
        try { ShellOps.RecycleFolder(root); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { App.Status(Loc.F("folderOpFail", ex.Message), true); return; }
        ForgetSchedules(inFolder.Select(s => s.Rel));
        foreach (var s in inFolder)
        {
            St.Settings.MoveScript(s.Rel, null);
            St.MoveLastRun(s.Rel, null);
        }
        St.NotifySettingsChanged();
        St.RefreshScripts();
        JumpLists.Update();
        App.Status(Loc.F("folderRecycled", folderName));
    }

    /// <summary>Fire and forget from a menu: an exception is logged and shown (1.3.1).</summary>
    public static void Build(ScriptFile f) => App.Fire(() => BuildAsync(f), "build");

    static async Task BuildAsync(ScriptFile f)
    {
        if (ReadOnly(f)) return;
        // The .exe wraps a PowerShell script (ExeBuilder); there is nothing like it for Python, batch or custom scripts
        if (!f.IsPowerShell) { App.Status(Loc.T("pyNoBuild"), true); return; }
        if (ExeBuilder.FindCsc() == null) { Dlg.Message(Loc.T("buildExe"), Loc.T("cscMissing"), Glyphs.Error, "SystemFillColorCriticalBrush"); return; }
        var meta = St.Meta(f);
        if (meta.Pending) await St.Analysis.WaitAsync(f, RunPlanner.AnalysisWait);
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
        if (ExternalScripts.IsExternalRel(folderRel)) { App.Status(Loc.T("extReadOnly"), true); return; }
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
        if (ExternalScripts.IsExternalRel(folderRel)) { App.Status(Loc.T("extReadOnly"), true); return; }
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
        ForgetSchedules(St.Scripts.Where(s => s.Rel.StartsWith(folderRel + "\\", StringComparison.OrdinalIgnoreCase)).Select(s => s.Rel).ToList());
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
        // The other of the two everyday modes (1.2.0: a plain Run is a separate window unless the script or Settings say otherwise)
        if (EffectiveMode(f) == "window") m.Items.Add(Item(Loc.T("ctxRunInline"), Glyphs.Play, () => Run(f, "inline")));
        else m.Items.Add(Item(Loc.T("ctxRunWindow"), Glyphs.Window, () => Run(f, "window")));
        m.Items.Add(Item(Loc.T("ctxRunAdmin"), Glyphs.Shield, () => Run(f, "admin")));
        var presets = Presets.Names(St.Settings, f.Rel);
        if (presets.Count > 0)
        {
            var pm = new MenuItem { Header = Loc.T("presetRunMenu"), Icon = MenuIcon(Glyphs.Data) };
            foreach (var n in presets)
            {
                string name = n;
                var it = Item(n, Glyphs.Play, () => RunPreset(f, name));
                it.Header = new TextBlock { Text = n };   // no access keys from underscores
                pm.Items.Add(it);
            }
            m.Items.Add(pm);
        }
        m.Items.Add(Item(Loc.T("rsMenu"), Glyphs.Settings, () => RunSettings(f), "Ctrl+Enter"));
        m.Items.Add(ScheduleMenu(f));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("edit"), Glyphs.Edit, () => Edit(f), "Ctrl+E"));
        m.Items.Add(Item(Loc.T("ctxExternal"), Glyphs.Window, () => ShellOps.ExternalEditor(f.Path), "Ctrl+Shift+E"));
        if (!f.IsExternal && !f.IsPowerShell)
        {
            // Shown but unavailable, with the reason in the tooltip
            var b = Item(Loc.T("buildExe"), Glyphs.Build, () => { }, "Ctrl+B");
            b.IsEnabled = false;
            b.ToolTip = Loc.T("pyNoBuild");
            ToolTipService.SetShowOnDisabled(b, true);
            m.Items.Add(b);
        }
        else if (!f.IsExternal) m.Items.Add(Item(Loc.T("buildExe"), Glyphs.Build, () => Build(f), "Ctrl+B"));
        m.Items.Add(new Separator());
        bool fav = St.Settings.IsFavorite(f.Rel);
        m.Items.Add(Item(Loc.T(fav ? "favRemove" : "favAdd"), fav ? Glyphs.StarFill : Glyphs.Star, () => ToggleFavorite(f), "Ctrl+D"));
        if (Safety.IsTrusted(St.Settings, f.Rel, f.Path, f.Hash))
            m.Items.Add(Item(Loc.T("ctxUntrust"), Glyphs.Shield, () => RevokeTrust(f)));
        m.Items.Add(Item(Loc.T("ctxIcon"), Glyphs.Image, () => ChangeIcon(f)));
        if (St.Settings.GetIcon(f.Rel) != null) m.Items.Add(Item(Loc.T("ctxIconReset"), Glyphs.Script, () => ResetIcon(f)));
        m.Items.Add(Item(Loc.T("ctxRuns"), Glyphs.History, () => ShowRuns(f)));
        m.Items.Add(new Separator());
        if (!f.IsExternal) m.Items.Add(Item(Loc.T("rename"), Glyphs.Edit, () => Rename(f), "F2"));
        m.Items.Add(Item(Loc.T("ctxOpenFolder"), Glyphs.OpenFolder, () => ShellOps.ShowInExplorer(f.Path)));
        m.Items.Add(Item(Loc.T("ctxCopyPath"), Glyphs.Copy, () => { if (ShellOps.CopyText(f.Path)) App.Status(f.Path); }));
        if (!f.IsExternal)
        {
            m.Items.Add(new Separator());
            m.Items.Add(Item(Loc.T("delete"), Glyphs.Delete, () => Delete(f), "Del"));
        }
        return m;
    }

    /// <summary>"Schedule…" (no schedule yet), or a submenu with the script's schedules and "Add schedule…".</summary>
    static MenuItem ScheduleMenu(ScriptFile f)
    {
        var list = ScheduleList.ForScript(St.Settings, f.Rel);
        if (list.Count == 0) return Item(Loc.T("schedMenu"), Glyphs.Clock, () => ScheduleDialog.Show(f, null));
        var m = new MenuItem { Header = Loc.T("schedTitle"), Icon = MenuIcon(Glyphs.Clock) };
        foreach (var s in list)
        {
            var sc = s;
            string text = s.Describe() + (s.Preset.Length > 0 ? " · " + Loc.F("schedPresetPart", s.Preset) : "") + (s.Enabled ? "" : " · " + Loc.T("schedIsOff"));
            var it = Item(text, Glyphs.Edit, () => ScheduleDialog.Show(f, sc));
            it.Header = new TextBlock { Text = text };
            it.ToolTip = Loc.T("schedEditTip");
            m.Items.Add(it);
        }
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("schedMenuAdd"), Glyphs.Add, () => ScheduleDialog.Show(f, null)));
        return m;
    }

    public static ContextMenu FolderMenu(string folderRel)
    {
        var m = new ContextMenu();
        bool external = ExternalScripts.IsExternalRel(folderRel);
        if (!external)
        {
            m.Items.Add(Item(Loc.T("grpNewScript"), Glyphs.Add, () => NewScript(folderRel)));
            m.Items.Add(Item(Loc.T("grpNewFolder"), Glyphs.NewFolder, () => NewFolder(folderRel)));
            if (folderRel.Length == 0)
            {
                m.Items.Add(Item(Loc.T("cmdAddScripts"), Glyphs.OpenFolder, PickFiles));
                m.Items.Add(Item(Loc.T("cmdImportFolder"), Glyphs.Folder, () => ImportFolder()));
            }
        }
        if (folderRel.Length > 0 && !external)
        {
            m.Items.Add(new Separator());
            m.Items.Add(Item(Loc.T("grpRename"), Glyphs.Edit, () => RenameFolder(folderRel)));
            m.Items.Add(Item(Loc.T("grpDelete"), Glyphs.Delete, () => DeleteFolder(folderRel)));
        }
        if (!external) m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("grpOpen"), Glyphs.OpenFolder, () => ShellOps.OpenFolder(St.FolderPath(folderRel))));
        return m;
    }
}
