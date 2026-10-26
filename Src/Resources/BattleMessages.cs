namespace Atria.Resources;

/// <summary>Named groups in BATTLE.DAT; indexes belong to the resource layout, not to UI logic.</summary>
public sealed class BattleMessages
{
    private const int RankNoticeCount = 5;
    private const int ItemNoticeStart = 6;
    private const int ItemNoticeCount = 6;
    private const int AutomationIndex = 13;
    private const int PauseIndex = 18;

    public IReadOnlyList<ReadOnlyMemory<byte>> Ranks
    {
        get;
    }
    public IReadOnlyList<ReadOnlyMemory<byte>> Items
    {
        get;
    }
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> labels;
    public ReadOnlyMemory<byte> Automation => labels[AutomationIndex];
    public ReadOnlyMemory<byte> Pause => labels[PauseIndex];

    public BattleMessages(IReadOnlyList<ReadOnlyMemory<byte>> labels)
    {
        if (labels.Count <= PauseIndex)
        {
            throw new InvalidDataException("Incomplete BATTLE.DAT message groups.");
        }

        this.labels = labels;
        Ranks = new MessageGroup(labels, 0, RankNoticeCount);
        Items = new MessageGroup(labels, ItemNoticeStart, ItemNoticeCount);
    }

    private sealed class MessageGroup(IReadOnlyList<ReadOnlyMemory<byte>> source, int offset, int count) : IReadOnlyList<ReadOnlyMemory<byte>>
    {
        public int Count => count;
        public ReadOnlyMemory<byte> this[int index] => (uint)index < (uint)count
            ? source[offset + index] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<ReadOnlyMemory<byte>> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
                yield return this[index];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
