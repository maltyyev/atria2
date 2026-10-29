using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Atria.Game.Combat;

public sealed class CombatInfoBinding
{
    public int NativeIndex
    {
        get;
    }
    public CombatInfoCharacter Info
    {
        get;
    }
    public CombatAnimationAssets Assets
    {
        get;
    }

    internal CombatInfoBinding(int index, int animationIndex, CombatInfoCharacter source, CombatAnimationAssets assets)
    {
        NativeIndex = index;
        Assets = assets;
        Info = source with
        {
        };
    }
}

public sealed class CombatActorImage
{
    public CombatActorState State { get; } = new();
    public CombatInfoBinding? Binding
    {
        get; internal set;
    }
    public CombatAnimationCursor? Animation
    {
        get; internal set;
    }
    public int ControlPlayer { get; internal set; } = -1;
    public int CurrentCommandIndex { get; internal set; } = -1; //native actor0 P-record pointer
    public int InitialHealth
    {
        get; internal set;
    }
    public bool IsSummon
    {
        get; internal set;
    }
    public bool IsRitualAnchor
    {
        get; internal set;
    }
}

/// <summary>4128c0/412fa0 actual pointer-free actor and shared INF ownership, without claiming battle completion.</summary>
public sealed class CombatSceneRuntime
{
    private const int WeakEncounterHealthPercent = 30;
    private readonly string root;
    private readonly Action<int>? loadNumericSound;
    private readonly List<CombatInfoBinding> bindings = [];
    private readonly List<CombatAnimationAssets> animations = [];
    private readonly CombatActorImage[] actors;
    private bool declaredBound, partyPopulated, attributesBound;
    public CombatSceneDefinition Scene
    {
        get;
    }
    public IReadOnlyList<CombatActorImage> Actors
    {
        get;
    }
    public IReadOnlyList<CombatInfoBinding> InfoBindings => bindings.AsReadOnly();
    public int[] TargetIndices
    {
        get;
    }
    public int InitialControlRank { get; } = 3; //413930 default45acfc
    public bool IsWeakEncounter
    {
        get; private set;
    }
    public CombatSummons Summons
    {
        get;
    }
    public int AccumulatedAward
    {
        get; private set;
    }

