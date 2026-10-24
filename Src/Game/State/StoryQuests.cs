using System.Text.Json;

namespace Atria.Game.State;

/// <summary>Journal-only observations of scenario state. Never changes scenario flags or rewards.</summary>
public static class StoryQuests
{
    private sealed record Condition(string Bank, int Index, int Minimum, int Maximum)
    {
        public bool Matches(GameState state)
        {
            int value = Bank switch
            {
                "global" => state.Globals[Index],
                "event" => state.Events[Index],
                "item" => state.GetQuestItemFlag(Index),
                _ => throw new InvalidDataException("Unknown story quest condition bank.")
            };
            return value >= Minimum && value <= Maximum;
        }
    }

    private sealed record Rule(QuestMilestones Milestone, Condition[] Conditions, bool RequiresStarted);
    private static readonly Dictionary<int, Rule[]> definitions = Load();
    public static IEnumerable<int> Ids => definitions.Keys;
    public static bool Contains(int id) => definitions.ContainsKey(id);

    public static QuestMilestones Infer(GameState state, int id)
    {
        var progress = QuestMilestones.None;
        if (!definitions.TryGetValue(id, out var rules)) return progress;
        foreach (var rule in rules)
        {
            if (rule.RequiresStarted && !state.Journal.Entries.GetValueOrDefault(id).HasFlag(QuestMilestones.Started)) continue;
            if (rule.Conditions.All(condition => condition.Matches(state))) progress |= QuestMilestones.Started | rule.Milestone;
        }
        return progress;
    }

    public static void Observe(GameState state)
    {
        foreach (int id in Ids)
        {
            var progress = Infer(state, id);
            if (progress != QuestMilestones.None) state.Journal.Record(state, id, progress);
        }
    }

    private static Dictionary<int, Rule[]> Load()
    {
        using var stream = typeof(StoryQuests).Assembly.GetManifestResourceStream("Atria.Content.Atria2.StoryQuests.json")
            ?? throw new InvalidDataException("Missing story quest definitions.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(entry => entry.GetProperty("id").GetInt32(), entry =>
            entry.GetProperty("rules").EnumerateArray().Select(rule => new Rule(
                QuestJournalTriggers.ParseMilestone(rule.GetProperty("milestone").GetString()),
                rule.GetProperty("all").EnumerateArray().Select(condition => new Condition(
                    condition.GetProperty("bank").GetString()!, condition.GetProperty("index").GetInt32(),
                    condition.GetProperty("minimum").GetInt32(), condition.GetProperty("maximum").GetInt32())).ToArray(),
                rule.TryGetProperty("requiresStarted", out var required) && required.GetBoolean())).ToArray());
    }
}
