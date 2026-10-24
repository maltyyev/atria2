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
    public const int OriginalEntryCount = 150;
    private readonly Dictionary<int, QuestMilestones> entries = [];
    public IReadOnlyDictionary<int, QuestMilestones> Entries => entries;
    public static int MaximumEntries => GameState.EventStatusCount + StoryQuests.Ids.Count();
    public int CompletionRevision { get; private set; }
    private int? trackedId;

    public bool CanTrack(GameState state, int id)
    {
        if (id <= 0 || id >= OriginalEntryCount && !StoryQuests.Contains(id)) return false;
        var progress = Progress(state, id);
        return progress.HasFlag(QuestMilestones.Started)
            && (progress & (QuestMilestones.Completed | QuestMilestones.Stopped | QuestMilestones.Declined)) == 0;
    }

    public int? ActiveQuest(GameState state) => trackedId is { } id && CanTrack(state, id) ? id : null;

    public void ToggleTracking(GameState state, int id)
    {
        if (CanTrack(state, id)) trackedId = trackedId == id ? null : id;
    }

    public void RestoreTracking(int id)
    {
        if (id != -1 && (id <= 0 || id >= OriginalEntryCount && !StoryQuests.Contains(id))) throw new InvalidDataException("Invalid tracked quest identifier.");
        trackedId = id == -1 ? null : id;
    }

    public QuestMilestones Progress(GameState state, int id)
    {
        var observed = entries.GetValueOrDefault(id);
        var current = (uint)id < GameState.EventStatusCount ? FromStatus(state.EventStatuses[id]) : StoryQuests.Infer(state, id);
        return observed.HasFlag(QuestMilestones.Declined) ? QuestMilestones.None : observed | current;
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
            Store(id, progress);
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
        if ((uint)id >= GameState.EventStatusCount && !StoryQuests.Contains(id) || (progress & ~known) != 0
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
        Store(id, milestone == QuestMilestones.Declined ? milestone : Progress(state, id) | QuestMilestones.Started | milestone);
    }

    private void Store(int id, QuestMilestones progress)
    {
        if (progress.HasFlag(QuestMilestones.Completed) && !entries.GetValueOrDefault(id).HasFlag(QuestMilestones.Completed)) CompletionRevision++;
        entries[id] = progress;
        if (trackedId == id && (progress & (QuestMilestones.Completed | QuestMilestones.Stopped | QuestMilestones.Declined)) != 0) trackedId = null;
    }

    public void CopyFrom(QuestJournal source)
    {
        Clear();
        foreach (var entry in source.entries) entries.Add(entry.Key, entry.Value);
        trackedId = source.trackedId;
    }

    public void Clear()
    {
        entries.Clear();
        CompletionRevision = 0;
        trackedId = null;
    }
}