    public CombatSceneRuntime(string resourceRoot, CombatSceneDefinition scene, uint milliseconds, Action<int>? loadNumericSound = null)
    {
        root = resourceRoot;
        Scene = scene;
        this.loadNumericSound = loadNumericSound;
        actors = Enumerable.Range(0, scene.RuntimeActorCount).Select(_ => new CombatActorImage()).ToArray();
        Actors = Array.AsReadOnly(actors);
        TargetIndices = Enumerable.Repeat(-1, actors.Length).ToArray();
        Summons = new(this);
        byte[] typeCache = new byte[CombatSceneBaselines.Count];
        int next = 0, markerFacing = 1;
        for (int i = 0; i < scene.Source.Count; i++)
        {
            var source = scene.Source.GetObject(i);
            int type = source.GetInteger(2);
            if (type == 0)
            {
                markerFacing = source.GetInteger(10);
                if (markerFacing is 0 or 1)
                {
                    markerFacing = 1;
                }

                for (int party = 0; party < scene.HeroCatalog.Count; party++)
                {
                    CombatActorState record = actors[scene.PartyStartIndex + party].State;
                    record.AnchorX = source.GetInteger(0);
                    record.SpriteX = source.GetInteger(0);
                    record.Depth = scene.PartySpawns[party].Y;
                    record.Facing = unchecked((byte)markerFacing);
                    record.Team = 100;
                    record.CanTurn = 1;
                }

                continue;
            }

            if ((uint)type >= 60)
            {
                throw new InvalidDataException("Combat scene type exceeds native cache.");
            }

            if (typeCache[type] == 0)
            {
                typeCache[type] = checked((byte)LoadInfo(Encoding.ASCII.GetBytes(type.ToString(CultureInfo.InvariantCulture) + ".inf")).NativeIndex);
            }

            CombatInfoBinding binding = bindings[typeCache[type]];
            CombatActorImage actor = actors[next++];
            CombatActorState data = actor.State;
            actor.Binding = binding;
            actor.Animation = new(data, binding.Info, binding.Assets);
            for (int slot = 0; slot < 6; slot++)
            {
                data.Equipment[slot] = -1;
            }

            data.MemberStatus = 1;
            data.MaximumHealth = source.GetInteger(6);
            data.Health = source.GetInteger(6);
            data.MaximumMana = source.GetInteger(7);
            data.Mana = source.GetInteger(7);
            data.HealthBarMaximum = source.GetInteger(6);
            data.ManaBarMaximum = source.GetInteger(7);
            data.Attack = unchecked(source.GetInteger(8) + 20);
            data.Defense = source.GetInteger(9);
            data.Rank = 1;
            data.MagicRank = 1;
            data.SpawnCondition = source.GetInteger(13);
            data.SpawnPlacement = source.GetInteger(14);
            data.SpawnStartTime = unchecked((int)milliseconds);
            data.SpawnEncounterThreshold = source.GetInteger(12);
            data.JumpSequenceIndex = binding.NativeIndex;
            data.AnchorX = source.GetInteger(0);
            data.TrackingX = source.GetInteger(0);
            data.Depth = unchecked(source.GetInteger(1) + CombatSceneBaselines.GetDepthOffset(type));
            data.Facing = actors[scene.PartyStartIndex].State.AnchorX <= source.GetInteger(0) ? (byte)255 : (byte)1;
            int team = source.GetInteger(15);
            if (team == 0)
            {
                team = 1;
            }

            data.Team = unchecked((byte)team);
            if (unchecked((sbyte)data.Team) > 99)
            {
                data.Facing = unchecked((byte)markerFacing);
            }

            data.CanTurn = source.GetInteger(10) < 2 ? (byte)1 : (byte)0;
            for (int name = 0; name < 3; name++)
            {
                ReadOnlySpan<byte> text = source.GetString(name).Span;
                if (text.Length >= 20)
                {
                    throw new InvalidDataException("Combat scene string exceeds native20-byte actor destination.");
                }

                text.CopyTo(data.SceneTexts[name]);
            }
        }
    }

    public void BindDeclaredAnimations(uint milliseconds)
    {
        if (declaredBound)
        {
            throw new InvalidOperationException("Declared combat animations are already initialized.");
        }

        for (int i = 0; i < Scene.PartyStartIndex; i++)
        {
            BindInitial(actors[i], milliseconds);
        }

        declaredBound = true;
    }

    public void PopulateSelectedParty(CombatPartySelection selection)
    {
        if (!selection.Completed)
        {
            throw new InvalidOperationException("Actual prebattle confirmation is required.");
        }

        if (!declaredBound || partyPopulated)
        {
            throw new InvalidOperationException("Combat party population is out of native setup order.");
        }

        if (selection.Party.Count != Scene.HeroCatalog.Count)
        {
            throw new InvalidDataException("Combat roster differs from selection.");
        }

        for (int party = 0; party < Scene.HeroCatalog.Count; party++)
        {
            var source = selection.Party[party];
            if (source.PendingAction == 0)
            {
                continue;
            }

            CombatActorImage actor = actors[Scene.PartyStartIndex + party];
            CombatActorState data = actor.State;
            Atria.Resources.CombatActorCodec.ImportParty(Atria.Resources.PartyCodec.EncodeMember(source), data);
            data.CommandSelection = InitialControlRank;
            data.ControlRank = InitialControlRank;
            data.HealthBarMaximum = data.MaximumHealth;
            data.ManaBarMaximum = data.MaximumMana;
            if (data.Health < data.MaximumHealth)
            {
                data.Health = unchecked(data.Health + 1);
            }

            int form = source.CombatForm;
            CombatInfoBinding binding = LoadInfo(Encoding.ASCII.GetBytes(Scene.HeroCatalog[party].BattleResource(form)));
            actor.Binding = binding;
            actor.Animation = new(data, binding.Info, binding.Assets);
            data.JumpSequenceIndex = binding.NativeIndex;
        }

        partyPopulated = true;
        Summons.Prepare(selection, LoadInfo);
    }

