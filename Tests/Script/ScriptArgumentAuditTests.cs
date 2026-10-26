using System.Text;
using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Script;

/// <summary>Checks every empty call without executing story branches or touching player state.</summary>
public static class ScriptArgumentAuditTests
{
    public static string Run(string root)
    {
        var fixture = ScriptProgram.Parse("TEST", "run(){/*lost_money()*/dialogue_script(0,1,\"get_item()\");get_global();helper();}helper(int x){return(x);}"u8);
        var fixtureCalls = EmptyCalls(fixture);
        Check(fixtureCalls.Count == 2 && fixtureCalls.All(c => Problem(fixture, c) is not null),
            "Scanner ignores comments/string contents and detects missing builtin/user-function arguments");
        var valid = ScriptProgram.Parse("TEST", "run(){script_end();get_daycount();set_daycount();event_count_plus();people_name();helper();}helper(){return();}"u8);
        Check(EmptyCalls(valid).All(c => Problem(valid, c) is null), "Valid zero/optional-argument calls and empty returns remain allowed");

        using var archive = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
        var rows = new List<string> { "member\tcaller\tcallee\tbyte_offset\tresult" };
        var problems = new List<string>();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int functions = 0, calls = 0, empty = 0;
        foreach (var entry in archive.Entries)
        {
            string name = Encoding.ASCII.GetString(entry.RawName.Span).TrimEnd('\0');
            var program = ScriptProgram.Parse(name, archive.ReadMember(entry.Index));
            var syntax = ScriptSyntax.Analyze(program);
            Check(syntax.Errors.Count == 0, name + ": " + string.Join("; ", syntax.Errors.Select(e => e.Message)));
            functions += program.Functions.Count;
            calls += syntax.Calls.Count;
            foreach (var call in EmptyCalls(program, syntax))
            {
                empty++;
                counts[call.Callee] = counts.GetValueOrDefault(call.Callee) + 1;
                string? problem = Problem(program, call);
                rows.Add($"{name}\t{call.Caller}\t{call.Callee}\t{call.ByteOffset}\t{problem ?? "allowed"}");
                if (problem is not null)
                {
                    problems.Add($"{name}/{call.Caller}+{call.ByteOffset}: {call.Callee}(): {problem}");
                }
            }
        }
        string output = Path.Combine(root, "..", "audit", "script-empty-arguments");
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "calls.tsv"), rows, Encoding.UTF8);
        string summary = $"Empty-argument audit: {archive.Entries.Count} members, {functions} functions, {calls} total calls, {empty} empty calls, {problems.Count} invalid calls.";
        File.WriteAllLines(Path.Combine(output, "summary.txt"), new[] { summary }
            .Concat(counts.Select(pair => $"{pair.Key}: {pair.Value}"))
            .Concat(problems), Encoding.UTF8);
        Check(problems.Count == 0, string.Join(Environment.NewLine, problems));
        return summary + " Report: " + output;
    }

    private static List<ScriptCallSite> EmptyCalls(ScriptProgram program, ScriptSyntaxReport? report = null)
    {
        var tokens = program.Tokens.Select((token, index) => (token.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
        return (report ?? ScriptSyntax.Analyze(program)).Calls.Where(call =>
        {
            int index = tokens[call.ByteOffset];
            return program.Tokens[index + 2] is { Kind: ScriptTokenKind.Symbol, Text: ")" };
        }).ToList();
    }

    private static readonly Dictionary<string, string?> builtinProblems = new(StringComparer.Ordinal);

    private static string? Problem(ScriptProgram program, ScriptCallSite call)
    {
        if (!call.IsBuiltin)
        {
            return !program.Functions.TryGetValue(call.Callee, out var definition) ? "missing function"
                : definition.Parameters.Length != 0 ? $"requires {definition.Parameters.Length} arguments" : null;
        }
        if (!builtinProblems.TryGetValue(call.Callee, out var problem))
        {
            try
            {
                // Fresh disposable state per builtin; no effects host, files or game session.
                new ScriptStateHost(new GameState { Mode = 410 }).Invoke(new("AUDIT", call.Callee, 0, []));
                problem = null;
            }
            catch (ScriptException error)
            {
                problem = error.Code + ": " + error.Message;
            }
            builtinProblems.Add(call.Callee, problem);
        }
        return problem;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Empty-argument audit: " + message);
        }
    }
}
