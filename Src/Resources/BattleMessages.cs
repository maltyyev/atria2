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
    public ReadOnlyMemory<byte> Automation
    {
        get;
    }
    public ReadOnlyMemory<byte> Pause
    {
        get;
    }

    public BattleMessages(IReadOnlyList<ReadOnlyMemory<byte>> labels)
    {
        if (labels.Count <= PauseIndex)
        {
            throw new InvalidDataException("Incomplete BATTLE.DAT message groups.");
        }

        Ranks = labels.Take(RankNoticeCount).ToArray();
        Items = labels.Skip(ItemNoticeStart).Take(ItemNoticeCount).ToArray();
        Automation = labels[AutomationIndex];
        Pause = labels[PauseIndex];
    }
}