    public void BindAllAttributes(int regionIndex, int variant, uint milliseconds, Func<int> nextNativeRandom)
    {
        if (!partyPopulated || attributesBound)
        {
            throw new InvalidOperationException("Combat attributes must initialize once after actual party selection.");
        }

        int award = AccumulatedAward;
        foreach (CombatActorImage actor in actors)
        {
            if (actor.Binding is not { } binding || actor.State.Rank == 0 || actor.State.Active == 0)
            {
                continue;
            }

            CombatEnemyAttributes.Initialize(actor.State, binding.Info, regionIndex, variant, nextNativeRandom, ref award);
            actor.State.JumpSequenceIndex = 0;
            BindInitial(actor, milliseconds);
        }

        AccumulatedAward = award;
        AuthoredEncounters.Apply(this);
        CaptureEncounterStrength();
        attributesBound = true;
    }

    internal void CaptureEncounterStrength()
    {
        int weakestAlly = int.MaxValue, strongestEnemy = 0;
        foreach (var image in actors)
        {
            image.InitialHealth = image.Binding is not null && image.State.Active != 0 ? Math.Max(0, image.State.Health) : 0;
            if (image.InitialHealth == 0)
            {
                continue;
            }

            if (image.State.Team >= 100)
            {
                weakestAlly = Math.Min(weakestAlly, image.InitialHealth);
            }
            else
            {
                strongestEnemy = Math.Max(strongestEnemy, image.InitialHealth);
            }
        }

        IsWeakEncounter = weakestAlly != int.MaxValue && strongestEnemy > 0
            && (long)strongestEnemy * 100 <= (long)weakestAlly * WeakEncounterHealthPercent;
    }

    public void BindPlayerRoles(CombatPartySelection selection)
    {
        if (!selection.Completed)
        {
            throw new InvalidOperationException("Actual party leader selection is required.");
        }

        if (!attributesBound)
        {
            throw new InvalidOperationException("Bind actual player roles after party population and final attribute initialization.");
        }

        for (int player = 0; player < selection.PlayerCount; player++)
        {
            CombatActorImage actor = actors[Scene.PartyStartIndex + selection.Leaders[player]];
            actor.State.ControlledBy = checked((byte)(player + 1));
            actor.ControlPlayer = player;
        }

        CombatActorImage leader = actors[Scene.PartyStartIndex + selection.Leaders[0]];
        CombatPartyControl.Flash(leader.State, 0xffff);
        leader.State.ControlRole = 1;
    }

    private static void BindInitial(CombatActorImage actor, uint milliseconds)
    {
        if (actor.Animation is null || actor.State.Rank == 0)
        {
            return;
        }

        actor.State.Active = 1;
        actor.Animation.BeginMotion(0, milliseconds);
        actor.State.ChainDeadline = unchecked((int)milliseconds + 50);
    }

    private CombatInfoBinding LoadInfo(ReadOnlySpan<byte> name)
    {
        CombatInfoDefinition file = CombatInfoDefinition.Load(root, name, loadNumericSound);
        int firstAnimation = animations.Count;
        foreach (ReadOnlyMemory<byte> resource in file.AnimationResources)
        {
            animations.Add(CombatAnimationAssets.Load(root, resource.Span));
        }

        foreach (CombatInfoCharacter character in file.Characters)
        {
            int animationIndex = firstAnimation + character.AnimationOrdinal;
            bindings.Add(new(bindings.Count, animationIndex, character, animations[animationIndex]));
        }

        if (file.Characters.Count == 0)
        {
            throw new InvalidDataException("Combat info resource has no character definition.");
        }

        return bindings[^1];
    }

    private static int I(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
}
