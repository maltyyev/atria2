namespace Atria.Script;

public sealed record ScriptArgumentSnapshot(bool IsString, int Integer, byte[] Bytes);
public sealed record ScriptCallSnapshot(string Function, int Offset, ScriptArgumentSnapshot[] Arguments, int Result);
public sealed record ScriptCheckpoint(string Member, string Function, string SourceHash, int Continuation, int ContinuationMode, ScriptCallSnapshot[] Calls);
/// <summary>Reconstruct interpreter locals/control stack by replaying recorded host results.
/// Previously executed host calls NEVER run again. The game snapshot already contains their effects.</summary>
public sealed class CheckpointScriptHost : IScriptHost, IScriptArgumentPolicy
{
    private readonly IScriptHost inner;
    private readonly List<ScriptCallSnapshot> calls = [];
    private readonly ScriptCallSnapshot[] replay;
    private int replayCursor;
    public Action<ScriptCall>? BeforeCall
    {
        get; set;
    }
    public Action<ScriptCall>? AfterCall
    {
        get; set;
    }

    public CheckpointScriptHost(IScriptHost inner, ScriptCallSnapshot[]? replay = null)
    {
        this.inner = inner;
        this.replay = replay ?? [];
    }

    public ScriptCallSnapshot[] Capture() => calls.ToArray();
    public bool SkipArguments(string name) => inner is IScriptArgumentPolicy policy && policy.SkipArguments(name);
    public int Invoke(ScriptCall call)
    {
        var args = call.Arguments.Select(a => new ScriptArgumentSnapshot(a.IsString, a.Integer, a.Bytes.ToArray())).ToArray();
        if (replayCursor < replay.Length)
        {
            var old = replay[replayCursor++];
            if (old.Function != call.Function || old.Offset != call.ByteOffset || old.Arguments.Length != args.Length
|| old.Arguments.Where((a, i) => a.IsString != args[i].IsString || a.Integer != args[i].Integer || !a.Bytes.AsSpan().SequenceEqual(args[i].Bytes)).Any())
            {
                throw new InvalidDataException("Saved scenario continuation diverged from its recorded calls.");
            }

            calls.Add(old);
            return old.Result;
        }

        BeforeCall?.Invoke(call);
        int result = inner.Invoke(call);
        calls.Add(new(call.Function, call.ByteOffset, args, result));
        AfterCall?.Invoke(call);
        return result;
    }

    public void VerifyReplayComplete()
    {
        if (replayCursor != replay.Length)
        {
            throw new InvalidDataException("Scenario ended before its saved continuation.");
        }
    }

    public static void Validate(ScriptProgram program, ScriptCheckpoint saved)
    {
        var host = new CheckpointScriptHost(new BoundaryHost(), saved.Calls);
        try
        {
            new ScriptMachine(program, host).Invoke(saved.Function);
        }
        catch (CheckpointBoundaryException)
        {
        }

        host.VerifyReplayComplete();
    }

    private sealed class CheckpointBoundaryException : Exception
    {
    }

    private sealed class BoundaryHost : IScriptHost
    {
        public int Invoke(ScriptCall call) => throw new CheckpointBoundaryException();
    }
}
