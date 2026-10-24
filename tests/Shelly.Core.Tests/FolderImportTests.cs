using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.1.1 "Import folder…": what is copied, the size limits, the name, the all-or-nothing copy, hidden scripts and
/// the delete guard.</summary>
public class FolderImportTests
{
    static string Write(string dir, string rel, string text = "x")
    {
        string p = Path.Combine(dir, rel.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    static (AppPaths Paths, string Src) Setup()
    {
        string root = TestUtil.TempDir();
        var paths = new AppPaths(Path.Combine(root, "shelly"));
        paths.EnsureFolders();
        string src = Path.Combine(root, "MyTool");
        Write(src, "main.py", "print(1)");
        Write(src, "run.ps1", "Write-Host 1");
        Write(src, "config.json", "{}");
        Write(src, "lib\\helper.py", "x = 1");
        Write(src, "lib\\_private.py", "y = 2");
        Write(src, "data\\input.csv", "a,b");
        return (paths, src);
    }

    static string[] Rels(IEnumerable<string> list) => list.Select(x => x.Replace(Path.DirectorySeparatorChar, '\\')).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    static bool NoTempLeft(string scripts) => !Directory.EnumerateDirectories(scripts, FolderImport.TempPrefix + "*").Any();

    // ---------- What is copied ----------

    [Fact]
    public void Plan_skips_environments_caches_git_and_node_modules()
    {
        var (_, src) = Setup();
        Write(src, ".venv\\pyvenv.cfg", "version = 3.12.1");
        Write(src, ".venv\\Scripts\\python.exe");
        Write(src, "env\\pyvenv.cfg");
        Write(src, "env\\Lib\\site.py");
        Write(src, "venv\\notes.txt");                  // a "venv" folder WITHOUT pyvenv.cfg is ordinary data
        Write(src, "__pycache__\\main.cpython-312.pyc");
        Write(src, ".git\\config");
        Write(src, "node_modules\\x\\index.js");
        Write(src, "lib\\__pycache__\\helper.pyc");
        var plan = FolderImport.Scan(src);
        Assert.Equal(new[] { "config.json", "data\\input.csv", "lib\\_private.py", "lib\\helper.py", "main.py", "run.ps1", "venv\\notes.txt" }, Rels(plan.Files));
        Assert.Equal(new[] { "lib\\helper.py", "main.py", "run.ps1" }, Rels(plan.Scripts));   // what the list would show
        Assert.Equal(6, plan.SkippedFolders);
        Assert.False(plan.IsLarge);
        Assert.Equal(plan.Files.Sum(f => new FileInfo(Path.Combine(src, f)).Length), plan.Bytes);
    }

    [Fact]
    public void Plan_does_not_follow_links_and_stops_at_the_depth_limit()
    {
        var (_, src) = Setup();
        string outside = Path.Combine(TestUtil.TempDir(), "secret");
        Write(outside, "keys.txt", "secret");
        bool links = true;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(src, "linked"), outside);
            File.CreateSymbolicLink(Path.Combine(src, "keys.txt"), Path.Combine(outside, "keys.txt"));
        }
        catch (Exception) { links = false; }   // Windows without the symlink privilege: the depth part still runs
        string deep = string.Join("\\", Enumerable.Range(1, ScriptCatalog.MaxDepth + 1).Select(i => "d" + i));
        Write(src, deep + "\\too-deep.py");
        Write(src, string.Join("\\", Enumerable.Range(1, ScriptCatalog.MaxDepth).Select(i => "d" + i)) + "\\deepest-ok.py");
        var plan = FolderImport.Scan(src);
        Assert.DoesNotContain(plan.Files, f => f.Contains("keys.txt") || f.Contains("linked"));
        if (links) Assert.Equal(2, plan.SkippedLinks);
        Assert.DoesNotContain(plan.Files, f => f.EndsWith("too-deep.py"));
        Assert.Contains(plan.Files, f => f.EndsWith("deepest-ok.py"));
    }

