using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Atria.Script;

namespace Atria.Tests.Script;

public static class TempleDialogueTests
{
    private const string Marker = "TEMPLE-5012-NARRATION-278";
    private const string SourceHash = "2e28879db63e19468eb889840490db8016d991731d8c157dbd0e595b35cf98df";
    private const string OldHex = "533531415fb6f3c8c4323738";
    private const string NewHex = "533531415fbcb3b8ed323738";
    public static void Run(byte[] original, ScriptProgram repaired, IEnumerable<ScriptProgram> corpus, string resourceRoot)
    {
        var raw = ScriptProgram.Parse("5012SHRI", original, false);
        Check(original.Length == 10490 && Hash(original) == SourceHash, "immutable source identity");
        Check(repaired.SourceSha256 == SourceHash && repaired.AppliedRepairs.SequenceEqual(new[] { Marker }), "specific repair provenance");
        Check(raw.Tokens.Count == repaired.Tokens.Count, "token count unchanged");
        int changes = 0;
        for (int i = 0; i < raw.Tokens.Count; i++)
        {
            ScriptToken before = raw.Tokens[i], after = repaired.Tokens[i];
            Check(before.Kind == after.Kind && before.Offset == after.Offset, "all token kinds/offsets unchanged");
            if (before == after)
            {
                continue;
            }

            changes++;
            Check(before.Kind == ScriptTokenKind.String && before.Offset == 8652 && Convert.ToHexString(before.RawBytes).ToLowerInvariant() == OldHex
&& Convert.ToHexString(after.RawBytes).ToLowerInvariant() == NewHex, "only identified literal changes");
        }

        Check(changes == 1, "one literal replacement");
        byte[] changed = (byte[])original.Clone();
        changed[0] = 32;
        byte[] longer = new byte[original.Length + 1];
        original.CopyTo(longer, 0);
        foreach (ScriptProgram rejected in new[]
        {
            raw,
            ScriptProgram.Parse("OTHER", original),
            ScriptProgram.Parse("5012SHRI", changed),
            ScriptProgram.Parse("5012SHRI", longer)
        }
        )
        {
            Check(!rejected.AppliedRepairs.Contains(Marker), "opt-out/name/hash/length gates");
        }

        Check(Hash(original) == SourceHash, "parse never mutates caller buffer");
        byte[] index = File.ReadAllBytes(Path.Combine(resourceRoot, "ROLAND.IDX"));
        byte[] data = File.ReadAllBytes(Path.Combine(resourceRoot, "ROLAND.DAT"));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int count = BinaryPrimitives.ReadInt32LittleEndian(index);
        bool target = false;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = index.AsSpan(4 + i * 56, 56);
            ReadOnlySpan<byte> field = record[..48];
            int end = field.IndexOf((byte)0);
            string hex = Convert.ToHexString(field[..(end < 0 ? 48 : end)]).ToLowerInvariant();
            keys.Add(hex);
            if (hex != NewHex)
            {
                continue;
            }

            int offset = BinaryPrimitives.ReadInt32LittleEndian(record[48..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(record[52..]);
            Check(i == 3491 && offset == 318617 && length == 158, "existing Russian narration record identity");
            Check(data.AsSpan(offset, length).SequenceEqual(data.AsSpan(318459, 158)), "supplied Russian277/278 bytes preserved");
            target = true;
        }

        Check(target && !keys.Contains(OldHex), "target exists while raw typo is absent");
        int references = 0;
        foreach (ScriptProgram program in corpus)
        {
            for (int i = 0; i < program.Tokens.Count; i++)
            {
                ScriptToken token = program.Tokens[i];
                if (token.Kind != ScriptTokenKind.Identifier || token.Text is not ("dialogue_script2" or "question_dialog2"))
                {
                    continue;
                }

                int depth = 0;
                var literals = new List<ScriptToken>();
                for (int j = i + 1; j < program.Tokens.Count; j++)
                {
                    ScriptToken part = program.Tokens[j];
                    if (part.Text == "(" && part.Kind == ScriptTokenKind.Symbol)
                    {
                        depth++;
                    }
                    else if (part.Text == ")" && part.Kind == ScriptTokenKind.Symbol && --depth == 0)
                    {
                        break;
                    }
                    else if (part.Kind == ScriptTokenKind.String)
                    {
                        literals.Add(part);
                    }
                }

                Check(literals.Count == 1 && keys.Contains(Convert.ToHexString(literals[0].RawBytes).ToLowerInvariant()), $"literal catalog reference {program.MemberName}+{token.Offset}");
                references++;
            }
        }

        Check(references == 5105, "complete original literal-reference inventory");
        foreach (int check in new[]
        {
            0,
            1
        }
        )
        {
            foreach (int weapons in new[]
            {
                0,
                1
            }
            )
            {
                foreach (int detox in new[]
                {
                    0,
                    1
                }
                )
                {
                    foreach (int maze in new[]
                    {
                        0,
                        1
                    }
                    )
                    {
                        foreach (int gift in new[]
                        {
                            0,
                            1
                        }
                        )
                        {
                            var host = new RecordedRoute(check, weapons, detox, maze, gift);
                            new ScriptMachine(repaired, host).Invoke("enter");
                            bool route = check == 1 && weapons == 1 && detox == 1 && maze == 1;
                            Check(host.Calls.Count(c => c.Function == "dialogue_script2" && Convert.ToHexString(c.Arguments[2].Bytes.Span).ToLowerInvariant() == NewHex) == (route ? 1 : 0), "route-specific narration");
                            if (!route)
                            {
                                continue;
                            }

                            int narration = host.Calls.FindIndex(c => c.Function == "dialogue_script2" && c.ByteOffset == 8626);
                            Check(narration >= 0 && host.Calls[narration].Arguments[0].Integer == -1 && host.Calls[narration].Arguments[1].Integer == -1, "narration presentation arguments");
                            Check(host.Calls.Skip(narration + 1).Select(c => c.Function).SequenceEqual(new[] { "dialogue_script2", "dialogue_script2", "dialogue_script2", "dialogue_script2", "game_end", "script_end", "script_end" }), "four shadow lines then ending and both source termination calls");
                            Check(host.Calls.Skip(narration + 1).Take(4).Select(c => Convert.ToHexString(c.Arguments[2].Bytes.Span).ToLowerInvariant()).SequenceEqual(new[] { "533530435fbaa7b8a3b3aab5e53233", "533530435fb8f1bcd2b8ae38", "533530435fbaa7b8a3b3aab5e53234", "533530435fb8f1bcd2b8ae39" }), "exact four shadow literals retained");
                            Check(host.Calls.Single(c => c.Function == "game_end").Arguments[0].Integer == 5
&& host.Calls.Where(c => c.Function == "battle_start2").Select(c => c.Arguments[0].Integer).SequenceEqual(new[] { 56, 57, 58 }), "ending5 and controlled battle callback order");
                        }
                    }
                }
            }
        }
    }

    // Explicit route test double: records effects; does not simulate battle outcomes or gameplay.
    private sealed class RecordedRoute(int check, int weapons, int detox, int maze, int gift) : IScriptHost
    {
        public List<ScriptCall> Calls { get; } = [];

        public int Invoke(ScriptCall call)
        {
            Calls.Add(call);
            int Arg(int n) => call.Arguments[n].RequireInteger();
            return call.Function switch
            {
                "check_eventitem" => weapons,
                "get_global" when Arg(0) == 647 => check,
                "get_event" when Arg(0) == 29 && Arg(1) == 1 => detox,
                "get_event" when Arg(0) == 35 && Arg(1) == 4 => maze,
                "get_event" when Arg(0) == 20 && Arg(1) == 6 => gift,
                "game_score_plus" or "set_global" or "dungeon_enter" or "script_end" or "dialogue_script2" or "battle_start2" or "game_end" => 0,
                _ => throw new InvalidOperationException("Unexpected temple route callback: " + call.Function)
            };
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(bool condition, string detail)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Temple dialogue: " + detail);
        }
    }
}
