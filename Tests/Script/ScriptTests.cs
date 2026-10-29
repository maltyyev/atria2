using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Atria.Script;

namespace Atria.Tests.Script;

public static class ScriptTests
{
    public static IReadOnlyList<string> Run(string resourceRoot, string? corpusReportPath = null)
    {
        var results = new List<string>();
        byte[] archive = File.ReadAllBytes(Path.Combine(resourceRoot, "SCRIPT.000"));
        int count = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(28));
        Check(count == 216, "Expected original 216-member script corpus.");
        var programs = new Dictionary<string, ScriptProgram>();
        var failures = new List<string>();
        byte[]? library = null;
        byte[]? originalSubevent = null;
        byte[]? originalVidon = null;
        byte[]? originalTemple = null;
        int functions = 0;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = archive.AsSpan(32 + i * 16, 16);
            string name = Encoding.ASCII.GetString(entry[..8]).TrimEnd('\0');
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            if (name == "32_LIBRA")
            {
                library = archive.AsSpan(offset, size).ToArray();
            }

            if (name == "SUBEVENT")
            {
                originalSubevent = archive.AsSpan(offset, size).ToArray();
            }

            if (name == "35_BAR")
            {
                originalVidon = archive.AsSpan(offset, size).ToArray();
            }

            if (name == "5012SHRI")
            {
                originalTemple = archive.AsSpan(offset, size).ToArray();
            }

