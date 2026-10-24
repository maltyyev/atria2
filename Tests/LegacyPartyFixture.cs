using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests;

internal static class LegacyPartyFixture
{
    public static int GetPartyInt(this GameState state, int index, int offset) => BinaryPrimitives.ReadInt32LittleEndian(PartyCodec.EncodeMember(state.Party[index]).AsSpan(offset));
    public static void SetPartyInt(this GameState state, int index, int offset, int value)
    {
        var bytes = PartyCodec.EncodeMember(state.Party[index]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        PartyCodec.DecodeMember(bytes, state.Party[index]);
    }
}
