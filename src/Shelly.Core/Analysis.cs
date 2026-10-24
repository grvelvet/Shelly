using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelly.Core;

public sealed class ParamInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    /// <summary>text | switch | bool | array</summary>
    public string Kind { get; set; } = "text";
    public bool Mandatory { get; set; }
    public string? Default { get; set; }
    public string DefaultText { get; set; } = "";
    public bool DefaultBool { get; set; }
    public string Help { get; set; } = "";
    public string Set { get; set; } = "";
    public List<string> SetValues { get; set; } = new();

    public string FriendlyType()
    {
        string t = Type.ToLowerInvariant();
        if (Kind == "switch") return Loc.T("ftSwitch");
        if (Kind == "bool") return Loc.T("ftBool");
        if (Kind == "array") return Loc.T("ftArray");
        if (t == "") return Loc.T("ftAny");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(system\.)?string$")) return Loc.T("ftString");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(system\.)?(int|int16|int32|int64|long|byte|double|single|float|decimal|uint16|uint32|uint64)$")) return Loc.T("ftNumber");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(system\.)?datetime$")) return Loc.T("ftDate");
        return Type;
    }
}

public sealed class DangerFinding
{
    public string Rule { get; set; } = "";
    /// <summary>high | medium</summary>
    public string Sev { get; set; } = "medium";
    public int Line { get; set; }
    public string Code { get; set; } = "";
    public string Detail { get; set; } = "";
    [JsonIgnore] public string Text => Loc.F("r" + Rule, Detail);
}

public sealed class ParseError
{
    public int Line { get; set; }
    public string Message { get; set; } = "";
}

public sealed class RequiresInfo
{
    public string Version { get; set; } = "";
    public List<string> Editions { get; set; } = new();
    public bool Elevation { get; set; }
    [JsonIgnore] public Version? ParsedVersion => System.Version.TryParse(Version, out var v) ? v : null;
}

/// <summary>What one engine's parser says about one script (the record of Analyzer.ps1).</summary>
public sealed class AnalysisRecord
{
    [JsonPropertyName("P")] public string Path { get; set; } = "";
    /// <summary>The content hash (SHA-256, hex) the record belongs to; older caches held the file time here.</summary>
    [JsonPropertyName("T")] public string Ticks { get; set; } = "";
    [JsonPropertyName("E")] public string Engine { get; set; } = "";
    public string Desc { get; set; } = "";
    public List<ParseError> Errors { get; set; } = new();
    public List<string> Ps7 { get; set; } = new();
    public List<string> WinPs { get; set; } = new();
    public List<ParamInfo> Params { get; set; } = new();
    public List<DangerFinding> Danger { get; set; } = new();
    public RequiresInfo Req { get; set; } = new();
    public List<string> MissingModules { get; set; } = new();
    /// <summary>The engine could not analyze the file (process failed); not saved in the cache.</summary>
    [JsonIgnore] public bool Failed { get; set; }
    /// <summary>Made by the built-in fallback parser (no PowerShell available or still waiting).</summary>
    [JsonIgnore] public bool Fallback { get; set; }

    public static List<AnalysisRecord> ParseJson(string json)
    {
        json = json.TrimStart('\uFEFF').Trim();
        if (json.Length == 0) return new();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
        opts.Converters.Add(new LooseStringConverter());
        opts.Converters.Add(new LooseBoolConverter());
        if (json.StartsWith("{")) json = "[" + json + "]";   // ConvertTo-Json of a single object in old PowerShell versions
        return JsonSerializer.Deserialize<List<AnalysisRecord>>(json, opts)?.Where(r => r != null && r.Path.Length > 0).ToList() ?? new();
    }

    sealed class LooseStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
        {
            JsonTokenType.String => r.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(r.ValueSpan),
            JsonTokenType.True => "True",
            JsonTokenType.False => "False",
            JsonTokenType.Null => null,
            _ => Skip(ref r)
        };
        static string? Skip(ref Utf8JsonReader r) { r.Skip(); return null; }
        public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
    }

    sealed class LooseBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => r.GetDouble() != 0,
            JsonTokenType.String => string.Equals(r.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        public override void Write(Utf8JsonWriter w, bool v, JsonSerializerOptions o) => w.WriteBooleanValue(v);
    }
}