    [Fact]
    public void Large_means_over_100_MB_or_over_2000_files()
    {
        Assert.Equal(100L * 1024 * 1024, FolderImport.LargeBytes);
        Assert.Equal(2000, FolderImport.LargeFiles);
        var (_, src) = Setup();
        Assert.False(FolderImport.Scan(src).IsLarge);
        string big = Path.Combine(src, "data", "big.bin");
        using (var fs = File.Create(big)) fs.SetLength(FolderImport.LargeBytes + 1);   // sparse where the file system allows it
        Assert.True(FolderImport.Scan(src).IsLarge);
        File.Delete(big);

        var many = Path.Combine(TestUtil.TempDir(), "many");
        Write(many, "a.py");
        for (int i = 0; i < FolderImport.LargeFiles - 1; i++) Write(many, "f\\" + i + ".txt");
        Assert.False(FolderImport.Scan(many).IsLarge);   // exactly 2000 files
        Write(many, "f\\one-more.txt");
        var plan = FolderImport.Scan(many);
        Assert.Equal(FolderImport.LargeFiles + 1, plan.Files.Count);
        Assert.True(plan.IsLarge);
    }

    // ---------- Names and sources ----------

    [Fact]
    public void Name_conflicts_and_bad_names_are_reported()
    {
        var (paths, src) = Setup();
        Directory.CreateDirectory(Path.Combine(paths.Scripts, "MyTool"));
        File.WriteAllText(Path.Combine(paths.Scripts, "notes"), "");
        Assert.Equal("fiExists", FolderImport.CheckName(paths.Scripts, "MyTool", out _));
        Assert.Equal("fiExists", FolderImport.CheckName(paths.Scripts, "notes", out _));
        Assert.Equal("fnChars", FolderImport.CheckName(paths.Scripts, "a/b", out _));
        Assert.Equal("fnEmpty", FolderImport.CheckName(paths.Scripts, "  ", out _));
        Assert.Equal("fnHidden", FolderImport.CheckName(paths.Scripts, ".x", out _));
        Assert.Equal("fnReserved", FolderImport.CheckName(paths.Scripts, "CON", out _));
        Assert.Equal("", FolderImport.CheckName(paths.Scripts, " New one ", out var clean));
        Assert.Equal("New one", clean);
        Assert.Equal("MyTool (2)", FolderImport.SuggestName(paths.Scripts, src));
        Assert.Equal("Imported", FolderImport.SuggestName(paths.Scripts, Path.Combine(TestUtil.TempDir(), "...")));   // nothing usable left
    }

    [Fact]
    public void Source_inside_Scripts_or_containing_it_is_refused()
    {
        var (paths, src) = Setup();
        Assert.Equal("", FolderImport.CheckSource(paths.Scripts, src));
        Assert.Equal("fiMissing", FolderImport.CheckSource(paths.Scripts, Path.Combine(src, "nope")));
        Assert.Equal("fiMissing", FolderImport.CheckSource(paths.Scripts, ""));
        Directory.CreateDirectory(Path.Combine(paths.Scripts, "Sub"));
        Assert.Equal("fiInsideScripts", FolderImport.CheckSource(paths.Scripts, Path.Combine(paths.Scripts, "Sub")));
        Assert.Equal("fiInsideScripts", FolderImport.CheckSource(paths.Scripts, paths.Scripts));
        Assert.Equal("fiContainsScripts", FolderImport.CheckSource(paths.Scripts, Path.GetDirectoryName(paths.Scripts)!));
    }

    // ---------- Copy: all or nothing ----------

