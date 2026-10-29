using Atria.Audio;
using Atria.Game.State;
using Atria.Game.Exploration;

namespace Atria.Game.Combat;

/// <summary>408570/413930 encounter setup and owned audio lifetime, before the battle clock begins.</summary>
public sealed class CombatEntryPreparation : IDisposable
{
    private readonly LegacyRandom random;
    private bool selected, disposed;
    public CombatSoundBank Sounds
    {
        get;
    }
    public CombatSceneRuntime Runtime
    {
        get;
    }
    public CombatBattleAssets Assets
    {
        get;
    }
    public CombatEntryRequest Request
    {
        get;
    }
    public int MusicTrack
    {
        get;
    }
    public uint Seed
    {
        get;
    }
    public bool PartyBound => selected;

    public CombatEntryPreparation(
        string resourceRoot,
        CombatEntryRequest request,
        LegacyRandom random,
        Func<uint> readUnixSeconds,
        uint milliseconds,
        Func<ReadOnlyMemory<byte>, IBufferedSoundEffect> loadSound,
        Action<ReadOnlyMemory<byte>> playExclusive,
        HeroCatalog? heroCatalog = null
    ) : this(resourceRoot, request, null, random, readUnixSeconds, milliseconds, loadSound, playExclusive, heroCatalog)
    {
    }

    public static CombatEntryPreparation PrepareRandom(
        string resourceRoot,
        CombatRandomEncounterInputs randomInputs,
        LegacyRandom random,
        Func<uint> readUnixSeconds,
        uint milliseconds,
        Func<ReadOnlyMemory<byte>, IBufferedSoundEffect> loadSound,
        Action<ReadOnlyMemory<byte>> playExclusive,
        HeroCatalog? heroCatalog = null
    ) => new(resourceRoot, default, randomInputs, random, readUnixSeconds, milliseconds, loadSound, playExclusive, heroCatalog);
    private CombatEntryPreparation(
        string resourceRoot,
        CombatEntryRequest request,
        CombatRandomEncounterInputs? randomInputs,
        LegacyRandom random,
        Func<uint> readUnixSeconds,
        uint milliseconds,
        Func<ReadOnlyMemory<byte>, IBufferedSoundEffect> loadSound,
        Action<ReadOnlyMemory<byte>> playExclusive,
        HeroCatalog? heroCatalog = null
    )
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(readUnixSeconds);
        // Validate the encounter domain before acquiring voices or changing the shared RNG.
        if (!randomInputs.HasValue)
        {
            _ = request.ResourceName;
            if (request.Kind == FixedEncounterKind.RandomField)
            {
                throw new ArgumentException("Random entry requires raw encounter inputs so resolution follows the entry seed.", nameof(request));
            }
        }

        this.random = random;
        Sounds = new(resourceRoot, loadSound, playExclusive);
        try
        {
            //413930 preloads0..49 before time(NULL)/srand. Forced music consumes the next rand exactly once.
            Seed = readUnixSeconds();
            random.Seed(Seed);
            Request = randomInputs is { } inputs ? CombatEntryRequest.FromRandom(CombatRandomEncounterResolution.Resolve(inputs, random)) : request;
            MusicTrack = Request.SelectMusic(random.Next);
            var scene = CombatSceneDefinition.Load(resourceRoot, Request, heroCatalog);
            Runtime = new(resourceRoot, scene, milliseconds, id => Sounds.Load(id));
            Assets = CombatBattleAssets.Load(resourceRoot, scene);
            Runtime.BindDeclaredAnimations(milliseconds);
        }
        catch
        {
            Sounds.Dispose();
            throw;
        }
    }

    public void BindSelectedParty(CombatPartySelection selection, int regionIndex, uint milliseconds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (selected)
        {
            throw new InvalidOperationException("Battle party has already been bound.");
        }

        if (!selection.Completed)
        {
            throw new InvalidOperationException("Battle entry requires actual completed party selection.");
        }

        Runtime.PopulateSelectedParty(selection);
        Runtime.BindAllAttributes(regionIndex, (int)Request.Kind, milliseconds, random.Next);
        Runtime.BindPlayerRoles(selection);
        selected = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Sounds.Dispose();
    }
}