/// <summary>Runs Analyzer.ps1 in an engine (powershell.exe or pwsh.exe) for a batch of files.
/// The script is started through a short -Command that loads it with [scriptblock]::Create, so a Group Policy
/// execution policy (AllSigned) does not block the analysis.</summary>
public static class AnalyzerRunner
{
    static string? scriptText;
    public static string ScriptText
    {
        get
        {
            if (scriptText != null) return scriptText;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shelly.Analyzer.ps1")!;
            using var rd = new StreamReader(s, Encoding.UTF8);
            return scriptText = rd.ReadToEnd();
        }
    }

    public static async Task<List<AnalysisRecord>> RunAsync(string exe, IReadOnlyList<string> paths, string tempDir, TimeSpan timeout, CancellationToken ct = default)
    {
        Directory.CreateDirectory(tempDir);
        string id = Guid.NewGuid().ToString("N");
        string code = Path.Combine(tempDir, "analyzer-" + id + ".ps1");
        string inF = Path.Combine(tempDir, "in-" + id + ".txt");
        string outF = Path.Combine(tempDir, "out-" + id + ".json");
        try
        {
            await File.WriteAllTextAsync(code, ScriptText, new UTF8Encoding(true), ct);
            await File.WriteAllLinesAsync(inF, paths, new UTF8Encoding(true), ct);
            string cmd = "& ([scriptblock]::Create([IO.File]::ReadAllText(" + Quoting.PsString(code) + ", [Text.Encoding]::UTF8))) -In " +
                         Quoting.PsString(inF) + " -Out " + Quoting.PsString(outF);
            var psi = new ProcessStartInfo(exe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + Quoting.NativeArg(cmd))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = tempDir
            };
            using var p = Process.Start(psi)!;
            _ = p.StandardOutput.ReadToEndAsync(ct);
            _ = p.StandardError.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
            if (!File.Exists(outF)) return new();
            return AnalysisRecord.ParseJson(await File.ReadAllTextAsync(outF, ct));
        }
        finally
        {
            foreach (var f in new[] { code, inF, outF }) { try { File.Delete(f); } catch { } }
        }
    }
}

/// <summary>Analysis results per engine, keyed by path + content hash; kept on disk so the next start is instant.</summary>
public sealed class AnalysisCache
{
    readonly Dictionary<string, AnalysisRecord> map = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();
    public string? FilePath { get; }
    public const int FormatVersion = 2;

    public AnalysisCache(string? filePath = null) { FilePath = filePath; }

    public static string Key(string path, string hash, string engine) => path + "|" + hash + "|" + engine;

    public AnalysisRecord? Get(string path, string hash, string engine)
    {
        lock (gate) return map.TryGetValue(Key(path, hash, engine), out var r) ? r : null;
    }

    public void Put(AnalysisRecord r)
    {
        lock (gate) map[Key(r.Path, r.Ticks, r.Engine)] = r;
    }

    public void Load()
    {
        if (FilePath == null || !File.Exists(FilePath)) return;
        try
        {
            string text = File.ReadAllText(FilePath);
            int nl = text.IndexOf('\n');
            if (nl < 0 || text.Substring(0, nl).Trim() != "v" + FormatVersion + "|" + AnalyzerHash()) return;
            foreach (var r in AnalysisRecord.ParseJson(text.Substring(nl + 1))) Put(r);
        }
        catch { }
    }

    public void Save(Func<string, bool>? keep = null)
    {
        if (FilePath == null) return;
        try
        {
            List<AnalysisRecord> list;
            lock (gate) list = map.Values.Where(r => !r.Failed && !r.Fallback && (keep == null || keep(r.Path))).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "v" + FormatVersion + "|" + AnalyzerHash() + "\n" + JsonSerializer.Serialize(list, JsonOpts.Relaxed), new UTF8Encoding(false));
        }
        catch { }
    }

    // Results of an older analyzer are dropped
    static string AnalyzerHash()
    {
        var h = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(AnalyzerRunner.ScriptText));
        return Convert.ToHexString(h, 0, 8);
    }
}
