namespace Atria.Audio;

/// <summary>A bounded PCM byte range inside a source file.</summary>
public readonly record struct PcmChunk(long Offset, int Length);

/// <summary>Integer PCM layout shared by AVI parsing and audio backends.</summary>
public readonly record struct PcmAudioFormat(ushort Channels, uint SampleRate, ushort BitsPerSample)
{
    private const int MaximumChannelCount = 64;

    public ushort BlockAlign => checked((ushort)(Channels * BitsPerSample / 8));
    public uint ByteRate => checked(SampleRate * BlockAlign);

    public void Validate()
    {
        if (Channels == 0 || Channels > MaximumChannelCount || SampleRate == 0 || BitsPerSample is not (8 or 16 or 24 or 32))
        {
            throw new InvalidDataException("Invalid integer PCM format.");
        }

        _ = BlockAlign;
        _ = ByteRate;
    }
}
