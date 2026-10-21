using Godot;

/// Collects the errors Godot reports while a test runs (an exception in a script, a missing node), so the test fails
/// on them even when its own checks pass. Warnings don't count.
public partial class ScriptErrors : Logger
{
    readonly List<string> messages = [];
    readonly object guard = new();

    /// Starts collecting; the test adds Messages to its failures before it reports.
    public static ScriptErrors Watch()
    {
        ScriptErrors errors = new();
        OS.AddLogger(errors);
        return errors;
    }

    public List<string> Messages
    {
        get
        {
            lock (guard)
            {
                return [.. messages];
            }
        }
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == (int)ErrorType.Warning)
        {
            return;
        }
        lock (guard)
        {
            messages.Add($"Godot reported an error: {(rationale.Length > 0 ? rationale : code)} ({file.GetFile()}:{line})");
        }
    }
}
