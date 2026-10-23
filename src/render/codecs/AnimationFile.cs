namespace Atria.Render.Codecs;

// 0x436ca0/0x437120 read 28 words and one DWORD per animation frame.
// Unknown words remain accessible; no guessed timing or hitbox interpretation is applied.
public sealed record AnimationFrame(ushort[] Words, uint FinalDword, short StoredOffsetX, short StoredOffsetY)
{
    public ushort SpriteFrameIndex => Words[0];
    public short OffsetX => unchecked((short)Words[1]);
    public short OffsetY => unchecked((short)Words[2]);
}
public sealed record AnimationSequence(ushort Metadata, AnimationFrame[] Frames);
public sealed record AnimationFile(byte[] SpriteReference, AnimationSequence[] Sequences)
{
    public static AnimationFile Decode(ReadOnlySpan<byte> data)
    {
        var reader = new CodecReader(data);
        var header = reader.Take(66);
        if (!header.StartsWith("ANIMATION DATA"u8)) throw new InvalidDataException("Unknown animation signature.");
        var reference = header.Slice(34, 30);
        int terminator = reference.IndexOf((byte)0);
        if (terminator >= 0) reference = reference[..terminator];
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[64..]);
        var sequences = new AnimationSequence[count];
        for (int sequence = 0; sequence < count; sequence++)
        {
            int frameCount = reader.U16();
            ushort metadata = reader.U16();
            var frames = new AnimationFrame[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                var words = new ushort[28];
                for (int w = 0; w < words.Length; w++) words[w] = reader.U16();
                short storedX = unchecked((short)words[1]), storedY = unchecked((short)words[2]);
                if (frame == 0) { words[1] = 0; words[2] = 0; }
                frames[frame] = new(words, reader.U32(), storedX, storedY);
            }
            sequences[sequence] = new(metadata, frames);
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unexpected trailing animation bytes.");
        return new(reference.ToArray(), sequences);
    }
}
