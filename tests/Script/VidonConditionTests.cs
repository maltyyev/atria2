using System.Text;
using Atria.Game.State;
using Atria.Script;

namespace Atria.Tests;

public static class VidonConditionTests
{
    public static void Run(byte[] original, ScriptProgram normalized)
    {
        const string repair = "VIDON-035-NATIVE-CONDITION-NORMALIZATION";
        var raw = ScriptProgram.Parse("35_BAR", original, false);
        Check(ScriptSyntax.Analyze(raw).Errors.Count == 1, "raw malformed condition remains diagnosed");
        Check(normalized.AppliedRepairs.SequenceEqual(new[] { repair }) && !normalized.LibraryRepairApplied,
            "specific normalization must retain its own provenance");
        Check(normalized.SourceSha256 == raw.SourceSha256 && ScriptSyntax.Analyze(normalized).Errors.Count == 0,
            "original identity retained while normalized body parses");
        Check(raw.Tokens.Where(t => t.Kind == ScriptTokenKind.String).SequenceEqual(
            normalized.Tokens.Where(t => t.Kind == ScriptTokenKind.String)), "all string bytes and offsets preserved");
        Check(raw.Functions.Values.Select(f => (f.Name, f.ByteOffset)).SequenceEqual(
            normalized.Functions.Values.Select(f => (f.Name, f.ByteOffset))), "all function source offsets preserved");
        byte[] changed = (byte[])original.Clone(); changed[0] = changed[0] == 32 ? (byte)9 : (byte)32;
        foreach (var rejected in new[] { ScriptProgram.Parse("35_BAR", changed), ScriptProgram.Parse("OTHER", original), raw })
            Check(rejected.AppliedRepairs.Count == 0 && ScriptSyntax.Analyze(rejected).Errors.Count == 1,
                "hash, name and opt-out gates must reject normalization");

        // Decision partition is recovered from native instructions, including the
        // malformed false path which skips the outer return. See BUG_35_BAR_EVALUATOR.
        foreach (int b in new[] { -1, 0, 1, 2 })
        foreach (int item in new[] { -1, 0, 1 })
        foreach (int d in new[] { -1, 0, 1, 2 })
        foreach (int e in new[] { -1, 0, 1, 2 })
        foreach (int f in new[] { -1, 0, 1, 2 })
        foreach (int g in new[] { -1, 0, 1, 2 })
        foreach (int rewarded in new[] { 0, 1 })
        {
            var state = State(b, item, d, e, f, g, rewarded);
            var effects = new Dialogues();
            new ScriptMachine(normalized, new ScriptStateHost(state, effects)).Invoke("abdur_talk");
            bool c = item != 0;
            int[] expected;
            int score = 0, expectedE = e, expectedItem = item, expectedReward = rewarded;
            if (b == 0) expected = [11];
            else if (b == 1 && !c && d == 1) { expected = [642, 12, 382, 13, 383, 14]; expectedItem = 1; }
            else if (b == 1 && c && d == 1 && e == 0)
            { expected = [643, 384, 15, 644, 16, 645, 17, 18]; score = 5; expectedE = 1; }
            else if (b == 1 && c && d == 1 && e == 1 && f == 1 && g == 0)
                expected = [19, 385, 20, 646, 21, 386, 22];
            else if (g == 1)
            {
                expected = rewarded == 0 ? [23, 387, 24, 388, 25] : [25];
                if (rewarded == 0) { score = 20; expectedReward = 1; }
            }
            else expected = [220];
            Check(effects.Keys.SequenceEqual(expected), $"dialogue partition b{b}/item{item}/d{d}/e{e}/f{f}/g{g}/reward{rewarded}");
            Check(state.Score == score && state.Events[351] == expectedE && state.Events[354] == expectedReward
                && state.GetQuestItemFlag(39) == expectedItem && state.ScriptActive == 0, "state effects and termination");
            Check(state.Events[353] == b && state.Events[191] == d && state.Events[342] == f && state.Events[371] == g
                && state.Globals[612] == 1 && state.Money == 0 && state.PendingExperience == 0, "unrelated branch inputs and rewards unchanged");
        }

        var repeated = State(1, 1, 1, 1, 0, 1, 0);
        var recorded = new Dialogues();
        var machine = new ScriptMachine(normalized, new ScriptStateHost(repeated, recorded));
        machine.Invoke("abdur_talk");
        recorded.Keys.Clear(); repeated.ScriptActive = 1;
        machine.Invoke("abdur_talk");
        Check(recorded.Keys.SequenceEqual(new[] { 25 }) && repeated.Score == 20 && repeated.ScriptActive == 0,
            "false malformed condition reaches reward, repeat never awards again");

        var first = State(0, 0, 1, 1, 1, 1, 0); first.Globals[612] = 0;
        var initial = new Dialogues();
        new ScriptMachine(normalized, new ScriptStateHost(first, initial)).Invoke("abdur_talk");
        Check(first.Globals[612] == 1 && first.GetQuestItemFlag(39) == 1 && first.GetQuestItemFlag(40) == 1
            && first.Score == 0 && initial.Keys[^1] == 11 && initial.Keys.Contains(218) && initial.Keys.Contains(219),
            "earlier initial quest-item branch still precedes later reward branch");
    }

    private static GameState State(int b, int item, int d, int e, int f, int g, int rewarded)
    {
        var state = new GameState { Mode = 420, ScriptActive = 1 };
        state.Globals[612] = 1; state.Events[353] = b; state.SetQuestItemFlag(39, item);
        state.Events[191] = d; state.Events[351] = e; state.Events[342] = f; state.Events[371] = g;
        state.Events[354] = rewarded;
        return state;
    }

    private sealed class Dialogues : IScriptEffects
    {
        public List<int> Keys { get; } = [];
        public void Execute(ScriptEffect effect, GameState state)
        {
            Check(effect.Kind == ScriptEffectKind.DialogueKey, "only original key dialogues are expected");
            string key = Encoding.Latin1.GetString(effect.Call.Arguments[2].Bytes.Span);
            int start = key.Length;
            while (start > 0 && char.IsAsciiDigit(key[start - 1])) start--;
            Keys.Add(int.Parse(key.AsSpan(start), System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Vidon condition: " + message); }
}
