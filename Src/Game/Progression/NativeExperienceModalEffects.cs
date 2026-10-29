using System.Text;
using Atria.Audio;
using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Progression;

public enum ExperienceAssetContext
{
    Exploration,
    Battle
}

/// <summary>424d00(0) receives45ae24: allocation redraw deadline or frozen level-notice deadline.</summary>
public delegate void ExperienceBattleBackdrop(GameState state, Span<ushort> target, uint nativeCombatMilliseconds);
/// <summary>424d40 retains the caller's active sprite archive; LEVELUP explicitly uses WAV.000.</summary>
public interface IExperienceNoticeSound
{
    void Play(int attenuation);
    void Stop();
}

public sealed class ExperienceAssets : IDisposable, IExperienceNoticeSound
{
    public SpriteFile Etc
    {
        get;
    }
    public SpriteFile? Player
    {
        get;
    }
    public BitmapFont Font
    {
        get;
    }
    public IReadOnlyList<ReadOnlyMemory<byte>> Labels
    {
        get;
    }
    public SoundEffectBank LevelUp
    {
        get;
    }

    public ExperienceAssets(string resourceRoot, AudioDevice audio, ExperienceAssetContext context = ExperienceAssetContext.Exploration)
    {
        string spriteArchive = context switch
        {
            ExperienceAssetContext.Exploration => "AT2SPR.000",
            ExperienceAssetContext.Battle => "SPR.000",
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        using var sprites = BinderArchive.Open(Path.Combine(resourceRoot, spriteArchive));
        using var waves = BinderArchive.Open(Path.Combine(resourceRoot, "WAV.000"));
        Etc = SpriteFile.Decode(Read(sprites, "ETC"));
        // Native424d40 ignores the loader return; exploration archive has no PLAYER member.
        // Keep that verified optional load explicit rather than substituting another archive's art.
        int player = sprites.FindResource("PLAYER"u8);
        Player = player < 0 ? null : SpriteFile.Decode(sprites.ReadMember(player));
        Font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(resourceRoot, "ENG1.FNT")), true);
        Labels = GameText.Messages(LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(resourceRoot, "BATTLE.DAT"))), "battle");
        LevelUp = audio.LoadEffect(Read(waves, "LEVELUP"));
    }

    private static byte[] Read(BinderArchive archive, string name)
    {
        int index = archive.FindResource(Encoding.ASCII.GetBytes(name));
        if (index < 0)
        {
            throw new FileNotFoundException("Required XP asset is missing.", name);
        }

        return archive.ReadMember(index);
    }

    public void Dispose() => LevelUp.Dispose();
    public void Play(int attenuation)
    {
        LevelUp.StopAll();
        LevelUp.SetCurrentVolume(attenuation);
        LevelUp.Play();
    }

    public void Stop() => LevelUp.StopAll();
}

/// <summary>Real native XP modal over an existing shared game window/GPU/input/audio session.</summary>
public sealed class NativeExperienceModalEffects : IExperienceModalEffects, IDisposable
{
    private readonly GameState state;
    private readonly IDialogueFrameHost host;
    private readonly BitmapFont font;
    private readonly IExperienceNoticeSound noticeSound;
    private readonly ExperienceAllocationRaster raster;
    private readonly ControlBindings bindings;
    private readonly Func<byte, bool> held;
    private readonly DialogueSceneDraw background;
    private readonly ExperienceBattleBackdrop? battleBackground;
    private readonly Action updateAwardMusic, stopExclusiveSound;
    private readonly Func<int> attenuation;
    private readonly Func<bool> soundEnabled;
    private readonly Func<ExperienceInput, bool> query;
    private ExperienceAllocationSession? current;
    private bool disposed;
    private uint pausedMilliseconds;
    public uint Milliseconds => host.Gate.LastFrameMilliseconds;

    private const int MaximumTicksPerBatch = 8;
    // Default background is424d00 variant1's STATIC copy. Optional battleBackground replaces it
    // with424d00(0)'s stateful world draw using the supplied45ae24 deadline. updateAwardMusic uses59ce0,
    // not current town track. held reads the shared current scan state; edge queries use host's bank.
    public NativeExperienceModalEffects(
        GameState state,
        IDialogueFrameHost host,
        ExperienceAssets assets,
        ControlBindings bindings,
        Func<byte, bool> held,
        DialogueSceneDraw background,
        Action updateAwardMusic,
        Action stopExclusiveSound,
        Func<int> attenuation,
        Func<bool> soundEnabled,
        X87Precision precision,
        ExperienceBattleBackdrop? battleBackground = null
    ) : this(
    state,
    host,
    new ExperienceAllocationRaster(assets.Etc, assets.Font, assets.Labels, precision),
    assets.Font,
    assets,
    bindings,
    held,
    background,
    updateAwardMusic,
    stopExclusiveSound,
    attenuation,
    soundEnabled,
    battleBackground
)
    {
    }

    public NativeExperienceModalEffects(
        GameState state,
        IDialogueFrameHost host,
        ExperienceAllocationRaster raster,
        BitmapFont font,
        IExperienceNoticeSound noticeSound,
        ControlBindings bindings,
        Func<byte, bool> held,
        DialogueSceneDraw background,
        Action updateAwardMusic,
        Action stopExclusiveSound,
        Func<int> attenuation,
        Func<bool> soundEnabled,
        ExperienceBattleBackdrop? battleBackground = null
    )
    {
        this.state = state;
        this.host = host;
        this.raster = raster;
        this.font = font;
        this.noticeSound = noticeSound;
        this.bindings = bindings;
        this.held = held;
        this.battleBackground = battleBackground;
        this.background = background;
        this.updateAwardMusic = updateAwardMusic;
        this.stopExclusiveSound = stopExclusiveSound;
        this.attenuation = attenuation;
        this.soundEnabled = soundEnabled;
        query = Query;
    }