    [Fact]
    public void Copy_puts_the_whole_folder_into_Scripts_with_a_manifest()
    {
        var (paths, src) = Setup();
        Write(src, ".venv\\pyvenv.cfg");
        var plan = FolderImport.Scan(src);
        string target = FolderImport.Copy(plan, paths.Scripts, "MyTool", new[] { "run.ps1" });
        Assert.Equal(Path.Combine(paths.Scripts, "MyTool"), target);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "config.json")));
        Assert.True(File.Exists(Path.Combine(target, "data", "input.csv")));
        Assert.False(Directory.Exists(Path.Combine(target, ".venv")));
        var m = FolderImport.Manifest.Read(target)!;
        Assert.Equal(new[] { "run.ps1" }, m.Hidden);
        Assert.Equal(plan.Source, m.Source);
        Assert.True(NoTempLeft(paths.Scripts));
        // The list: the scripts of the folder carry it as their project, the unticked one is not shown, the source stays as it was
        var files = ScriptCatalog.Scan(paths);
        Assert.Equal(new[] { "MyTool\\lib\\helper.py", "MyTool\\main.py" }, files.Select(f => f.Rel).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(files, f => Assert.Equal(target, f.ProjectRoot));
        Assert.True(File.Exists(Path.Combine(target, "run.ps1")));   // hidden, not deleted
        Assert.True(File.Exists(Path.Combine(src, "run.ps1")));
        // the working folder of a run is the script's own folder
        Assert.Equal(Path.Combine(target, "lib"), Path.GetDirectoryName(files.Single(f => f.Name == "helper").Path));
    }

    [Fact]
    public void Cancel_in_the_middle_leaves_nothing_behind()
    {
        var (paths, src) = Setup();
        var plan = FolderImport.Scan(src);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(n => { if (n == 2) cts.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => FolderImport.Copy(plan, paths.Scripts, "MyTool", Array.Empty<string>(), cts.Token, progress));
        Assert.False(Directory.Exists(Path.Combine(paths.Scripts, "MyTool")));
        Assert.True(NoTempLeft(paths.Scripts));
        Assert.Empty(ScriptCatalog.Scan(paths));
    }

    [Fact]
    public void A_failing_file_leaves_nothing_behind()
    {
        var (paths, src) = Setup();
        var plan = FolderImport.Scan(src);
        File.Delete(Path.Combine(src, "run.ps1"));   // gone between the dialog and the copy
        Assert.ThrowsAny<IOException>(() => FolderImport.Copy(plan, paths.Scripts, "MyTool", Array.Empty<string>()));
        Assert.False(Directory.Exists(Path.Combine(paths.Scripts, "MyTool")));
        Assert.True(NoTempLeft(paths.Scripts));
    }

    [Fact]
    public void A_name_taken_during_the_copy_is_not_overwritten()
    {
        var (paths, src) = Setup();
        var plan = FolderImport.Scan(src);
        string target = Path.Combine(paths.Scripts, "MyTool");
        var progress = new SyncProgress(n => { if (n == 1) Write(target, "mine.txt", "keep"); });
        var ex = Assert.ThrowsAny<IOException>(() => FolderImport.Copy(plan, paths.Scripts, "MyTool", Array.Empty<string>(), default, progress));
        Assert.Equal("fiExists", ex.Message);
        Assert.Equal(new[] { "mine.txt" }, Directory.GetFiles(target).Select(Path.GetFileName).ToArray());
        Assert.True(NoTempLeft(paths.Scripts));
        Assert.Throws<InvalidOperationException>(() => FolderImport.Copy(plan, paths.Scripts, "MyTool", Array.Empty<string>()));
    }

    [Fact]
    public void Stale_temporary_folders_are_cleaned_up_and_never_listed()
    {
        var (paths, _) = Setup();
        string stale = Path.Combine(paths.Scripts, FolderImport.TempPrefix + "abc");
        Write(stale, "half.py");
        Assert.Empty(ScriptCatalog.Scan(paths));   // a dot folder: never in the list
        Assert.Equal(0, FolderImport.CleanupStale(paths.Scripts, TimeSpan.FromHours(1)));   // too new: maybe still being copied
        Assert.Equal(1, FolderImport.CleanupStale(paths.Scripts, TimeSpan.Zero));
        Assert.False(Directory.Exists(stale));
    }

    sealed class SyncProgress(Action<int> a) : IProgress<int> { public void Report(int v) => a(v); }

    // ---------- Hidden scripts and the manifest ----------

    [Fact]
    public void Hiding_one_script_keeps_the_file_and_the_others()
    {
        var (paths, src) = Setup();
        string target = FolderImport.Copy(FolderImport.Scan(src), paths.Scripts, "MyTool", Array.Empty<string>());
        Assert.Equal(3, ScriptCatalog.Scan(paths).Count);
        FolderImport.Hide(target, Path.Combine(target, "lib", "helper.py"));
        FolderImport.Hide(target, Path.Combine(target, "lib", "helper.py"));   // twice: once in the list
        Assert.Equal(new[] { "lib\\helper.py" }, FolderImport.Manifest.Read(target)!.Hidden);
        Assert.Equal(new[] { "MyTool\\main.py", "MyTool\\run.ps1" }, ScriptCatalog.Scan(paths).Select(f => f.Rel).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.True(File.Exists(Path.Combine(target, "lib", "helper.py")));
    }

    [Fact]
    public void A_damaged_manifest_still_marks_the_folder_and_hides_nothing()
    {
        var (paths, _) = Setup();
        string dir = Path.Combine(paths.Scripts, "Tool");
        Write(dir, "a.py");
        Write(dir, FolderImport.ManifestName, "{ not json");
        Write(paths.Scripts, "plain.ps1");
        var files = ScriptCatalog.Scan(paths);
        Assert.Equal(dir, files.Single(f => f.Name == "a").ProjectRoot);
        Assert.False(files.Single(f => f.Name == "plain").InProject);
        Assert.Empty(FolderImport.Manifest.Read(dir)!.Hidden);
        Assert.Null(FolderImport.Manifest.Read(paths.Scripts + Path.DirectorySeparatorChar + "nope"));
    }

    [Fact]
    public void Scripts_in_external_folders_are_never_projects()
    {
        var (paths, src) = Setup();
        Write(src, FolderImport.ManifestName, "{\"hidden\": [\"main.py\"]}");
        var files = ScriptCatalog.Scan(paths, new[] { src });
        Assert.All(files, f => Assert.False(f.InProject));
        Assert.Contains(files, f => f.Name == "main");
    }

    // ---------- Delete guard ----------

    [Fact]
    public void Only_an_imported_folder_strictly_inside_Scripts_may_be_deleted()
    {
        var (paths, src) = Setup();
        string target = FolderImport.Copy(FolderImport.Scan(src), paths.Scripts, "MyTool", Array.Empty<string>());
        Assert.True(FolderImport.CanDelete(paths.Scripts, target));
        Assert.False(FolderImport.CanDelete(paths.Scripts, paths.Scripts));                       // Scripts itself
        Assert.False(FolderImport.CanDelete(paths.Scripts, Path.GetDirectoryName(paths.Scripts)!)); // above it
        Assert.False(FolderImport.CanDelete(paths.Scripts, Path.Combine(paths.Scripts, "MyTool", "..", "..")));
        Write(src, FolderImport.ManifestName, "{}");
        Assert.False(FolderImport.CanDelete(paths.Scripts, src));                                   // outside Scripts, even with a manifest
        string plain = Path.Combine(paths.Scripts, "Plain");
        Write(plain, "a.ps1");
        Assert.False(FolderImport.CanDelete(paths.Scripts, plain));                                 // not imported
        string link = Path.Combine(paths.Scripts, "Link");
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Assert.False(FolderImport.CanDelete(paths.Scripts, link));                              // a link to an imported folder
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        var (files, bytes) = FolderImport.Measure(target);
        Assert.Equal(7, files);   // 6 files + the manifest
        Assert.True(bytes > 0);
    }

    [Fact]
    public void Size_text_is_short()
    {
        string old = Loc.Language;
        try
        {
            Loc.SetLanguage("en");
            Assert.Equal("1 KB", FolderImport.SizeText(10));
            Assert.Equal("2 MB", FolderImport.SizeText(2 * 1024 * 1024));
            Loc.SetLanguage("ru");
            Assert.EndsWith("ГБ", FolderImport.SizeText(3L * 1024 * 1024 * 1024));
            foreach (var k in new[] { "cmdImportFolder", "fiImport", "fiAddExternal", "fiImportAnyway", "projMarkTip", "projDelFolder", "projHideOne" })
                Assert.DoesNotContain("\u2014", Loc.T(k));
            Assert.Equal("Импортировать папку…", Loc.T("cmdImportFolder"));
            Assert.Equal("Скрипт со своими файлами", Loc.T("projMarkTip"));
        }
        finally { Loc.SetLanguage(old); }
    }
}
