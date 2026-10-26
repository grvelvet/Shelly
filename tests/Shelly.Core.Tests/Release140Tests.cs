using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.4.0: External editor detection/selection, Window run transcript capture, ScriptLinter (PowerShell, Python,
/// Batch, custom), Trusted script badges, Settings &amp; Easter egg updates.</summary>
public class Release140Tests
{
    static string RepoFile(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(new[] { d.FullName }.Concat(parts).ToArray());
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    // ---------- 1. External Editor & Editor Choice ----------

    [Fact]
    public void Editor_settings_round_trip_and_validate()
    {
        var s = new Settings();
        Assert.Equal("builtin", s.DefaultEditor);
        Assert.Equal("auto", s.ExternalEditor);
        Assert.Equal("", s.ExternalEditorPath);
        Assert.Equal("", s.ExternalEditorArgs);

        s.LoadJson("{\"defaultEditor\":\"external\",\"externalEditor\":\"custom\",\"externalEditorPath\":\"C:\\\\Tools\\\\vim.exe\",\"externalEditorArgs\":\"+{line} {file}\"}");
        Assert.Equal("external", s.DefaultEditor);
        Assert.Equal("custom", s.ExternalEditor);
        Assert.Equal(@"C:\Tools\vim.exe", s.ExternalEditorPath);
        Assert.Equal("+{line} {file}", s.ExternalEditorArgs);

        var s2 = new Settings();
        s2.LoadJson(s.ToJson());
        Assert.Equal("external", s2.DefaultEditor);
        Assert.Equal("custom", s2.ExternalEditor);
        Assert.Equal(@"C:\Tools\vim.exe", s2.ExternalEditorPath);
        Assert.Equal("+{line} {file}", s2.ExternalEditorArgs);

        // Invalid values fall back to safe defaults
        var bad = new Settings();
        bad.LoadJson("{\"defaultEditor\":\"bogus\",\"externalEditor\":\"emacs\"}");
        Assert.Equal("builtin", bad.DefaultEditor);
        Assert.Equal("auto", bad.ExternalEditor);
    }

    [Fact]
    public void Auto_external_editor_prefers_VSCode_then_NotepadPP_and_never_opens_non_ps1_in_ISE()
    {
        var localApp = @"C:\Users\me\AppData\Local";
        var progFiles = @"C:\Program Files";
        var sysRoot = @"C:\Windows";
        var vsCode = Path.Combine(localApp, "Programs", "Microsoft VS Code", "Code.exe");
        var npp = Path.Combine(progFiles, "Notepad++", "notepad++.exe");
        var ise = Path.Combine(sysRoot, "System32", "WindowsPowerShell", "v1.0", "powershell_ise.exe");
        var notepad = Path.Combine(sysRoot, "System32", "notepad.exe");
        Func<string, string?> fakeEnv = k => k switch
        {
            "LOCALAPPDATA" => localApp,
            "ProgramFiles" => progFiles,
            "SystemRoot" => sysRoot,
            "PATH" => "",
            _ => null
        };
        var s = new Settings { ExternalEditor = "auto" };

        // 1. When VS Code is installed, it is used for all script types with -g "file:line" when line > 0
        Func<string, bool> allInstalled = p => p == vsCode || p == npp || p == ise || p == notepad;
        var rPs = ExternalEditors.Resolve(s, @"C:\Scripts\a.ps1", 15, allInstalled, fakeEnv);
        Assert.Equal(vsCode, rPs.ExePath);
        Assert.Equal("-g \"C:\\Scripts\\a.ps1:15\"", rPs.Arguments);

        var rPy = ExternalEditors.Resolve(s, @"C:\Scripts\b.py", 0, allInstalled, fakeEnv);
        Assert.Equal(vsCode, rPy.ExePath);
        Assert.Equal("\"C:\\Scripts\\b.py\"", rPy.Arguments);

        // 2. When VS Code is absent and Notepad++ is installed, Notepad++ is used with -n<line>
        Func<string, bool> nppInstalled = p => p == npp || p == ise || p == notepad;
        var rBat = ExternalEditors.Resolve(s, @"C:\Scripts\c.bat", 42, nppInstalled, fakeEnv);
        Assert.Equal(npp, rBat.ExePath);
        Assert.Equal("-n42 \"C:\\Scripts\\c.bat\"", rBat.Arguments);

        // 3. When neither VS Code nor Notepad++ is installed:
        //    - .ps1 -> powershell_ise.exe
        //    - .py, .bat, .cmd, .js -> notepad.exe (NEVER powershell_ise.exe!)
        Func<string, bool> stockWindows = p => p == ise || p == notepad;
        var stockPs = ExternalEditors.Resolve(s, @"C:\Scripts\a.ps1", 0, stockWindows, fakeEnv);
        Assert.Equal(ise, stockPs.ExePath);

        foreach (var nonPs in new[] { @"C:\Scripts\b.py", @"C:\Scripts\c.bat", @"C:\Scripts\d.cmd", @"C:\Scripts\e.js" })
        {
            var resolved = ExternalEditors.Resolve(s, nonPs, 0, stockWindows, fakeEnv);
            Assert.NotEqual(ise, resolved.ExePath);
            Assert.Equal(notepad, resolved.ExePath);
        }

        // 4. Even when the user explicitly chose "ise", non-PowerShell scripts never open in ISE
        var sIse = new Settings { ExternalEditor = "ise" };
        Assert.Equal(ise, ExternalEditors.Resolve(sIse, @"C:\Scripts\a.ps1", 0, stockWindows, fakeEnv).ExePath);
        var pyFallback = ExternalEditors.Resolve(sIse, @"C:\Scripts\b.py", 0, stockWindows, fakeEnv);
        Assert.Equal(notepad, pyFallback.ExePath);
        Assert.True(pyFallback.UsedIseFallback);
        Assert.Equal(notepad, ExternalEditors.Resolve(sIse, @"C:\Scripts\c.bat", 0, stockWindows, fakeEnv).ExePath);
    }

    [Fact]
    public void Custom_external_editor_supports_file_and_line_placeholders()
    {
        var customExe = @"C:\Tools\Sublime\subl.exe";
        var s = new Settings
        {
            ExternalEditor = "custom",
            ExternalEditorPath = customExe,
            ExternalEditorArgs = "{file}:{line}"
        };
        var r = ExternalEditors.Resolve(s, @"C:\My Scripts\test.py", 27, p => p == customExe, _ => null);
        Assert.Equal(customExe, r.ExePath);
        Assert.Equal("\"C:\\My Scripts\\test.py\":27", r.Arguments);

        // Without {file} in template, quoted file path is appended automatically
        s.ExternalEditorArgs = "--wait";
        var r2 = ExternalEditors.Resolve(s, @"C:\My Scripts\test.py", 1, p => p == customExe, _ => null);
        Assert.Equal("--wait \"C:\\My Scripts\\test.py\"", r2.Arguments);
    }

    // ---------- 2. Window Run Transcript / Output Capture ----------

    [Fact]
    public void TranscriptCleaner_strips_headers_and_footers_for_PS51_PS7_and_native_output()
    {
        string rawPs = """
**********************
Windows PowerShell transcript start
Start time: 20261024143000
Username: DESKTOP\User
RunAs User: DESKTOP\User
Configuration Name: 
Machine: DESKTOP (Microsoft Windows NT 10.0.22631.0)
Host Application: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoLogo -NoProfile -EncodedCommand ...
Process ID: 12345
PSVersion: 5.1.22621.2506
PSEdition: Desktop
PSCompatibleVersions: 1.0, 2.0, 3.0, 4.0, 5.0, 5.1.22621.2506
BuildVersion: 10.0.22621.2506
CLRVersion: 4.0.30319.42000
WSManStackVersion: 3.0
PSRemotingProtocolVersion: 2.3
SerializationVersion: 1.1.0.1
**********************
Hello from script!
Line 2 of output
**********************
Windows PowerShell transcript end
End time: 20261024143002
**********************
""";
        string cleaned = TranscriptCleaner.Clean(rawPs);
        Assert.Equal("Hello from script!" + Environment.NewLine + "Line 2 of output", cleaned);

        // Russian localized transcript header/footer
        string rawRu = """
**********************
Начало записи сценария Windows PowerShell
Время начала: 20261024143000
Имя пользователя: PC\User
**********************
Результат работы
Ошибка: файл не найден
**********************
Конец записи сценария Windows PowerShell
Время окончания: 20261024143005
**********************
""";
        Assert.Equal("Результат работы" + Environment.NewLine + "Ошибка: файл не найден", TranscriptCleaner.Clean(rawRu));
    }

    [Fact]
    public void WindowArgs_and_WindowHost_include_StartTranscript_when_logFile_is_given()
    {
        string psArgs = RunArgs.WindowArgs(@"C:\Scripts\test.ps1", "", null, @"C:\Cache\run-1.exit", @"C:\Cache\run-1.log");
        Assert.Contains("Start-Transcript -LiteralPath 'C:\\Cache\\run-1.log' -Force", psArgs);
        Assert.Contains("Stop-Transcript", psArgs);
        Assert.Contains("WriteAllText('C:\\Cache\\run-1.exit'", psArgs);

        var pyInfo = new PythonInfo { Path = @"C:\Python\python.exe", Prefix = Array.Empty<string>(), Version = "3.12.1", Source = "auto" };
        string pyArgs = PythonRun.WindowArgs(pyInfo, @"C:\Scripts\app.py", "", null, @"C:\Cache\run-2.exit", @"C:\Cache\run-2.log");
        Assert.Contains("Start-Transcript -LiteralPath 'C:\\Cache\\run-2.log' -Force", pyArgs);
        Assert.Contains("Stop-Transcript", pyArgs);
        Assert.Contains("& 'C:\\Python\\python.exe' '-u' 'C:\\Scripts\\app.py'", pyArgs);
    }

    // ---------- 3. Built-in ScriptLinter ----------

    [Fact]
    public void PowerShell_linter_detects_empty_catch_secrets_aliases_and_bad_error_preference()
    {
        string code = """
$ErrorActionPreference = "SilentlyContinue"
$apiPassword = "MySuperSecretPassword123!"
ls C:\Temp | % { $_ }
try {
    Invoke-Something
} catch {
}
""";
        var issues = ScriptLinter.LintPowerShell(code);
        Assert.Contains(issues, i => i.Code == "PS-ERRPREF" && i.Line == 1 && i.Severity == "warning");
        Assert.Contains(issues, i => i.Code == "PS-SECRET" && i.Line == 2 && i.Severity == "warning");
        Assert.Contains(issues, i => i.Code == "PS-ALIAS" && i.Line == 3 && i.Severity == "info");
        Assert.Contains(issues, i => i.Code == "PS-EMPTY-CATCH" && i.Severity == "warning");
    }

    [Fact]
    public void Python_linter_detects_tabs_bare_except_mutable_default_print2_and_none_eq()
    {
        string code = "def add_item(item, bucket=[]):\n" +
                      "    if item == None:\n" +
                      "\t    print \"bad indent and print2\"\n" +
                      "    try:\n" +
                      "        pass\n" +
                      "    except:\n" +
                      "        pass\n";
        var issues = ScriptLinter.LintPython(code);
        Assert.Contains(issues, i => i.Code == "PY-MUTABLE-DEFAULT" && i.Line == 1);
        Assert.Contains(issues, i => i.Code == "PY-EQ-NONE" && i.Line == 2);
        Assert.Contains(issues, i => i.Code == "PY-TAB-SPACE" && i.Line == 3);
        Assert.Contains(issues, i => i.Code == "PY-PRINT2" && i.Line == 3);
        Assert.Contains(issues, i => i.Code == "PY-BARE-EXCEPT" && i.Line == 6);

        var syn = ScriptLinter.ParsePythonSyntaxError("  File \"<stdin>\", line 12\n    x = \"abc\n        ^\nSyntaxError: unterminated string literal");
        Assert.NotNull(syn);
        Assert.Equal("PY-SYNTAX", syn!.Code);
        Assert.Equal("error", syn.Severity);
        Assert.Equal(12, syn.Line);
        Assert.Equal(9, syn.Column);
    }

    [Fact]
    public void Batch_linter_detects_bom_delayed_expansion_missing_label_for_percent_and_parens()
    {
        string code = """
@echo off
echo !MY_VAR!
for %i in (*.txt) do echo %i
goto :missing_target
if exist C:\Temp (
    echo ok
""";
        var issues = ScriptLinter.LintBatch(code, hasBom: true);
        Assert.Contains(issues, i => i.Code == "BAT-BOM" && i.Severity == "warning");
        Assert.Contains(issues, i => i.Code == "BAT-DELAYED-EXP" && i.Line == 2);
        Assert.Contains(issues, i => i.Code == "BAT-FOR-PERCENT" && i.Line == 3);
        Assert.Contains(issues, i => i.Code == "BAT-MISSING-LABEL" && i.Line == 4);
        Assert.Contains(issues, i => i.Code == "BAT-PARENS");

        // When setlocal EnableDelayedExpansion and the target label exist, no false positives
        string cleanBatch = """
@echo off
setlocal EnableDelayedExpansion
echo !MY_VAR!
for %%i in (*.txt) do echo %%i
goto :done
:done
exit /b 0
""";
        var cleanIssues = ScriptLinter.LintBatch(cleanBatch, hasBom: false);
        Assert.Empty(cleanIssues);
    }

    // ---------- 4. Trusted Scripts Badge & Settings / Easter Egg ----------

    [Fact]
    public void Trusted_script_helpers_match_hash_and_revoke_cleanly()
    {
        var s = new Settings();
        string rel = @"Tools\deploy.ps1";
        string path = @"C:\Scripts\Tools\deploy.ps1";
        string hash = "ABCDEF123456";

        Assert.False(Safety.IsTrusted(s, rel, path, hash));
        Safety.AddTrust(s, rel, Safety.TrustKeyOf(rel, hash));
        Assert.True(Safety.IsTrusted(s, rel, path, hash));
        Assert.False(Safety.IsTrusted(s, rel, path, "DIFFERENT_HASH"));

        Assert.True(Safety.RemoveTrust(s, rel));
        Assert.False(Safety.IsTrusted(s, rel, path, hash));
    }

    [Fact]
    public void Settings_and_MascotEgg_reflect_140_ui_changes()
    {
        string settingsSrc = RepoFile("src", "Shelly", "Views", "SettingsPage.cs");
        Assert.DoesNotContain("BuildCompat()", settingsSrc);
        Assert.DoesNotContain("Loc.T(\"setTrusted\")", settingsSrc);
        Assert.Contains("Loc.T(\"setEditor\")", settingsSrc);

        string eggSrc = RepoFile("src", "Shelly", "Views", "MascotEgg.cs");
        Assert.Contains("d.HideFooter();", eggSrc);
        Assert.DoesNotContain("AddButton(\"ok\"", eggSrc);

        string xamlSrc = RepoFile("src", "Shelly", "Views", "ScriptsPage.xaml");
        Assert.Contains("Binding IsTrusted", xamlSrc);
        Assert.Contains("Binding TrustedLabel", xamlSrc);
    }

    // ---------- 5. Release 1.4.1 UI & Settings refinements ----------

    [Fact]
    public void Release_141_ComboBox_styles_EditorChoiceDialog_and_Folders_section()
    {
        string styles = RepoFile("src", "Shelly", "Styles.xaml");
        Assert.Contains("TargetType=\"ComboBox\"", styles);
        Assert.Contains("TargetType=\"ComboBoxItem\"", styles);
        Assert.Contains("x:Key=\"ComboBoxToggleButton\"", styles);

        string settingsSrc = RepoFile("src", "Shelly", "Views", "SettingsPage.cs");
        Assert.Contains("public const double ComboWidth = 148;", settingsSrc);
        Assert.Contains("bool _extFoldersExpanded = false;", settingsSrc);
        Assert.Contains("Card(Glyphs.Folder, Loc.T(\"setScriptsFolder\"), St.Paths.Scripts, Btn(", settingsSrc);
        Assert.Contains("Card(Glyphs.Folder, Loc.T(\"setExtFolders\"), Loc.T(\"setExtNote\"), Row(Btn(Loc.T(\"setExtAdd\"), AddExternalFolder), toggleBtn), list);", settingsSrc);

        string dialogSrc = RepoFile("src", "Shelly", "Dialogs", "EditorChoiceDialog.cs");
        Assert.Contains("OptionCard(", dialogSrc);
        Assert.Contains("HorizontalAlignment.Right", dialogSrc);
        Assert.DoesNotContain("HorizontalAlignment.Stretch", dialogSrc);

        string oldLang = Loc.Language;
        try
        {
            Loc.SetLanguage("ru");
            Assert.Equal("Папка по умолчанию", Loc.T("setScriptsFolder"));
            Assert.Equal("Дополнительные папки", Loc.T("setExtFolders"));
            Assert.Equal("Подключаемые внешние папки", Loc.T("setExtNote"));

            Loc.SetLanguage("en");
            Assert.Equal("Default folder", Loc.T("setScriptsFolder"));
            Assert.Equal("Additional folders", Loc.T("setExtFolders"));
            Assert.Equal("Connected external folders", Loc.T("setExtNote"));
        }
        finally
        {
            Loc.SetLanguage(oldLang);
        }
    }

    // ---------- 6. Release 1.4.2 fixes & hardening ----------

    [Fact]
    public void Release_142_TranscriptCleaner_handles_large_logs_and_20480KB_without_overflow()
    {
        var s = new Settings { HistoryOutputKB = "20480" };
        Assert.Equal(20480, s.HistoryOutputKBValue);
        Assert.Equal(20480 * 1024, s.HistoryOutputChars);

        // Even if a huge maxKB is passed, Cap never overflows Int32 or throws ArgumentOutOfRangeException
        var (capped, trunc) = TranscriptCleaner.Cap("Hello world", 20_971_520, "[truncated]");
        Assert.Equal("Hello world", capped);
        Assert.False(trunc);

        // ReadRawLogFile reads >64 KB without premature tail truncation
        string dir = TestUtil.TempDir();
        string logPath = Path.Combine(dir, "window-run.log");
        string bigBody = string.Join(Environment.NewLine, Enumerable.Range(1, 5000).Select(i => $"Line {i:D4}: " + new string('x', 20)));
        File.WriteAllText(logPath, $"{TranscriptCleaner.Separator}\r\nPowerShell transcript start\r\nStart time: 20261026\r\n{TranscriptCleaner.Separator}\r\n{bigBody}\r\n{TranscriptCleaner.Separator}\r\nPowerShell transcript end\r\nEnd time: 20261026\r\n{TranscriptCleaner.Separator}\r\n");

        string raw = TranscriptCleaner.ReadRawLogFile(logPath);
        var (fullOut, fullTrunc) = TranscriptCleaner.Cap(raw, 1024, "[skipped]");
        Assert.False(fullTrunc);
        Assert.StartsWith("Line 0001:", fullOut);
        Assert.EndsWith("Line 5000: " + new string('x', 20), fullOut);

        // ExternalEditors.Quote uses CommandLineToArgvW rules (trailing backslash before quote is doubled)
        Assert.Equal("\"C:\\Scripts\\ trailing\\\\\"", ExternalEditors.Quote(@"C:\Scripts\ trailing\"));

        // ExeStub uses FileMode.CreateNew and contains no legacy #if LAUNCHER blocks
        string stub = ExeBuilder.StubTemplate;
        Assert.DoesNotContain("#if LAUNCHER", stub);
        Assert.Contains("FileMode.CreateNew", stub);
    }
}