using System.Runtime.ExceptionServices;
using Atria.Core.Contracts.Audio;
using Atria.Core.Resources;

namespace Atria.Core.Runtime.Audio;

/// <summary>Owns the session's single WAV.000 sound slot while respecting borrowed notice sounds.</summary>
internal sealed class SessionExclusiveSoundChannel(string root, IGameAudio audio)
{
    private IBufferedSoundEffect? current;
    private bool ownsCurrent;

    /// <summary>Loads and plays one named WAV.000 resource on the exclusive channel.</summary>
    public void PlayMember(ReadOnlySpan<byte> member, int attenuation)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "WAV.000"));
        int index = archive.FindResource(member);
        if (index < 0)
        {
            throw new FileNotFoundException("Exclusive scenario WAV resource is missing.");
        }

        PlayWave(archive.ReadMember(index), attenuation);
    }

    /// <summary>Loads and takes ownership of an arbitrary WAV payload for exclusive playback.</summary>
    public void PlayWave(ReadOnlyMemory<byte> wave, int attenuation)
    {
        Replace(audio.LoadEffect(wave), owned: true, attenuation);
    }

    /// <summary>Plays a sound owned by another session resource without taking disposal ownership.</summary>
    public void PlayBorrowed(IBufferedSoundEffect sound, int attenuation) => Replace(sound, owned: false, attenuation);

    /// <summary>Stops the active sound and disposes it only when the channel loaded it.</summary>
    public void Stop()
    {
        if (current is not { } sound)
        {
            return;
        }

        bool owned = ownsCurrent;
        current = null;
        ownsCurrent = false;

        Exception? stopFailure = null;
        try
        {
            sound.StopAll();
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        if (owned)
        {
            try
            {
                sound.Dispose();
            }
            catch when (stopFailure is not null)
            {
                ExceptionDispatchInfo.Capture(stopFailure).Throw();
            }
        }

        if (stopFailure is not null)
        {
            ExceptionDispatchInfo.Capture(stopFailure).Throw();
        }

    }

    private void Replace(IBufferedSoundEffect sound, bool owned, int attenuation)
    {
        try
        {
            Stop();
        }
        catch
        {
            if (owned)
            {
                try
                {
                    sound.Dispose();
                }
                catch
                {
                    // Preserve the failure that prevented replacing the previous sound.
                }
            }

            throw;
        }

        current = sound;
        ownsCurrent = owned;
        try
        {
            sound.SetCurrentVolume(attenuation);
            sound.Play();
        }
        catch
        {
            current = null;
            ownsCurrent = false;
            try
            {
                sound.StopAll();
            }
            catch
            {
                // Keep the setup/playback exception as the channel operation's failure.
            }

            if (owned)
            {
                try
                {
                    sound.Dispose();
                }
                catch
                {
                    // Keep the setup/playback exception as the channel operation's failure.
                }
            }

            throw;
        }
    }
}
