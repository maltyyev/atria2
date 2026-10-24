using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Atria.Resources;

namespace Atria.Game.Combat;

public enum FixedEncounterKind
{
    Scripted = 0,
    Forced = 1,
    RandomField = 2
}

/// <summary>408570 encounter identity; variant2 must carry an already resolved random scene.</summary>
public readonly record struct CombatEntryRequest(FixedEncounterKind Kind, int Number)
{
    public CombatRandomEncounterResolution? RandomScene
    {
        get; private init;
    }

    public static CombatEntryRequest FromRandom(CombatRandomEncounterResolution scene) => new(FixedEncounterKind.RandomField, scene.SceneNumber)
    {
        RandomScene = scene
    };
    public string ResourceName => Kind switch
    {
        FixedEncounterKind.Scripted => "SBATTL" + Number.ToString("D2", CultureInfo.InvariantCulture),
        FixedEncounterKind.Forced => "EBAT" + Number.ToString("D3", CultureInfo.InvariantCulture),
        FixedEncounterKind.RandomField when RandomScene is { } scene => scene.ResourceName,
        _ => throw new NotSupportedException("Unsupported fixed encounter kind.")
    };

    // Forced encounter selection consumes exactly one native rand() call; do not use System.Random.
    public int SelectMusic(Func<int> nextNativeRandom) => Kind switch
    {
        FixedEncounterKind.Scripted => 24,
        FixedEncounterKind.Forced => 22 + nextNativeRandom() % 2,
        FixedEncounterKind.RandomField when RandomScene.HasValue => CombatRandomEncounterResolution.MusicTrack,
        _ => throw new NotSupportedException("Unsupported fixed encounter kind.")
    };
}

public readonly record struct PartySpawn(int RuntimeIndex, int Actor, int X, int Y, sbyte Facing);
/// <summary>Unmodified scene actor declaration; no inferred AI or combat outcome.</summary>
public sealed record CombatSceneActor(int RuntimeIndex, SodObject Source)
{
    public int Type => Source.GetInteger(2);
    public int X => Source.GetInteger(0);
    public int Y => Source.GetInteger(1);
    public int SpawnState => Source.GetInteger(13); //4128c0 -> runtime60
    public int SpawnField64 => Source.GetInteger(14);
    public string InfoResource => Type.ToString(CultureInfo.InvariantCulture) + ".inf";
}

/// <summary>4128c0 disk scene stage, before424100 player selection and412fa0 party projection.</summary>
public sealed class CombatSceneDefinition
{
    public const int RuntimeRecordSize = 0x379;
    public CombatEntryRequest Request
    {
        get;
    }
    public SodV2 Source
    {
        get;
    }
    public int RuntimeActorCount => checked(Source.Count + 6);
    public int PartyStartIndex => Source.Count;
    public int Header73
    {
        get;
    }
    public int Header77
    {
        get;
    }
    public IReadOnlyList<CombatSceneActor> Actors
    {
        get;
    }
    public IReadOnlyList<PartySpawn> PartySpawns
    {
        get;
    }

    public CombatSceneDefinition(CombatEntryRequest request, SodV2 source)
    {
        Request = request;
        Source = source;
        // Native allocates0xf316 bytes for actors: at most68 complete913-byte records.
        if (source.Count > 62)
        {
            throw new InvalidDataException("Battle actors exceed native runtime record allocation.");
        }

        Header73 = BinaryPrimitives.ReadInt32LittleEndian(source.Header.Span[73..]);
        Header77 = BinaryPrimitives.ReadInt32LittleEndian(source.Header.Span[77..]);
        var actors = new List<CombatSceneActor>(source.Count);
        var spawns = new PartySpawn[6];
        bool marker = false;
        for (int i = 0; i < source.Count; i++)
        {
            SodObject obj = source.GetObject(i);
            int type = obj.GetInteger(2);
            if (type != 0)
            {
                // Native indexes the60-byte zeroed local_1fc type lookup with this value.
                if ((uint)type >= 60)
                {
                    throw new InvalidDataException("Battle info type exceeds native lookup storage.");
                }

                actors.Add(new CombatSceneActor(actors.Count, obj));
                continue;
            }

            marker = true;
            int facing = obj.GetInteger(10);
            if (facing is 0 or 1)
            {
                facing = 1;
            }

            for (int actor = 0; actor < 6; actor++)
            {
                spawns[actor] = new PartySpawn(PartyStartIndex + actor, actor, obj.GetInteger(0), unchecked(Header73 + 10 + actor * 20), unchecked((sbyte)facing));
            }
        }

        if (!marker)
        {
            throw new NotSupportedException("Battle scene has no recovered party spawn marker.");
        }

        Actors = actors.AsReadOnly();
        PartySpawns = Array.AsReadOnly(spawns);
    }

    public static CombatSceneDefinition Load(string resourceRoot, CombatEntryRequest request)
    {
        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "SOD.000"));
        int index = archive.FindResource(Encoding.ASCII.GetBytes(request.ResourceName));
        if (index < 0)
        {
            throw new FileNotFoundException("Required battle scene is missing.", request.ResourceName);
        }

        return new CombatSceneDefinition(request, SodV2.Parse(archive.ReadMember(index)));
    }
}
