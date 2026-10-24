using Shelly.Core;

namespace Shelly.Services;

/// <summary>What RunPlanner reads, from the running app (or the headless AppState of a scheduled run).</summary>
public sealed class AppRunEnvironment : IRunEnvironment
{
    readonly AppState st;
    readonly Func<string, bool> inlineRunning;

    /// <param name="inlineRunning">null: nothing runs inline (headless).</param>
    public AppRunEnvironment(AppState st, Func<string, bool>? inlineRunning = null)
    {
        this.st = st;
        this.inlineRunning = inlineRunning ?? (_ => false);
    }

    public Settings Settings => st.Settings;
    public ScriptFile? Find(string rel) => st.Find(rel);
    public ScriptMeta Meta(ScriptFile f) => st.Meta(f);
    public bool PwshOk => st.PwshOk;
    public bool Ps51Ok => st.Ps51Ok;
    public string? PwshPath => st.Pwsh?.Path;
    public string? Ps51Path => st.Ps51;
    public AnalysisService.Engines Engines => st.Analysis.Eng;
    public Task WaitForAnalysisAsync(ScriptFile f, TimeSpan timeout) => st.Analysis.WaitAsync(f, timeout);
    public PythonVenv? VenvFor(ScriptFile f) => st.VenvFor(f, fresh: true);
    public bool PythonLookupPending => st.PythonSearching || st.Python == null;
    public string PythonError => st.PythonError;
    public Task<PythonInfo?> EnsurePythonAsync() => st.EnsurePythonAsync();
    public bool InlineRunning(string rel) => inlineRunning(rel);
    public void Log(string text) => AppLog.Write(text);
}
