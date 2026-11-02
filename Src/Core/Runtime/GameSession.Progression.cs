using Atria.Core.Contracts.Audio;
using Atria.Core.Game;
using Atria.Core.Game.Progression;
using Atria.Core.Game.State;
using Atria.Core.Presentation.Progression;
using Atria.Core.Resources;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    private ExperienceAssets? experienceAssets;
    private NativeExperienceModalEffects? experienceModal;
    private PendingExperienceRuntime? pendingExperience;
    // Native59ce0 is BSS zero until408570 selects a battle reward track.
    // It is independent of the current menu/town musicId.
    private int awardMusicTrack = 0;
    private void InitializeProgression()
    {
        if (pendingExperience is not null)
        {
            return;
        }

        var assets = new ExperienceAssets(root, audio, ExperienceAssetContext.Exploration);
        try
        {
            var noticeSound = new SessionExperienceNoticeSound(exclusiveSound, assets.LevelUp);
            experienceModal = new NativeExperienceModalEffects(
                state,
                host,
                new ExperienceAllocationRaster(assets.Etc, assets.Font, assets.Labels, X87Precision.Bits53),
                assets.Font,
                noticeSound,
                bindings,
                host.IsHeld,
                DrawExperienceBackdrop,
                () => music.Update(awardMusicTrack),
                exclusiveSound.Stop,
                () => CurrentEffectsVolume,
                () => true
            );
            pendingExperience = new PendingExperienceRuntime(items, experienceModal);
            experienceAssets = assets;
        }
        catch
        {
            assets.Dispose();
            throw;
        }
    }

    //4219b0 returns immediately after pending XP. The caller must skip its normal input dispatch.
    private bool ProcessPendingExperience(GameState value)
    {
        if (value.ExperiencePending == 0)
        {
            return false;
        }

        if (!ReferenceEquals(value, state))
        {
            throw new InvalidOperationException("XP belongs to the shared session state.");
        }
        // Reject before allocating assets or consuming input if424d00 has no recovered backdrop here.
        if (value.Mode != GameMode.MainMenu && (value.Mode != GameMode.Town || townAssets is null)
&& (value.Mode is not (GameMode.Interior or GameMode.Inn or GameMode.Shop) || interiorAssets is null) && (value.Mode is not (GameMode.World or GameMode.WorldEntry) || explorationRenderer is null) && (value.Mode != GameMode.Dungeon || dungeonComposer is null))
        {
            throw new NotSupportedException($"XP static backdrop for mode {value.Mode} is not integrated.");
        }

        InitializeProgression();
        return pendingExperience!.ProcessPending(value);
    }

    private void DrawExperienceBackdrop(GameState value, Span<ushort> target)
    {
        if (value.Mode is GameMode.World or GameMode.WorldEntry)
        {
            CopyExplorationStaticBackdrop(target);
            return;
        }

        if (value.Mode == GameMode.Dungeon)
        {
            CopyDungeonStaticBackdrop(target);
            return;
        }

        ReadOnlySpan<ushort> background = value.Mode switch
        {
            200 => menuBackground,
            400 when townAssets is not null => townAssets.Background565,
            410 or 420 or 430 when interiorAssets is not null => interiorAssets.Background565,
            _ => throw new NotSupportedException($"XP static backdrop for mode {value.Mode} is not integrated.")
        };
        if (target.Length != 640 * 480 || background.Length != target.Length)
        {
            throw new InvalidDataException("XP requires a complete640x480 static background.");
        }

        background.CopyTo(target);
    }

    private void DisposeProgression()
    {
        experienceModal?.Dispose();
        exclusiveSound.Stop();
        experienceAssets?.Dispose();
        experienceModal = null;
        experienceAssets = null;
        pendingExperience = null;
    }

    private sealed class SessionExperienceNoticeSound(SessionExclusiveSoundChannel channel, IBufferedSoundEffect sound) : IExperienceNoticeSound
    {
        public void Play(int attenuation) => channel.PlayBorrowed(sound, attenuation);
        public void Stop() => channel.Stop();
    }
}
