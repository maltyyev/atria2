using System.Buffers.Binary;

namespace Atria.Resources;

/// <summary>Two blocks of eight DirectInput scan codes, read by 0x413750.</summary>
public sealed class ControlBindings
{
    private readonly byte[] scanCodes;
    private ControlBindings(byte[] scanCodes) => this.scanCodes = scanCodes;
    public byte this[int player, int action] => (uint)player < 2 && (uint)action < 8
        ? scanCodes[player * 8 + action] : throw new ArgumentOutOfRangeException();
    public byte Previous => this[0, 0];
    public byte Next => this[0, 1];
    public byte Up => this[0, 2];
    public byte Down => this[0, 3];
    public byte Confirm => this[0, 4];
    public byte Cancel => this[0, 5];

    public static ControlBindings Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 64) throw new InvalidDataException("BATTLE.CFG must contain two eight-code control blocks.");
        byte[] result = new byte[16];
        for (int i = 0; i < result.Length; i++)
        {
            uint code = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * 4, 4));
            if (code > 255) throw new InvalidDataException("Control scan code is outside the native keyboard state.");
            result[i] = (byte)code;
        }
        return new(result);
    }
}
