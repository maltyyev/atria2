using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Persistence;

/// <summary>Version1 native-only GameState schema. Explicit fields avoid reflection/AOT and do not invent legacy storage.</summary>
public static class NativeGameStateCodec
{
    private const uint JournalExtension = 0x4c4e524a; // JRNL, optional tail for pre-0.7.1 saves.
    private const int JournalVersion = 2;
    private const uint HeroesExtension = 0x4f524548; // HERO: stable identities, separate from legacy escort records.
    private const int HeroesVersion = 1;
    public const int MaxBytes = 300000;
    public static byte[] Encode(GameState s)
    {
        if (s.People.Count > 20)
        {
            throw new InvalidDataException("Saved people exceed20 recovered slots.");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int[] scalars = [s.Money, s.Score, s.ResourceCounterDc88, s.ResourceCounterFc9a, s.EventCounter, s.DayCounterStorage, s.Mode, s.ModeStackDepth, s.EnterPermission, s.ScriptActive, s.ShopNumber, s.PendingExperience, s.ExperiencePending, s.PendingBattle, s.DungeonEntryPending, s.ChangedChip, s.DungeonFog, s.DungeonFogDirty, s.NewCharacterPending, s.NewCharacterId, s.VisualMode, s.EndingSelection, s.EndingResetFlag, s.WorldIndex, s.WorldX, s.WorldY, s.Year, s.Month, s.Day, s.InitializedPartyCount];
        writer.Write(scalars.Length);
        foreach (int value in scalars)
        {
            writer.Write(value);
        }

        writer.Write(PartyCodec.Encode(s.Party.Take(GameState.LegacyPartyRecordCount).ToArray()));
        foreach (int value in s.OwnedItems)
        {
            writer.Write(value);
        }

        foreach (int value in s.AvailableItems)
        {
            writer.Write(value);
        }

        writer.Write(s.QuestItemRecords);
        foreach (int value in s.Events)
        {
            writer.Write(value);
        }

        foreach (int value in s.EventStatuses)
        {
            writer.Write(value);
        }

        foreach (short value in s.Globals)
        {
            writer.Write(value);
        }

        WriteBytes(writer, s.PendingDungeonMap, 4096);
        WriteBytes(writer, s.PendingDungeonScript, 4096);
        writer.Write(s.People.Count);
        foreach (var person in s.People)
        {
            WriteBytes(writer, person.DisplayName.Span, 4096);
            WriteBytes(writer, person.LegacyName.Span, 4096);
            WriteBytes(writer, person.Handler.Span, 4096);
        }

        if (s.Journal.Entries.Count != 0 || s.Journal.ActiveQuest(s).HasValue)
        {
            writer.Write(JournalExtension);
            writer.Write(JournalVersion);
            writer.Write(s.Journal.Entries.Count);
            foreach (var entry in s.Journal.Entries.OrderBy(entry => entry.Key))
            {
                writer.Write(entry.Key);
                writer.Write((byte)entry.Value);
            }
            writer.Write(s.Journal.ActiveQuest(s) ?? -1);
        }
        var additional = Enumerable.Range(0, s.HeroCatalog.Count)
            .Where(i => s.HeroCatalog[i].StorageIndex >= GameState.LegacyPartyRecordCount).ToArray();
        if (additional.Length != 0)
        {
            writer.Write(HeroesExtension);
            writer.Write(HeroesVersion);
            writer.Write(additional.Length);
            foreach (int index in additional)
            {
                WriteBytes(writer, System.Text.Encoding.UTF8.GetBytes(s.HeroCatalog[index].Id), 128);
                writer.Write(PartyCodec.EncodeMember(s.Heroes[index]));
            }
        }
        writer.Flush();
        if (stream.Length > MaxBytes)
        {
            throw new InvalidDataException("Native GameState exceeds bounded schema capacity.");
        }

        return stream.ToArray();
    }

    public static GameState Decode(ReadOnlySpan<byte> bytes, HeroCatalog? heroCatalog = null)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new InvalidDataException("Native GameState exceeds bounded schema capacity.");
        }

        using var stream = new MemoryStream(bytes.ToArray(), false);
        using var reader = new BinaryReader(stream);
        try
        {
            if (reader.ReadInt32() != 30)
            {
                throw new InvalidDataException("Native GameState scalar schema is invalid.");
            }

            int[] v = new int[30];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = reader.ReadInt32();
            }

            var s = new GameState(heroCatalog);
            s.HeroCatalog.InitializeAdditionalHeroes(s);
            ApplyScalars(v, s);
            PartyCodec.Decode(ReadExact(reader, GameState.LegacyPartyRecordCount * PartyCodec.RecordSize), s.Party.Take(GameState.LegacyPartyRecordCount).ToArray());
            ReadInts(reader, s.OwnedItems);
            ReadInts(reader, s.AvailableItems);
            ReadExact(reader, 3774).CopyTo(s.QuestItemRecords, 0);
            ReadInts(reader, s.Events);
            ReadInts(reader, s.EventStatuses);
            for (int i = 0; i < 1000; i++)
            {
                s.Globals[i] = reader.ReadInt16();
            }

            s.PendingDungeonMap = ReadBytes(reader, 4096);
            s.PendingDungeonScript = ReadBytes(reader, 4096);
            int count = reader.ReadInt32();
            if ((uint)count > 20)
            {
                throw new InvalidDataException("Native people count exceeds20 slots.");
            }

