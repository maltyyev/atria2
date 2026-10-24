using System.Text.Json;
using Atria.Script;

namespace Atria.Game.State;

public static class QuestJournalTriggers
{
    public sealed record Trigger(string Member, int Offset, string Function, int Quest, QuestMilestones Milestone);
    public static IReadOnlyList<Trigger> All { get; } = Load();
    private static readonly Dictionary<(string Member, int Offset), Trigger> byCall = All.ToDictionary(rule => (rule.Member, rule.Offset));

    public static void Observe(ScriptCall call, GameState state)
    {
        if (byCall.TryGetValue((call.Member.ToUpperInvariant(), call.ByteOffset), out var rule) && call.Function == rule.Function)
        {
            state.Journal.Record(state, rule.Quest, rule.Milestone);
        }
    }

    private static Trigger[] Load()
    {
        using var stream = typeof(QuestJournalTriggers).Assembly.GetManifestResourceStream("Atria.Content.Atria2.QuestJournalTriggers.json")
            ?? throw new InvalidDataException("Missing quest journal trigger definitions.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(entry => new Trigger(
            entry.GetProperty("member").GetString()!, entry.GetProperty("offset").GetInt32(), entry.GetProperty("function").GetString()!,
            entry.GetProperty("quest").GetInt32(), ParseMilestone(entry.GetProperty("milestone").GetString()))).ToArray();
    }

    internal static QuestMilestones ParseMilestone(string? name) => name switch
            {
                "started" => QuestMilestones.Started,
                "objectiveReached" => QuestMilestones.ObjectiveReached,
                "completed" => QuestMilestones.Completed,
                "leadFound" => QuestMilestones.LeadFound,
                "pursuitContinued" => QuestMilestones.PursuitContinued,
                "declined" => QuestMilestones.Declined,
                "stopped" => QuestMilestones.Stopped,
                _ => throw new InvalidDataException("Unknown journal milestone.")
            };
}
