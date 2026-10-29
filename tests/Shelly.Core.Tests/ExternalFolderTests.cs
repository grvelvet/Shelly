using Xunit;

namespace Shelly.Core.Tests;

public class ExternalFolderTests
{
    static (AppPaths Paths, string Ext) Setup()
    {
        string root = TestUtil.TempDir();
        var paths = new AppPaths(Path.Combine(root, "shelly"));
        paths.EnsureFolders();
        string ext = Path.Combine(root, "reports");
        Directory.CreateDirectory(ext);
        return (paths, ext);
    }

    static void Write(string dir, string name, string text = "Write-Host 1")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), text);
    }

    [Fact]
    public void Scan_lists_scripts_of_an_external_folder_next_to_the_regular_ones()
    {
        var (paths, ext) = Setup();
        Write(paths.Scripts, "a.ps1");
        Write(ext, "run.ps1");
        Write(Path.Combine(ext, "Sub"), "deep.ps1");
        Write(ext, "notes.txt");
        Write(ext, ".hidden.ps1");
        var files = ScriptCatalog.Scan(paths, new[] { ext });
        Assert.Equal(3, files.Count);
        string key = ExternalScripts.RootKey(ext);
        var run = files.Single(f => f.Name == "run");
        Assert.Equal(key + "\\run.ps1", run.Rel);
        Assert.Equal(key, run.Folder);
        Assert.Equal(Path.Combine(ext, "run.ps1"), run.Path);
        Assert.True(run.IsExternal);
        Assert.Equal(ext, run.ExternalRoot);
        Assert.NotEqual("", run.Hash);
        var deep = files.Single(f => f.Name == "deep");
        Assert.Equal(key + "\\Sub\\deep.ps1", deep.Rel);
        Assert.Equal(key + "\\Sub", deep.Folder);
        var regular = files.Single(f => f.Name == "a");
        Assert.False(regular.IsExternal);
        Assert.Equal("a.ps1", regular.Rel);
        // the scripts run in their own folder: the working directory is the folder of Path
        Assert.Equal(ext, Path.GetDirectoryName(run.Path));
    }

    [Fact]
    public void Scan_without_external_folders_is_the_same_as_before()
    {
        var (paths, ext) = Setup();
        Write(paths.Scripts, "a.ps1");
        Write(ext, "run.ps1");
        Assert.Equal(new[] { "a.ps1" }, ScriptCatalog.Scan(paths).Select(f => f.Rel));
        Assert.Equal(new[] { "a.ps1" }, ScriptCatalog.Scan(paths, Array.Empty<string>()).Select(f => f.Rel));
    }

    [Fact]
    public void Rel_of_an_external_script_never_clashes()
    {
        var (paths, ext) = Setup();
        string ext2 = Path.Combine(Path.GetDirectoryName(ext)!, "other");
        string key = ExternalScripts.RootKey(ext);
        // a regular script with the same file name, and a regular folder named like the id
        Write(paths.Scripts, "run.ps1");
        Write(Path.Combine(paths.Scripts, key.Replace(':', '_')), "run.ps1");
        Write(ext, "run.ps1");
        Write(ext2, "run.ps1");
        var files = ScriptCatalog.Scan(paths, new[] { ext, ext2 });
        Assert.Equal(4, files.Count);
        Assert.Equal(4, files.Select(f => f.Rel).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files.Where(f => f.IsExternal), f => Assert.Contains(':', f.Rel));   // a colon cannot be part of a Windows file name
        Assert.All(files.Where(f => !f.IsExternal), f => Assert.DoesNotContain(':', f.Rel));
        // the same content in two places is trusted separately
        var a = files.Single(f => f.Rel == "run.ps1"); var b = files.Single(f => f.ExternalRoot == ext);
        Assert.Equal(a.Hash, b.Hash);
        Assert.NotEqual(Safety.TrustKeyOf(a.Rel, a.Hash), Safety.TrustKeyOf(b.Rel, b.Hash));
        Assert.NotEqual(ExternalScripts.RootKey(ext), ExternalScripts.RootKey(ext2));
        Assert.Equal(ExternalScripts.RootKey(ext), ExternalScripts.RootKey(ext.ToUpperInvariant()));   // the id does not depend on the case of the path
    }

    [Fact]
    public void Missing_or_unreadable_folders_are_skipped_without_error()
    {
        var (paths, ext) = Setup();
        Write(paths.Scripts, "a.ps1");
        string file = Path.Combine(Path.GetDirectoryName(ext)!, "afile.txt");
        File.WriteAllText(file, "x");
        var files = ScriptCatalog.Scan(paths, new[] { Path.Combine(ext, "gone"), file, "relative\\path", "" });
        Assert.Equal(new[] { "a.ps1" }, files.Select(f => f.Rel));
        // a subfolder that cannot be read: the rest of the folder is still listed
        Write(ext, "ok.ps1");
        string locked = Path.Combine(ext, "locked");
        Write(locked, "hidden.ps1");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(locked, UnixFileMode.None);
            try { Assert.Contains(ScriptCatalog.Scan(paths, new[] { ext }), f => f.Name == "ok"); }
            finally { File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        }
    }

    [Fact]
    public void Links_inside_an_external_folder_are_not_followed()
    {
        var (paths, ext) = Setup();
        string outside = Path.Combine(Path.GetDirectoryName(ext)!, "outside");
        Write(outside, "secret.ps1");
        Write(ext, "ok.ps1");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(ext, "dirlink"), outside);
            File.CreateSymbolicLink(Path.Combine(ext, "filelink.ps1"), Path.Combine(outside, "secret.ps1"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // Windows without the right to create links
        Assert.Equal(new[] { "ok" }, ScriptCatalog.Scan(paths, new[] { ext }).Select(f => f.Name));
    }

    [Fact]
    public void Number_of_files_and_folders_per_external_folder_is_limited()
    {
        var (paths, ext) = Setup();
        for (int i = 0; i < ExternalScripts.MaxFilesPerFolder + 5; i++) File.WriteAllText(Path.Combine(ext, "s" + i + ".ps1"), "");
        Assert.Equal(ExternalScripts.MaxFilesPerFolder, ScriptCatalog.Scan(paths, new[] { ext }).Count);
        string wide = Path.Combine(Path.GetDirectoryName(ext)!, "wide");
        for (int i = 0; i < 30; i++) Directory.CreateDirectory(Path.Combine(wide, "d" + i));
        Write(Path.Combine(wide, "d0"), "first.ps1");
        Assert.Contains(ScriptCatalog.Scan(paths, new[] { wide }), f => f.Name == "first");
    }

    [Fact]
    public void Usable_drops_folders_that_would_list_a_script_twice()
    {
        var (paths, ext) = Setup();
        string sub = Path.Combine(ext, "inner");
        Directory.CreateDirectory(sub);
        string inScripts = Path.Combine(paths.Scripts, "lib");
        Directory.CreateDirectory(inScripts);
        var list = ExternalScripts.Usable(paths.Scripts, new[] { ext, ext.ToUpperInvariant() + "\\", sub, inScripts, paths.Scripts, Path.GetDirectoryName(paths.Scripts)!, Path.Combine(ext, "nope") });
        Assert.Equal(new[] { ext }, list);
    }

    [Fact]
    public void Check_validates_a_folder_before_it_is_added()
    {
        var (paths, ext) = Setup();
        string other = Path.Combine(Path.GetDirectoryName(ext)!, "other");
        Directory.CreateDirectory(other);
        string Check(string? p, params string[] existing) => ExternalScripts.Check(p, paths.Scripts, existing, out _);
        Assert.Equal("", Check(ext));
        Assert.Equal("", Check(" " + ext + Path.DirectorySeparatorChar + " "));
        ExternalScripts.Check(ext + Path.DirectorySeparatorChar, paths.Scripts, Array.Empty<string>(), out string full);
        Assert.Equal(ext, full);   // normalized: no trailing separator
        Assert.Equal("extInvalid", Check(null));
        Assert.Equal("extInvalid", Check("  "));
        Assert.Equal("extNotAbsolute", Check("reports"));
        Assert.Equal("extNotAbsolute", Check("..\\reports"));
        Assert.Equal("extMissing", Check(Path.Combine(ext, "nope")));
        Assert.Equal("extDuplicate", Check(ext, ext));
        if (OperatingSystem.IsWindows()) Assert.Equal("extDuplicate", Check(ext.ToUpperInvariant(), ext));   // paths are not case sensitive there
        Assert.Equal("extInsideScripts", Check(paths.Scripts));
        Directory.CreateDirectory(Path.Combine(paths.Scripts, "lib"));
        Assert.Equal("extInsideScripts", Check(Path.Combine(paths.Scripts, "lib")));
        Assert.Equal("extContainsScripts", Check(Path.GetDirectoryName(paths.Scripts)!));
        Directory.CreateDirectory(Path.Combine(ext, "inner"));
        Assert.Equal("extOverlap", Check(Path.Combine(ext, "inner"), ext));
        Assert.Equal("extOverlap", Check(ext, Path.Combine(ext, "inner")));
        Assert.Equal("", Check(other, ext));
        var many = Enumerable.Range(0, ExternalScripts.MaxFolders).Select(i => Path.Combine(Path.GetTempPath(), "shelly-x" + i)).ToArray();
        Assert.Equal("extTooMany", Check(other, many));
    }

    [Fact]
    public void Settings_keep_clean_external_folders_and_save_them()
    {
        var (paths, ext) = Setup();
        string gone = Path.Combine(Path.GetDirectoryName(ext)!, "unplugged");   // not there now: kept, the drive may be back later
        var s = new Settings();
        string many = string.Join(",", Enumerable.Range(0, 30).Select(i => Settings.JsonString(Path.Combine(Path.GetTempPath(), "shelly-m" + i))));
        s.LoadJson("{\"externalFolders\":[" + Settings.JsonString(ext + Path.DirectorySeparatorChar) + "," + Settings.JsonString(ext.ToUpperInvariant()) + ",\"relative\\\\dir\"," + Settings.JsonString(gone) + ",\"\"]}");
        Assert.Equal(new[] { ext, gone }, s.ExternalFolders);
        var t = new Settings();
        t.LoadJson("{\"externalFolders\":[" + many + "]}");
        Assert.Equal(ExternalScripts.MaxFolders, t.ExternalFolders.Count);
        string file = Path.Combine(paths.Root, "launcher-settings.json");
        s.Save(file);
        Assert.Contains("\"externalFolders\"", File.ReadAllText(file));
        Assert.Equal(new[] { ext, gone }, Settings.Load(file).ExternalFolders);
        Assert.Empty(new Settings().ExternalFolders);
        var one = new Settings();
        one.LoadJson("{\"externalFolders\":" + Settings.JsonString(ext) + "}");
        Assert.Equal(new[] { ext }, one.ExternalFolders);
    }

    [Fact]
    public void Resolve_maps_a_rel_back_to_its_folder_and_nothing_else()
    {
        var (_, ext) = Setup();
        string key = ExternalScripts.RootKey(ext);
        var roots = new[] { ext };
        Assert.Equal(ext, ExternalScripts.Resolve(roots, key));
        Assert.Equal(Path.Combine(ext, "Sub", "x.ps1"), ExternalScripts.Resolve(roots, key + "\\Sub\\x.ps1"));
        Assert.Null(ExternalScripts.Resolve(roots, key + "\\..\\x.ps1"));
        Assert.Null(ExternalScripts.Resolve(roots, key + "\\Sub\\..\\..\\x.ps1"));
        Assert.Null(ExternalScripts.Resolve(roots, key + "\\"));
        Assert.Null(ExternalScripts.Resolve(roots, "ext:000000000000\\x.ps1"));
        Assert.Null(ExternalScripts.Resolve(roots, "Sub\\x.ps1"));
        Assert.True(ExternalScripts.IsExternalRel(key + "\\x.ps1"));
        Assert.False(ExternalScripts.IsExternalRel("x.ps1"));
    }

    [Fact]
    public void Removing_a_folder_forgets_the_settings_of_its_scripts_only()
    {
        var (_, ext) = Setup();
        string key = ExternalScripts.RootKey(ext);
        string x = key + "\\a.ps1";
        var s = new Settings();
        s.SetFavorite(x, true); s.SetFavorite("a.ps1", true);
        s.SetEngineChoice(x, "7"); s.SetEngineChoice("a.ps1", "7");
        s.SetIcon(x, "assets\\i.png"); s.SetIcon("a.ps1", "assets\\j.png");
        s.Trusted.Add(x.ToLowerInvariant() + "|ABC"); s.Trusted.Add("a.ps1|ABC");
        s.LastRuns.Add(x + "|ok|0|2026-10-18T10:00:00|5|inline"); s.LastRuns.Add("a.ps1|ok|0|2026-10-18T10:00:00|5|inline");
        RunPreset.Set(s, x, "window", new Dictionary<string, object>()); RunPreset.Set(s, "a.ps1", "window", new Dictionary<string, object>());
        s.CollapsedGroups.Add(key); s.CollapsedGroups.Add(key + "\\Sub"); s.CollapsedGroups.Add("Sub");
        s.DropFolder(key);
        Assert.Equal(new[] { "a.ps1" }, s.Favorites);
        Assert.Equal(new[] { "a.ps1|7" }, s.Engines);
        Assert.Equal(new[] { "a.ps1|assets\\j.png" }, s.ScriptIcons);
        Assert.Equal(new[] { "a.ps1|ABC" }, s.Trusted);
        Assert.Single(s.LastRuns);
        Assert.Null(RunPreset.Get(s, x));
        Assert.NotNull(RunPreset.Get(s, "a.ps1"));
        Assert.Equal(new[] { "Sub" }, s.CollapsedGroups);
    }

    [Fact]
    public void The_list_shows_each_folder_as_a_titled_group()
    {
        var (paths, ext) = Setup();
        string other = Path.Combine(Path.GetDirectoryName(ext)!, "more", "reports");   // same name as the first folder
        Write(paths.Scripts, "a.ps1");
        Write(ext, "r.ps1");
        Write(Path.Combine(ext, "Sub", "Deep"), "d.ps1");
        Directory.CreateDirectory(Path.Combine(ext, "EmptySub"));
        Directory.CreateDirectory(other);
        var roots = ExternalScripts.Usable(paths.Scripts, new[] { ext, other });
        var scripts = ScriptCatalog.Scan(paths, roots);
        string key = ExternalScripts.RootKey(ext), key2 = ExternalScripts.RootKey(other);
        var folders = ExternalScripts.FolderKeys(roots, scripts);
        Assert.Equal(new[] { key, key + "\\Sub", key + "\\Sub\\Deep", key2 }, folders);   // empty subfolders are not listed, an empty folder still is
        Loc.Language = "en";
        var groups = ExternalScripts.Groups(roots);
        Assert.Equal(new[] { key, key2 }, groups.Select(g => g.Key));
        Assert.Equal(new[] { ext, other }, groups.Select(g => g.Root));
        Assert.All(groups, g => Assert.Contains(g.Root, g.Title));   // same folder name twice: the full path tells them apart
        var single = ExternalScripts.Groups(new[] { ext });
        Assert.Equal("reports", single[0].Title);
        var list = ScriptGrouping.Build(scripts, folders, Array.Empty<ScriptFile>(), new HashSet<string>(), "fav", "root", null, single.ToDictionary(g => g.Key, g => g.Title));
        Assert.Equal(single[0].Title, list.Single(e => e.IsGroup && e.Key == key).Title);
        Assert.Equal("Sub", list.Single(e => e.IsGroup && e.Key == key + "\\Sub").Title);
        Assert.Equal(2, list.Single(e => e.IsGroup && e.Key == key).Count);   // r.ps1 and d.ps1 count for the folder
        Assert.Equal(2, list.Single(e => e.IsGroup && e.Key == key + "\\Sub\\Deep").Depth);
        Assert.Contains(list, e => !e.IsGroup && e.File!.Rel == "a.ps1");
    }

    [Fact]
    public void Texts_exist_in_both_languages()
    {
        string[] keys = { "setExtFolders", "setExtNote", "setExtNone", "setExtAdd", "setExtRemove", "setExtPick", "setExtMissing", "extGroup", "extAdded", "extRemoved",
            "extReadOnly", "extInvalid", "extNotAbsolute", "extMissing", "extDuplicate", "extInsideScripts", "extContainsScripts", "extOverlap", "extTooMany" };
        try
        {
            foreach (var k in keys)
            {
                Loc.Language = "en"; string en = Loc.T(k);
                Loc.Language = "ru"; string ru = Loc.T(k);
                Assert.NotEqual(k, en);
                Assert.NotEqual(k, ru);
                Assert.NotEqual(en, ru);
            }
        }
        finally { Loc.Language = "en"; }
    }

    static string RepoFile(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(new[] { d.FullName }.Concat(parts).ToArray());
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Dropped_folder_on_main_window_connects_as_external_folder()
    {
        string mainXaml = RepoFile("src", "Shelly", "Views", "MainWindow.xaml");
        string mainCs = RepoFile("src", "Shelly", "Views", "MainWindow.xaml.cs");
        string actionsCs = RepoFile("src", "Shelly", "Views", "ScriptActions.cs");

        Assert.Contains("x:Name=\"DropOverlay\"", mainXaml);
        Assert.Contains("AllowDrop = true;", mainCs);
        Assert.Contains("PreviewDragOver += OnDragFiles;", mainCs);
        Assert.Contains("PreviewDrop +=", mainCs);
        Assert.Contains("SettingsPage.TryAddExternalFolder(dir)", actionsCs);
        Assert.DoesNotContain("ImportFolder(dirs[0]);", actionsCs);

        try
        {
            Loc.Language = "en";
            Assert.Contains("external folder", Loc.T("dropHintNote"));
            Loc.Language = "ru";
            Assert.Contains("подключается как внешняя", Loc.T("dropHintNote"));
        }
        finally { Loc.Language = "en"; }
    }
}
