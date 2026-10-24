using System.Globalization;
using System.Text;
using Atria.Resources;

namespace Atria.Audio;

public interface IBufferedSoundEffect : IDisposable
{
    void SetCurrentVolume(int hundredthsDb);
    void Play();
    void StopAll();
}

/// <summary>42ee40/42ee60/42ef40: battle-local numeric WAV cache; exclusive channel belongs to the session.</summary>
public sealed class CombatSoundBank : IDisposable
{
    public const int Capacity = 200;
    private const int PreloadedSoundCount = 50;
    private readonly IBufferedSoundEffect?[] sounds = new IBufferedSoundEffect?[Capacity];
    private readonly BinderArchive archive;
    private readonly Func<ReadOnlyMemory<byte>, IBufferedSoundEffect> load;
    private readonly Action<ReadOnlyMemory<byte>> playExclusiveWave;
    private bool disposed;
    public int LoadedCount
    {
        get; private set;
    }

    public CombatSoundBank(string resourceRoot, Func<ReadOnlyMemory<byte>, IBufferedSoundEffect> load, Action<ReadOnlyMemory<byte>> playExclusiveWave)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(playExclusiveWave);
        this.load = load;
        this.playExclusiveWave = playExclusiveWave;
        archive = BinderArchive.Open(Path.Combine(resourceRoot, "WAV.000"));
        try
        {
            for (int id = 0; id < PreloadedSoundCount; id++)
            {
                Load(id);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // INF X records use this same cache after preloaded sounds. Missing numeric members are optional.
    public bool Load(int id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // Native 42ee60 used an inclusive upper bound for a fixed-size array. Reject that unsafe boundary.
        if ((uint)id >= Capacity)
        {
            throw new InvalidDataException($"Battle sound cache index exceeds its {Capacity} slots.");
        }

        if (sounds[id] is not null)
        {
            return true;
        }

        byte[] name = Encoding.ASCII.GetBytes(id.ToString("D3", CultureInfo.InvariantCulture) + ".wav");
        int index = archive.FindResource(name);
        if (index < 0)
        {
            return false;
        }

        sounds[id] = load(archive.ReadMember(index));
        LoadedCount++;
        return true;
    }

    public void Play(int id, int attenuation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (id < 0)
        {
            throw new InvalidDataException("Negative battle sound index.");
        }

        if (id == 0)
        {
            return;
        }

        if (id >= Capacity)
        {
            string name = id.ToString(CultureInfo.InvariantCulture) + ".wav";
            int index = archive.FindResource(Encoding.ASCII.GetBytes(name));
            if (index < 0)
            {
                throw new FileNotFoundException($"Exclusive battle WAV resource is missing: {name}");
            }
            // The session callback consumes RIFF bytes, never the ASCII resource name.
            playExclusiveWave(archive.ReadMember(index));
            return;
        }

        if (sounds[id] is not { } sound)
        {
            return;
        }

        sound.SetCurrentVolume(attenuation);
        sound.Play();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            for (int i = 0; i < sounds.Length; i++)
            {
                if (sounds[i] is not { } sound)
                {
                    continue;
                }
                //42ef00 mistakenly stops the exclusive pointer while freeing each numeric bank.
                // Stop this bank before destroying its voices and shared sample storage.
                try
                {
                    sound.StopAll();
                }
                finally
                {
                    sound.Dispose();
                    sounds[i] = null;
                }
            }
        }
        finally
        {
            archive.Dispose();
        }
    }
}
