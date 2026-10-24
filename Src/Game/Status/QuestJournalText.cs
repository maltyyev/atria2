using Atria.Game.State;

namespace Atria.Game.Status;

public static class QuestJournalText
{
    public const int InformationEntry = 0;
    public const int OriginalEntryCount = 150;

    public static bool IsVisible(GameState state, int id) => state.Journal.Progress(state, id) != QuestMilestones.None
        && GameText.Get($"quests.{id:D3}.heading.0").Length != 0;

    public static string Description(GameState state, int id)
    {
        string prefix = $"quests.{id:D3}";
        if (id == InformationEntry) return GameText.Get(prefix + ".description");
        var progress = state.Journal.Progress(state, id);
        var paragraphs = new List<string>();
        if (progress.HasFlag(QuestMilestones.Started)) Add(".start.text");
        if (progress.HasFlag(QuestMilestones.LeadFound)) Add(".lead.text");
        if (progress.HasFlag(QuestMilestones.PursuitContinued)) Add(".pursuit.text");
        if (progress.HasFlag(QuestMilestones.ObjectiveReached)) Add(".progress.text");
        if (progress.HasFlag(QuestMilestones.Stopped))
        {
            paragraphs.Add(GameText.Paragraph(prefix + ".stopped.text", GameText.Get("ui.quests.stoppedDescription")));
        }
        else if (progress.HasFlag(QuestMilestones.Completed)) Add(".description");
        return string.Join("\n\n", paragraphs);

        void Add(string suffix)
        {
            if (GameText.Current.TryGet(prefix + suffix, out string text) && text.Length != 0) paragraphs.Add(text);
        }
    }

    public static string Status(GameState state, int id)
    {
        var progress = state.Journal.Progress(state, id);
        return GameText.Get(progress.HasFlag(QuestMilestones.Completed) ? "ui.quests.closed"
            : progress.HasFlag(QuestMilestones.Stopped) ? "ui.quests.stopped" : "ui.quests.active");
    }
}