            var people = new ScriptPerson[count];
            for (int i = 0; i < count; i++)
            {
                people[i] = new(ReadBytes(reader, 4096), ReadBytes(reader, 4096), ReadBytes(reader, 4096));
            }

            s.People = people;
            var extensions = new HashSet<uint>();
            while (stream.Position < stream.Length)
            {
                uint signature = reader.ReadUInt32();
                int version = reader.ReadInt32();
                if (!extensions.Add(signature)) throw new InvalidDataException("Duplicate native state extension.");
                if (signature == HeroesExtension)
                {
                    if (version != HeroesVersion) throw new InvalidDataException("Unknown hero save version.");
                    int countHeroes = reader.ReadInt32();
                    if (countHeroes < 0 || countHeroes > (stream.Length - stream.Position) / PartyCodec.RecordSize)
                        throw new InvalidDataException("Truncated hero records.");
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < countHeroes; i++)
                    {
                        string id = System.Text.Encoding.UTF8.GetString(ReadBytes(reader, 128));
                        int index = s.HeroCatalog.Find(id);
                        if (!ids.Add(id) || index < 0 || s.HeroCatalog[index].StorageIndex < GameState.LegacyPartyRecordCount)
                            throw new InvalidDataException($"Unknown or duplicate saved hero: {id}");
                        PartyCodec.DecodeMember(ReadExact(reader, PartyCodec.RecordSize), s.Heroes[index]);
                    }
                    continue;
                }
                if (signature != JournalExtension || version is < 1 or > JournalVersion)
                {
                    throw new InvalidDataException("Unknown native journal extension.");
                }
                int entries = reader.ReadInt32();
                if ((uint)entries > QuestJournal.MaximumEntries) throw new InvalidDataException("Saved journal entry count exceeds capacity.");
                for (int i = 0; i < entries; i++) s.Journal.Restore(reader.ReadInt32(), (QuestMilestones)reader.ReadByte());
                if (version >= 2) s.Journal.RestoreTracking(reader.ReadInt32());
            }
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Native GameState has trailing bytes outside its schema.");
            }

            return s;
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException("Native GameState is truncated.", error);
        }
    }

    public static void Apply(GameState source, GameState target)
    {
        // Validate and detach before copying anything into the live state.
        var s = Decode(Encode(source), target.HeroCatalog);
        LegacySnapshotBridge.CopyRecoveredState(s, target);
        target.ModeStackDepth = s.ModeStackDepth;
        target.EnterPermission = s.EnterPermission;
        target.ScriptActive = s.ScriptActive;
        target.ShopNumber = s.ShopNumber;
        target.PendingExperience = s.PendingExperience;
        target.ExperiencePending = s.ExperiencePending;
        target.PendingBattle = s.PendingBattle;
        target.DungeonEntryPending = s.DungeonEntryPending;
        target.ChangedChip = s.ChangedChip;
        target.DungeonFog = s.DungeonFog;
        target.DungeonFogDirty = s.DungeonFogDirty;
        target.NewCharacterPending = s.NewCharacterPending;
        target.NewCharacterId = s.NewCharacterId;
        target.VisualMode = s.VisualMode;
        target.EndingSelection = s.EndingSelection;
        target.EndingResetFlag = s.EndingResetFlag;
        target.PendingDungeonMap = s.PendingDungeonMap;
        target.PendingDungeonScript = s.PendingDungeonScript;
        target.People = s.People;
    }

    private static void ApplyScalars(int[] v, GameState s)
    {
        s.Money = v[0];
        s.Score = v[1];
        s.ResourceCounterDc88 = v[2];
        s.ResourceCounterFc9a = v[3];
        s.EventCounter = v[4];
        s.DayCounterStorage = v[5];
        s.Mode = v[6];
        s.ModeStackDepth = v[7];
        s.EnterPermission = v[8];
        s.ScriptActive = v[9];
        s.ShopNumber = v[10];
        s.PendingExperience = v[11];
        s.ExperiencePending = v[12];
        s.PendingBattle = v[13];
        s.DungeonEntryPending = v[14];
        s.ChangedChip = v[15];
        s.DungeonFog = v[16];
        s.DungeonFogDirty = v[17];
        s.NewCharacterPending = v[18];
        s.NewCharacterId = v[19];
        s.VisualMode = v[20];
        s.EndingSelection = v[21];
        s.EndingResetFlag = v[22];
        s.WorldIndex = v[23];
        s.WorldX = v[24];
        s.WorldY = v[25];
        s.Year = v[26];
        s.Month = v[27];
        s.Day = v[28];
        s.InitializedPartyCount = v[29];
    }

    internal static void WriteBytes(BinaryWriter writer, ReadOnlySpan<byte> bytes, int maximum)
    {
        if (bytes.Length > maximum)
        {
            throw new InvalidDataException("Native variable field exceeds bounded capacity.");
        }

        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    internal static byte[] ReadBytes(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if ((uint)count > (uint)maximum)
        {
            throw new InvalidDataException("Native variable field length exceeds bounded capacity.");
        }

        return ReadExact(reader, count);
    }

    internal static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] result = reader.ReadBytes(count);
        if (result.Length != count)
        {
            throw new EndOfStreamException();
        }

        return result;
    }

    private static void ReadInts(BinaryReader reader, int[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadInt32();
        }
    }
}
