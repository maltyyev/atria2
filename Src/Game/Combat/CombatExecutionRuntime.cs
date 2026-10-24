using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>One serial owner for recovered actor/hit/visual stages. Caller supplies opportunities; no assumed frequency.</summary>
public sealed class CombatExecutionRuntime : ICombatControlEffects
{
    public CombatSceneRuntime Runtime => entry.Runtime;

    private readonly CombatEntryPreparation entry;
    private readonly GameState state;
    private readonly Func<int> nextRandom, attenuation;
    private readonly Action<int, uint> rankNotice, itemNotice;
    private readonly Action<int> sound;
    private readonly Action<CombatParticleState> collide;
    private readonly Func<bool> magicActive;
    private readonly CombatParticleImpactAdapter impacts;
    private readonly CombatHitPass hitPass;
    private readonly ushort[] pixels = new ushort[640 * 480];
    private uint now;
    public CombatParticleSystem Particles
    {
        get;
    }
    public CombatHitExecutor Hits
    {
        get;
    }
    public CombatParticleMagicRuntime Magic
    {
        get;
    }
    public CombatCameraBounds Camera
    {
        get;
    }
    public CombatActorController Controller
    {
        get;
    }
    public CombatFrameRuntime Frames
    {
        get;
    }
    public CombatSceneRenderer Renderer
    {
        get;
    }
    public CombatHudRaster Hud
    {
        get;
    }
    public ReadOnlyMemory<ushort> CachedFrame => pixels;
    public bool HasCachedFrame
    {
        get; private set;
    }
    public bool GlobalEffectBusy => Magic.ActiveMagic is not null;
    public int VisualDetail
    {
        get => Frames.VisualDetail; set => Frames.VisualDetail = value;
    }

    public CombatExecutionRuntime(
        string root,
        CombatEntryPreparation entry,
        GameState state,
        LegacyRandom random,
        ControlBindings bindings,
        CombatScanInput input,
        Func<int> currentAttenuation,
        Action<int, uint> showRankNotice,
        Action<int, uint> showConsumedItemNotice
    )
    {
        if (!entry.PartyBound)
        {
            throw new InvalidOperationException("Execution requires actual completed party binding.");
        }

        this.entry = entry;
        this.state = state;
        nextRandom = random.Next;
        attenuation = currentAttenuation;
        rankNotice = showRankNotice;
        itemNotice = showConsumedItemNotice;
        sound = PlaySound;
        Particles = CombatParticleSystem.Load(root);
        Hits = new(entry.Runtime, state, nextRandom, SpawnHitParticle, sound);
        Magic = new(root, entry.Runtime, Particles, Hits, () => now, nextRandom, sound, ApplyScreenEffect)
        {
            GlobalDepth = entry.Runtime.Scene.Header73
        };
        Camera = entry.Assets.CreateCamera(entry.Runtime, Particles.Translate);
        Controller = new(entry.Runtime, state, bindings, input, this, nextRandom);
        Frames = new(entry.Runtime, state, Camera, Controller, this, nextRandom);
        impacts = new(entry.Runtime, Particles, Hits, () => now, () => VisualDetail, sound, Magic.Transform);
        hitPass = new(entry.Runtime, Hits);
        collide = Collide;
        magicActive = () => GlobalEffectBusy;
        Renderer = new(entry.Runtime, entry.Assets, Camera, Frames.Shake);
        Hud = new(entry.Runtime, entry.Assets, Magic.GlobalStatus);
    }

    //4083d0 tests active magic before invoking this stage. Keep the subsequent hit pass explicit:
    //a frame effect can start magic DURING actor processing without suppressing that same pass's hits.
    public CombatFrameResult AdvanceActors(uint sampledMilliseconds, bool returnOnNoFriendly)
    {
        now = sampledMilliseconds;
        return Frames.Step(now, returnOnNoFriendly);
    }

    public CombatFrameResult AdvanceActors(uint sampledMilliseconds)
    {
        now = sampledMilliseconds;
        return Frames.Step(now);
    }

    public void AdvanceStatuses(uint sampledMilliseconds) => CombatStatusRuntime.Advance(Runtime, Magic.GlobalStatus, sampledMilliseconds);
    public void AdvanceHits(uint sampledMilliseconds)
    {
        now = sampledMilliseconds;
        hitPass.Run(now);
    }

    public void AdvanceLogicalVisual(uint sampledMilliseconds, bool advanceActorVisuals = true)
    {
        now = sampledMilliseconds;
        Renderer.PrepareActors(VisualDetail, advanceActorVisuals);
        CombatParticlePass pass = Particles.Advance(now, sound, Magic.Transform, collide);
        if (!pass.HadActiveParticles)
        {
            Magic.ReleaseActiveMagic();
        }

        Renderer.AppendParticles(pass.Draws);
        Renderer.DrawWorldAndMenu(pixels);
        Hud.AdvanceVisualState(now, Controller.CommandSelectionActive);
        Hud.Draw(pixels);
        HasCachedFrame = true;
    }

    public void CopyCachedFrame(Span<ushort> destination)
    {
        if (!HasCachedFrame)
        {
            throw new InvalidOperationException("Combat has no completed logical visual frame.");
        }

        if (destination.Length != pixels.Length)
        {
            throw new ArgumentException("Combat target must be640x480.");
        }

        pixels.CopyTo(destination);
    }

    //407fe0 follows world/HUD composition: current-actor MP and detail changes affect the next world draw.
    public void AdvanceGlobalUi(CombatGlobalUiRuntime ui, CombatGlobalUiRaster raster, CombatScanInput input, uint sampledMilliseconds, bool paused)
    {
        if (!HasCachedFrame)
        {
            throw new InvalidOperationException("Global combat UI requires the preceding world/HUD frame.");
        }

        VisualDetail = ui.Advance(input.Query, sampledMilliseconds, VisualDetail, entry.Runtime.Actors[Hud.CurrentActorIndex].State);
        raster.Draw(ui, Controller.AutomationEnabled, paused, pixels);
    }

    public void PlaySound(int id) => entry.Sounds.Play(id, attenuation());
    public void SpawnActorParticle(int actorIndex, int id, uint milliseconds) => Particles.SpawnActor(entry.Runtime.Actors[actorIndex], id, milliseconds, VisualDetail, sound);
    public void ApplyFrameEffect(int actorIndex, int id, int argument, uint milliseconds)
    {
        now = milliseconds;
        Magic.ApplyFrameEffect(actorIndex, id, argument);
    }

    public void ShowConsumedItemNotice(int noticeIndex, uint milliseconds) => itemNotice(noticeIndex, milliseconds);
    public void ShowRankNotice(int rank, uint milliseconds) => rankNotice(rank, milliseconds);
    private void SpawnHitParticle(CombatHitParticle value) => Particles.SpawnPositional(value, now, VisualDetail, sound);
    private void ApplyScreenEffect(uint flags) => Frames.Shake.ApplyFrame(flags);
    private void Collide(CombatParticleState particle) => CombatParticleCollision.Apply(particle, entry.Runtime.Actors, state, magicActive, nextRandom, impacts);
}
