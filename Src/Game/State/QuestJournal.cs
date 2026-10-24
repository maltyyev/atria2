namespace Atria.Game.State;

[Flags]
public enum QuestMilestones : byte
{
    None = 0,
    Started = 1,
    ObjectiveReached = 2,
    Completed = 4,
    Stopped = 8,
    LeadFound = 16,
    PursuitContinued = 32,
    Declined = 64
}

/// <summary>Observed journal history, separate from the scenario's mutable offer-status bank.</summary>
public sealed class QuestJournal
{
    private readonly Dictionary<int, QuestMilestones> entries = [];
    public IReadOnlyDictionary<int, QuestMilestones> Entries => entries;

    public QuestMilestones Progress(GameState state, int id)
    {
        var observed = entries.GetValueOrDefault(id);
        return observed.HasFlag(QuestMilestones.Declined) ? QuestMilestones.None : observed | FromStatus(state.EventStatuses[id]);
    }

    public void ObserveStatus(GameState state, int id, int status)
    {
        if (entries.GetValueOrDefault(id).HasFlag(QuestMilestones.Declined) && status == 4) return;
        var progress = Progress(state, id) | FromStatus(status);
        if (status == 3 && progress.HasFlag(QuestMilestones.Started) && !progress.HasFlag(QuestMilestones.Completed))
        {
            progress |= QuestMilestones.Stopped;
        }
        if (status is 1 or 2)
        {
            progress &= ~QuestMilestones.Stopped;
        }
        if (progress != QuestMilestones.None)
        {
            entries[id] = progress;
        }
    }

    public static QuestMilestones FromStatus(int status) => status switch
    {
        1 => QuestMilestones.Started,
        2 => QuestMilestones.Started | QuestMilestones.ObjectiveReached,
        4 => QuestMilestones.Started | QuestMilestones.Completed,
        _ => QuestMilestones.None
    };

    public void Restore(int id, QuestMilestones progress)
    {
        const QuestMilestones known = QuestMilestones.Started | QuestMilestones.ObjectiveReached | QuestMilestones.Completed
            | QuestMilestones.Stopped | QuestMilestones.LeadFound | QuestMilestones.PursuitContinued | QuestMilestones.Declined;
        if ((uint)id >= GameState.EventStatusCount || (progress & ~known) != 0
            || !progress.HasFlag(QuestMilestones.Started) && progress != QuestMilestones.Declined)
        {
            throw new InvalidDataException("Invalid saved quest journal milestone.");
        }
        if (!entries.TryAdd(id, progress))
        {
            throw new InvalidDataException("Duplicate saved quest journal entry.");
        }
    }

    public void Record(GameState state, int id, QuestMilestones milestone)
    {
        entries[id] = milestone == QuestMilestones.Declined ? milestone : Progress(state, id) | QuestMilestones.Started | milestone;
    }

    public void CopyFrom(QuestJournal source)
    {
        entries.Clear();
        foreach (var entry in source.entries) entries.Add(entry.Key, entry.Value);
    }

    public void Clear() => entries.Clear();
}
