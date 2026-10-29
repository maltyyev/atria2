using System.Buffers.Binary;

namespace Atria.Game.State;

/// <summary>Recovered storage domains. Zero construction is not a new-game initializer.</summary>
public sealed class GameState
{
    public const int GlobalCount = 1000, EventCount = 1000, EventStatusCount = 200;
    public const int LegacyPartyRecordCount = 10, PartyRecordSize = 280, InventoryBankCount = 200;
    public const int QuestItemCount = 51, QuestItemRecordSize = 74;
    // 4cb910: signed 16-bit globals; 46c938: flat int[1000]; 46d8d8: int[200].
    public short[] Globals { get; } = new short[GlobalCount];
    public int[] Events { get; } = new int[EventCount];
    public int[] EventStatuses { get; } = new int[EventStatusCount];
    public QuestJournal Journal { get; } = new();
    // Preserve uninterpreted fields until actor/save recovery establishes their meaning.
    public PartyMember[] Party { get; }
    public PartyMember[] Heroes { get; }
    public HeroCatalog HeroCatalog { get; }

    public GameState(HeroCatalog? heroCatalog = null)
    {
        HeroCatalog = heroCatalog ?? HeroCatalog.Default;
        Party = Enumerable.Range(0, HeroCatalog.StorageCount).Select(_ => new PartyMember()).ToArray();
        Heroes = Enumerable.Range(0, HeroCatalog.Count).Select(index => Party[HeroCatalog[index].StorageIndex]).ToArray();
    }

    public bool HasHero(string id) => HeroCatalog.Find(id) is var index && index >= 0 && Heroes[index].MemberStatus != 0;
    public int[] OwnedItems { get; } = new int[InventoryBankCount];
    public int[] AvailableItems { get; } = new int[InventoryBankCount];
    public byte[] QuestItemRecords { get; } = new byte[QuestItemCount * QuestItemRecordSize];
    public int Money
    {
        get; set;
    } // 46fc92
    public int Score
    {
        get; set;
    } // 46fc96
    public int ResourceCounterDc88
    {
        get; set;
    } // 46dc88: semantic label pending
    public int ResourceCounterFc9a
    {
        get; set;
    } // 46fc9a: semantic label pending
    public int EventCounter
    {
        get; set;
    } // 46c910
    public int DayCounterStorage
    {
        get; set;
    } // 47019c: saved as 4 bytes
    public int ScriptDay => unchecked((sbyte)DayCounterStorage);
    public int Mode
    {
        get; set;
    } // 470194; 1100 suppresses selected callbacks
    public int ModeStackDepth
    {
        get; set;
    } // 470198
    public int EnterPermission
    {
        get; set;
    } // 468104
    public int ScriptActive
    {
        get; set;
    } // 470f70
    public int ShopNumber
    {
        get; set;
    } // 46fca4
    public int PendingExperience
    {
        get; set;
    } // 46dc08
    public int ExperiencePending
    {
        get; set;
    } // 46fd64
    public int PendingBattle
    {
        get; set;
    } // 46fd4c
    public int BattleVariant
    { // Native4cb99c is a WORD alias of globals[70]; globals[71] is independent.
        get => unchecked((ushort)Globals[70]); set => Globals[70] = unchecked((short)value);
    }
    public byte[] PendingDungeonMap { get; set; } = [];
    public byte[] PendingDungeonScript { get; set; } = [];
    public int DungeonEntryPending
    {
        get; set;
    } // 46fcd0
    public int ChangedChip
    {
        get; set;
    } // 46fd58
    public int DungeonFog
    {
        get; set;
    } // 46fce4
    public int DungeonFogDirty
    {
        get; set;
    } // 463c20
    public int NewCharacterPending
    {
        get; set;
    } // 46fcdc
    public int NewCharacterId
    {
        get; set;
    } // 46fd5c
    public int VisualMode
    {
        get; set;
    } // 45169c
    public int EndingSelection
    {
        get; set;
    } // 46fca0
    public int EndingResetFlag
    {
        get; set;
    } // 46fd6c
    public int WorldIndex
    {
        get; set;
    } // 46dc10
    public int WorldX
    {
        get; set;
    } // 46dc14
    public int WorldY
    {
        get; set;
    } // 46dc18
    public int Year
    {
        get; set;
    } // 46fcc0
    public int Month
    {
        get; set;
    } // 46fcc4
    public int Day
    {
        get; set;
    } // 46fcc8
    public int InitializedPartyCount
    {
        get; set;
    } // 46fc8e
    public IReadOnlyList<ScriptPerson> People { get; internal set; } = Array.Empty<ScriptPerson>();

    public int GetMemberStatus(int index) => Party[index].MemberStatus;
    public void SetMemberStatus(int index, int value) => Party[index].MemberStatus = value;
    public int GetQuestItemFlag(int index) => BinaryPrimitives.ReadInt32LittleEndian(QuestItemRecords.AsSpan(QuestItemOffset(index), 4));
    public void SetQuestItemFlag(int index, int value) => BinaryPrimitives.WriteInt32LittleEndian(QuestItemRecords.AsSpan(QuestItemOffset(index), 4), value);
    private static int QuestItemOffset(int index) => (uint)index < QuestItemCount ? index * QuestItemRecordSize : throw new ArgumentOutOfRangeException(nameof(index));
}

public sealed record ScriptPerson(ReadOnlyMemory<byte> DisplayName, ReadOnlyMemory<byte> LegacyName, ReadOnlyMemory<byte> Handler);