    public void Allocate(ExperienceAllocationSession session)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (current is not null)
        {
            throw new InvalidOperationException("XP modal is already active.");
        }

        current = session;
        PumpFocused();
        session.TakeRedraw(Milliseconds);
        Draw(session.Party, null, session.NativeRedrawDeadline);
        while (true)
        {
            PumpFocused();
            int ticks = 0;
            while (ticks < MaximumTicksPerBatch && TryLogicalTick())
            {
                ticks++;
                updateAwardMusic();
                if (session.TakeRedraw(Milliseconds))
                {
                    Draw(session.Party, null, session.NativeRedrawDeadline);
                }

                if (session.Finished)
                {
                    host.Present();
                    return;
                }

                session.ApplyInput(query);
            }

            if (host.Gate.IsDue)
            {
                continue;
            }

            host.Present(); // Cached pixels only; GPU presents never transfer XP or advance input.
            host.Gate.Clock.Yield();
        }
    }

    public void ShowLevelUp(LevelUpNotice notice, IReadOnlyList<PartyMember> awardedParty)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (current is null || !current.Finished)
        {
            throw new InvalidOperationException("Level notice requires completed XP allocation.");
        }

        if (soundEnabled())
        {
            stopExclusiveSound();
            noticeSound.Play(attenuation());
        }

        uint observedPause = pausedMilliseconds;
        do
        {
            PumpFocused();
            int ticks = 0;
            ApplyFocusPause(notice, ref observedPause);
            while (!notice.Completed && ticks < MaximumTicksPerBatch && TryLogicalTick())
            {
                ticks++;
                ApplyFocusPause(notice, ref observedPause);
                Draw(awardedParty, notice.Frame, notice.Deadline);
                notice.AfterRenderedFrame(Milliseconds, held(bindings.Confirm));
            }

            ApplyFocusPause(notice, ref observedPause);
            if (!notice.Completed && host.Gate.IsDue)
            {
                continue;
            }

            host.Present();
            if (!notice.Completed)
            {
                host.Gate.Clock.Yield();
            }
        }
        while (!notice.Completed);
    }

    public void Finish(AwardFinishRelease release)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (current is null)
        {
            throw new InvalidOperationException("No native XP modal is active.");
        }

        PumpFocused();
        while (held(bindings.Cancel))
        {
            PumpFocused();
            int ticks = 0;
            while (held(bindings.Cancel) && ticks < MaximumTicksPerBatch && TryLogicalTick())
            {
                ticks++;
                updateAwardMusic();
            }

            if (held(bindings.Cancel) && !host.Gate.IsDue)
            {
                host.Gate.Clock.Yield();
            }
        }

        release.Observe(false);
        noticeSound.Stop();
        current = null;
    }

    private bool Query(ExperienceInput input) => input switch
    {
        ExperienceInput.CycleMode => host.QueryScan(bindings.Confirm),
        ExperienceInput.PreviousMember => host.QueryScan(bindings.Up),
        ExperienceInput.NextMember => host.QueryScan(bindings.Down),
        ExperienceInput.GiveFromSelectedHeld => held(bindings.Previous),
        ExperienceInput.TakeFromOthersHeld => held(bindings.Next),
        _ => host.QueryScan(bindings.Cancel)
    };
    private void Draw(IReadOnlyList<PartyMember> party, LevelUpNoticeFrame? notice, uint nativeCombatMilliseconds)
    {
        ExperienceAllocationSession session = current ?? throw new InvalidOperationException("No XP scene.");
        if (battleBackground is { } drawBattle)
        {
            drawBattle(state, host.FrameBuffer, nativeCombatMilliseconds);
        }
        else
        {
            background(state, host.FrameBuffer);
        }

        raster.Draw(party, session.Allocation, notice?.Actor ?? session.SelectedMember, session.Mode, state.Globals[80], host.FrameBuffer, heroCatalog: state.HeroCatalog);
        if (notice is LevelUpNoticeFrame frame)
        {
            ProgressionNoticeRaster.Draw(frame, font, host.FrameBuffer);
        }
    }

    private bool TryLogicalTick()
    {
        PumpFocused();
        if (!host.Gate.IsDue)
        {
            return false;
        }

        host.Gate.CommitScheduled();
        host.BeginLogicalFrame(Milliseconds);
        return true;
    }

    private void PumpFocused()
    {
        host.Pump();
        bool paused = !host.Focused;
        uint before = host.Gate.Clock.Milliseconds;
        while (!host.Closed && !host.Focused)
        {
            host.Gate.Clock.Yield();
            host.Pump();
        }

        if (host.Closed)
        {
            throw new OperationCanceledException("The game window closed during XP allocation.");
        }

        if (paused)
        {
            pausedMilliseconds = unchecked(pausedMilliseconds + host.Gate.Clock.Milliseconds - before);
            host.Gate.Resume();
        }
    }

    private void ApplyFocusPause(LevelUpNotice notice, ref uint observed)
    {
        notice.PostponeForFocusPause(unchecked(pausedMilliseconds - observed));
        observed = pausedMilliseconds;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        noticeSound.Stop();
        current = null;
        disposed = true;
    }
}
