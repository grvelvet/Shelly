using System.Globalization;
using Xunit;

namespace Shelly.Core.Tests;

public class ScriptErrorAndLanguageTests
{
    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("ru-BY", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("uk-UA", "en")]
    [InlineData("", "en")]
    public void Language_follows_the_system_culture(string culture, string expected) =>
        Assert.Equal(expected, Loc.LanguageFor(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Saved_language_is_told_from_a_first_start()
    {
        string dir = TestUtil.TempDir();
        Assert.False(Settings.Load(Path.Combine(dir, "none.json")).LanguageSaved);
        var s = new Settings();
        s.LoadJson("{\"theme\":\"dark\"}");
        Assert.False(s.LanguageSaved);
        s = new Settings();
        s.LoadJson("{\"language\":\"ru\"}");
        Assert.True(s.LanguageSaved);
        Assert.Equal("ru", s.Language);
        s = new Settings();
        s.LoadJson("{\"language\":\"de\"}");   // a saved value, even an unknown one, is not replaced by the system language
        Assert.True(s.LanguageSaved);
    }

    [Fact]
    public void New_texts_exist_in_both_languages_and_the_tray_hint_is_short()
    {
        try
        {
            foreach (var lang in new[] { "en", "ru" })
            {
                Loc.Language = lang;
                foreach (var k in new[] { "logRunError", "logRunWindow", "trayFavNone", "compatTitle", "compatAllOk", "p7Check" })
                    Assert.NotEqual(k, Loc.T(k));
                string line = Loc.F("logRunError", "Backup.ps1", 3);
                Assert.Contains("Backup.ps1", line);
                Assert.Contains("3", line);
                string hint = Loc.T("trayFavNone");
                Assert.Equal(hint.Trim(), hint);
                Assert.True(hint.Length <= 24, hint);
            }
        }
        finally { Loc.Language = "en"; }
        Assert.False(Loc.Has("navCompat"));
    }

    static (string Exe, string Args) Shell(string script) => OperatingSystem.IsWindows()
        ? ("cmd.exe", "/c \"" + script + "\"")
        : ("/bin/sh", "-c \"" + script + "\"");

    static InlineRunner RunToEnd(string script)
    {
        var r = new InlineRunner();
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        var (exe, args) = Shell(script);
        r.Start(exe, args, Path.GetTempPath());
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "the run did not finish");
        return r;
    }

    [Fact]
    public void Runner_keeps_error_stream_lines_and_the_exit_code()
    {
        using var r = OperatingSystem.IsWindows() ? RunToEnd("echo fine & echo oops 1>&2 & exit 3") : RunToEnd("echo fine; echo oops 1>&2; exit 3");
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("oops", r.ErrorText().Trim());
        Assert.Contains("fine", r.Log.GetText(null));
        Assert.Contains("! oops", r.Log.GetText(null));
    }

    [Fact]
    public void Runner_has_no_error_text_when_nothing_went_to_the_error_stream()
    {
        using var r = RunToEnd("echo fine");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.ErrorText());
    }
}
