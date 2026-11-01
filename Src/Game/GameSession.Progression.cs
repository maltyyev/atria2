using Atria.Audio;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private ExperienceAssets? experienceAssets;
    private NativeExperienceModalEffects? experienceModal;
    private PendingExperienceRuntime? pendingExperience;
    // Native59ce0 is BSS zero until408570 selects a battle reward track.
    // It is independent of the current menu/town musicId.
    private int awardMusicTrack = 0;
    private IBufferedSoundEffect? exclusiveScenarioSound;
    private bool exclusiveScenarioSoundOwned;
    private void InitializeProgression()
    {
        if (pendingExperience is not null)
        {
            return;
        }

        var assets = new ExperienceAssets(root, audio, ExperienceAssetContext.Exploration);
        try
        {
            var noticeSound = new SessionExperienceNoticeSound(this, assets.LevelUp);
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
                StopExclusiveScenarioSound,
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

    //42ed90/42ee00 own one WAV.000 channel. Neither music nor AT2WAV UI banks belong to it.
    // Replacing a channel stops/releases its prior sound; an initially null channel is verified BSS state.
    private void ReplaceExclusiveScenarioSound(IBufferedSoundEffect sound, bool owned, int attenuation)
    {
        StopExclusiveScenarioSound();
        exclusiveScenarioSound = sound;
        exclusiveScenarioSoundOwned = owned;
        sound.SetCurrentVolume(attenuation);
        sound.Play();
    }

    private void PlayExclusiveScenarioSound(ReadOnlySpan<byte> resourceName)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "WAV.000"));
        int index = archive.FindResource(resourceName);
        if (index < 0)
        {
            throw new FileNotFoundException("Exclusive scenario WAV resource is missing.");
        }

        var sound = audio.LoadEffect(archive.ReadMember(index));
        try
        {
            ReplaceExclusiveScenarioSound(sound, true, CurrentEffectsVolume);
        }
        catch
        {
            sound.Dispose();
            throw;
        }
    }

    private void StopExclusiveScenarioSound()
    {
        if (exclusiveScenarioSound is not { } sound)
        {
            return;
        }

        sound.StopAll();
        if (exclusiveScenarioSoundOwned)
        {
            sound.Dispose();
        }

        exclusiveScenarioSound = null;
        exclusiveScenarioSoundOwned = false;
    }

    private void DisposeProgression()
    {
        experienceModal?.Dispose();
        StopExclusiveScenarioSound();
        experienceAssets?.Dispose();
        experienceModal = null;
        experienceAssets = null;
        pendingExperience = null;
    }

    private sealed class SessionExperienceNoticeSound(GameSession session, IBufferedSoundEffect sound) : IExperienceNoticeSound
    {
        public void Play(int attenuation) => session.ReplaceExclusiveScenarioSound(sound, false, attenuation);
        public void Stop() => session.StopExclusiveScenarioSound();
    }
}
