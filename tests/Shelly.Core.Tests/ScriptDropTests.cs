using Xunit;

namespace Shelly.Core.Tests;

public class ScriptDropTests
{
    static string Write(string dir, string name, string text = "Write-Host 1")
    {
        Directory.CreateDirectory(dir);
        string p = Path.Combine(dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void Only_ps1_files_are_taken_and_the_rest_is_counted()
    {
        string root = TestUtil.TempDir();
        string scripts = Path.Combine(root, "Scripts");
        Directory.CreateDirectory(scripts);
        string a = Write(Path.Combine(root, "src"), "a.ps1");
        string b = Write(Path.Combine(root, "src"), "B.PS1");
        string txt = Write(Path.Combine(root, "src"), "notes.txt");
        string psm = Write(Path.Combine(root, "src"), "mod.psm1");
        var plan = ScriptCatalog.PlanDrop(scripts, new[] { a, b, txt, psm, Path.Combine(root, "src"), Path.Combine(root, "missing.ps1"), a });
        Assert.Equal(new[] { Path.Combine(scripts, "a.ps1"), Path.Combine(scripts, "B.ps1") }, plan.Copy.Select(c => c.Target).ToArray());
        Assert.Equal(4, plan.Ignored);   // txt, psm1, the folder and a missing file; the repeated .ps1 counts once
        Assert.Equal(0, plan.Already);
    }

    [Fact]
    public void Existing_files_are_never_overwritten()
    {
        string root = TestUtil.TempDir();
        string scripts = Path.Combine(root, "Scripts");
        Write(scripts, "tool.ps1", "old");
        string same = Write(Path.Combine(root, "s1"), "tool.ps1", "old");        // the same file again: nothing to do
        string other = Write(Path.Combine(root, "s2"), "tool.ps1", "different");  // same name, other content: a new name
        string third = Write(Path.Combine(root, "s3"), "tool.ps1", "third");      // and a third one in the same drop
        var plan = ScriptCatalog.PlanDrop(scripts, new[] { same, other, third });
        Assert.Equal(1, plan.Already);
        Assert.Equal(new[] { Path.Combine(scripts, "tool (2).ps1"), Path.Combine(scripts, "tool (3).ps1") }, plan.Copy.Select(c => c.Target).ToArray());
        Assert.Equal("old", File.ReadAllText(Path.Combine(scripts, "tool.ps1")));
    }

    [Fact]
    public void A_script_that_is_already_in_the_Scripts_folder_is_not_copied_onto_itself()
    {
        string scripts = Path.Combine(TestUtil.TempDir(), "Scripts");
        string inside = Write(Path.Combine(scripts, "Sub"), "x.ps1");
        var plan = ScriptCatalog.PlanDrop(scripts, new[] { inside });
        Assert.Empty(plan.Copy);
        Assert.Equal(1, plan.Already);
    }

    [Fact]
    public void Unusable_names_are_ignored()
    {
        string root = TestUtil.TempDir();
        string scripts = Path.Combine(root, "Scripts");
        string dot = Write(Path.Combine(root, "src"), "..ps1");
        var plan = ScriptCatalog.PlanDrop(scripts, new[] { dot });
        Assert.Empty(plan.Copy);
        Assert.Equal(1, plan.Ignored);
    }
}