            try
            {
                var program = ScriptProgram.Parse(name, archive.AsSpan(offset, size));
                string filename = name.ToLowerInvariant() + ".ScR";
                var fromFilename = ScriptProgram.Parse(filename, archive.AsSpan(offset, size));
                Check(fromFilename.AppliedRepairs.SequenceEqual(program.AppliedRepairs)
                    && fromFilename.Tokens.SequenceEqual(program.Tokens)
                    && fromFilename.SourceSha256 == program.SourceSha256
                    && fromFilename.MemberName == filename,
                    "Filename and archive identity must apply identical repairs without rewriting caller identity: " + name);
                programs.Add(name, program);
                functions += program.Functions.Count;
            }
            catch (ScriptException error)
            {
                failures.Add(error.Message);
            }
        }

        results.Add($"Corpus indexed: {programs.Count}/{count} members, {functions} distinct member functions.");
        int syntaxErrors = 0, callCount = 0, unresolvedCalls = 0, ignoredCount = 0;
        foreach (ScriptProgram program in programs.Values)
        {
            ScriptSyntaxReport report = ScriptSyntax.Analyze(program);
            syntaxErrors += report.Errors.Count;
            callCount += report.Calls.Count;
            unresolvedCalls += report.Calls.Count(c => !c.IsBuiltin && !c.HasDefinition);
            ignoredCount += report.IgnoredStatementTokens.Count;
            foreach (ScriptException error in report.Errors)
            {
                results.Add("BODY_ERROR " + error.Message);
            }
        }

        results.Add($"Corpus body analysis: {syntaxErrors} errors, {callCount} calls, {unresolvedCalls} statically unresolved calls, {ignoredCount} native-ignored statement tokens.");
        if (corpusReportPath is not null)
        {
            WriteCorpusReport(programs.Values, corpusReportPath);
        }

        Check(syntaxErrors == 0, "Normalized corpus body-defect inventory changed: " + string.Join(" | ", results.Where(s => s.StartsWith("BODY_ERROR", StringComparison.Ordinal))));
        Check(originalTemple is not null, "5012SHRI fixture missing.");
        TempleDialogueTests.Run(originalTemple!, programs["5012SHRI"], programs.Values, resourceRoot);
        results.Add("Temple: inferred narration278 repair gates, immutable tokens, complete literal catalog coverage and controlled ScriptMachine route checks passed.");
        Check(originalVidon is not null, "35_BAR fixture missing.");
        VidonConditionTests.Run(originalVidon!, programs["35_BAR"]);
        results.Add("Vidon: exact source identity, raw string/offset preservation, instruction-derived branch partition and reward-once behavior passed.");
        Check(originalSubevent is not null, "SUBEVENT fixture missing.");
        var rawSubevent = ScriptProgram.Parse("SUBEVENT", originalSubevent!, false);
        Check(ScriptSyntax.Analyze(rawSubevent).Errors.Count == 2 && programs["SUBEVENT"].AppliedRepairs.Count == 3, "Two exact terminators and authenticated quest-handler append must retain syntax inventory.");
        byte[] alteredSubevent = (byte[])originalSubevent!.Clone();
        alteredSubevent[Array.IndexOf(alteredSubevent, (byte)'\r')] = (byte)'\n';
        var unrecognizedSubevent = ScriptProgram.Parse("SUBEVENT", alteredSubevent);
        Check(unrecognizedSubevent.AppliedRepairs.Count == 0 && ScriptSyntax.Analyze(unrecognizedSubevent).Errors.Count == 2, "SUBEVENT repair must reject a changed member hash.");
        foreach (string failure in failures)
        {
            results.Add("CORPUS_ERROR " + failure);
        }

        Check(library is not null, "Library member fixture missing.");
        ExpectError(() => ScriptProgram.Parse("32_LIBRA", library!, false), "UNCLOSED_BLOCK");
        ExpectError(() => ScriptProgram.Parse("32_libra.scr", library!, false), "UNCLOSED_BLOCK");
        ExpectError(() => ScriptProgram.Parse("OTHER.scr", library!), "UNCLOSED_BLOCK");
        byte[] altered = (byte[])library!.Clone();
        altered[0] ^= 1;
        ExpectError(() => ScriptProgram.Parse("32_LIBRA", altered), "UNCLOSED_BLOCK");
        ExpectError(() => ScriptProgram.Parse("32_libra.scr", altered), "UNCLOSED_BLOCK");
        var lib = programs["32_LIBRA"];
        Check(lib.LibraryRepairApplied && lib.Functions.Count == 8, "Library repair/function index mismatch.");
        Check(programs["32_SHOP"].Functions.ContainsKey("enter"), "Overlapping shop must remain loadable.");
        results.Add("Library: malformed EOF rejected, exact hash gate enforced, repaired eight functions indexed, shop retained.");
        var host = new LibraryHost();
        var machine = new ScriptMachine(lib, host);
        machine.Invoke("enter");
        Check(host.Dialogues == 4 && host.Globals.GetValueOrDefault(821) == 1, "First entry state/dialogue mismatch.");
        machine.Invoke("enter");
        Check(host.Dialogues == 4, "Repeat entry repeated initial dialogues.");
        machine.Invoke("black_talk");
        Check(host.Score == 5 && host.Events.GetValueOrDefault((32, 2)) == 1, "Black-first reward mismatch.");
        int firstReadDialogues = host.Dialogues;
        machine.Invoke("black_talk");
        Check(host.Score == 5 && host.Dialogues > firstReadDialogues, "Repeat black read should retain reward and dialogue branch.");
        var otherHost = new LibraryHost();
        var otherMachine = new ScriptMachine(lib, otherHost);
        otherMachine.Invoke("red_talk");
        otherMachine.Invoke("black_talk");
        Check(otherHost.Score == 0, "Reading red before black must suppress first-read score.");
        results.Add("Library interpreter: first/repeat entry, black-first reward, repeat read and red-first branch passed with recorded callback test host.");
        var scalar = ScriptProgram.Parse("TEST", Encoding.ASCII.GetBytes("int g; helper(int x) { return x*2; } run() { int x; x=3+4*5; g=helper(x); return g/2; }"));
        var scalarMachine = new ScriptMachine(scalar, new UnresolvedScriptHost());
        Check(scalarMachine.Invoke("run") == 23 && scalarMachine.Globals["g"] == 46, "Arithmetic/function/global behavior.");
        var returns = ScriptProgram.Parse("TEST", "run(){ if(1){ return 7; } return 9; }"u8);
        Check(new ScriptMachine(returns, new UnresolvedScriptHost()).Invoke("run") == 7, "Nested-return cursor behavior differs from native dispatch.");
        var emptyReturn = ScriptProgram.Parse("TEST", "run(){return();}"u8);
        Check(new ScriptMachine(emptyReturn, new UnresolvedScriptHost()).Invoke("run") == 0, "Empty parenthesized return must use initialized-zero result.");
        var logicHost = new LibraryHost();
        var eager = ScriptProgram.Parse("TEST", "run(){return(0 && get_global(1));}"u8);
        Check(new ScriptMachine(eager, logicHost).Invoke("run") == 0 && logicHost.GlobalReads == 1, "Logical evaluation must be eager.");
        var mixed = ScriptProgram.Parse("TEST", "run(){return(0 && 1 || 1);}"u8);
        Check(new ScriptMachine(mixed, new UnresolvedScriptHost()).Invoke("run") == 0, "Native logical chain retains its initial operator.");
        var loop = ScriptProgram.Parse("TEST", "run(){ while(1){} }"u8);
        ExpectError(() => new ScriptMachine(loop, new UnresolvedScriptHost()) { StepLimit = 100 }.Invoke("run"), "STEP_LIMIT");
        ExpectError(() => machine.Invoke("suba_161"), "MISSING_FUNCTION");
        var subevent = programs["SUBEVENT"];
        Check(subevent.Functions.ContainsKey("suba_161") && subevent.Functions.ContainsKey("suba_162"), "Authenticated patch restores both quest handlers.");
        ExpectError(() => new ScriptMachine(rawSubevent, new UnresolvedScriptHost()).Invoke("suba_161"), "MISSING_FUNCTION");
        ExpectError(() => new ScriptMachine(lib, new UnresolvedScriptHost()).Invoke("enter"), "UNIMPLEMENTED_BUILTIN");
        Quest63RecoveryTests.Run(resourceRoot, subevent);
        results.Add("Scalar contracts: precedence, calls, globals, nested-return cursor, step limit, missing quest handler and unresolved builtin diagnostics passed.");
        Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
        results.Add(ScriptArgumentAuditTests.Run(resourceRoot));
        return results;
    }

    private static void WriteCorpusReport(IEnumerable<ScriptProgram> programs, string path)
    {
        using var output = File.Create(path);
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("evidence", "C# byte tokenizer/function index/body syntax analysis; not original runtime or full game execution");
        json.WriteStartArray("members");
        foreach (var program in programs)
        {
            var report = ScriptSyntax.Analyze(program);
            json.WriteStartObject();
            json.WriteString("member", program.MemberName);
            json.WriteString("original_sha256", program.SourceSha256);
            json.WriteBoolean("library_repair", program.LibraryRepairApplied);
            json.WriteStartArray("applied_repairs");
            foreach (string repair in program.AppliedRepairs)
            {
                json.WriteStringValue(repair);
            }

            json.WriteEndArray();
            json.WriteNumber("functions", program.Functions.Count);
            json.WriteNumber("calls", report.Calls.Count);
            json.WriteStartArray("errors");
            foreach (var error in report.Errors)
            {
                json.WriteStartObject();
                json.WriteString("code", error.Code);
                json.WriteNumber("member_offset", error.ByteOffset);
                json.WriteString("message", error.Message);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("ignored_statement_tokens");
            foreach (var token in report.IgnoredStatementTokens)
            {
                json.WriteStartObject();
                json.WriteString("function", token.Function);
                json.WriteNumber("member_offset", token.ByteOffset);
                json.WriteString("kind", token.Kind.ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("unresolved_calls");
            foreach (var call in report.Calls.Where(c => !c.IsBuiltin && !c.HasDefinition))
            {
                json.WriteStartObject();
                json.WriteString("caller", call.Caller);
                json.WriteString("callee", call.Callee);
                json.WriteNumber("member_offset", call.ByteOffset);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Check(bool condition, string detail)
    {
        if (!condition)
        {
            throw new InvalidOperationException(detail);
        }
    }

    private static void ExpectError(Action action, string code)
    {
        try
        {
            action();
        }
        catch (ScriptException error) when (error.Code == code)
        {
            return;
        }

        throw new InvalidOperationException($"Expected script diagnostic {code}.");
    }

    // A deliberately limited test double. This is not the game builtin implementation.
    private sealed class LibraryHost : IScriptHost
    {
        public Dictionary<int, int> Globals { get; } = [];
        public Dictionary<(int, int), int> Events { get; } = [];
        public int Dialogues
        {
            get; private set;
        }
        public int Score
        {
            get; private set;
        }
        public int GlobalReads
        {
            get; private set;
        }

        public int Invoke(ScriptCall call)
        {
            int Arg(int n) => call.Arguments[n].RequireInteger();
            switch (call.Function)
            {
                case "get_global":
                    GlobalReads++;
                    return Globals.GetValueOrDefault(Arg(0));
                case "set_global":
                    Globals[Arg(0)] = Arg(1);
                    return 0;
                case "get_event":
                    return Events.GetValueOrDefault((Arg(0), Arg(1)));
                case "set_event":
                    Events[(Arg(0), Arg(1))] = Arg(2);
                    return 0;
                case "game_score_plus":
                    Score += Arg(0);
                    return 0;
                case "dialogue_script2":
                    Check(call.Arguments.Count == 3 && call.Arguments[2].IsString, "Dialogue key must preserve byte string type.");
                    Dialogues++;
                    return 0;
                case "script_end":
                    return 0;
                case "people_name":
                    Check(call.Arguments.Count == 12 && call.Arguments.All(a => a.IsString), "Library people-name triples.");
                    return 0;
                default:
                    throw new ScriptException("UNIMPLEMENTED_BUILTIN", call.Member, call.ByteOffset, call.Function);
            }
        }
    }
}
