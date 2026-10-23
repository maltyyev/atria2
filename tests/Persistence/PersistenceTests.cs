using System.Buffers.Binary;
using Atria.Game.Persistence;

namespace Atria.Tests.Persistence;

public static class PersistenceTests
{
    public static string Run()
    {
        // Synthetic byte fixture uses independently calculated native write-call offsets.
        // It proves storage contracts and corruption handling, never original interoperability.
        var data = new byte[288671];
        for (int i = 100; i < data.Length; i++) data[i] = unchecked((byte)(i * 37 + 11));
        "ATRIA-II SAVE FILE (c)Jamie system development\0"u8.CopyTo(data);
        void Put(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at, 4), value);
        void Check(bool result, string text) { if (!result) throw new InvalidOperationException(text); }
        Put(233, 300); Put(237, 2); Put(241, 4); Put(245, 6);
        Put(8543, unchecked((int)0x12345687)); // native 0x12345678 XOR 0xff
        Put(8539, 6); Put(288655, 1450); Put(288659, 3); Put(288663, 14);
        Put(274291, -42); Put(278291 + 199 * 4, 161);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(281093, 2), -32768);
        var image = LegacySaveImage.Parse(data);
        var state = image.ReadRecoveredState();
        Check(state.Mode == 300 && state.WorldIndex == 2 && state.WorldX == 4 && state.WorldY == 6, "Native mode/world offsets.");
        Check(state.Money == 0x12345678 && state.InitializedPartyCount == 6, "Low-byte XOR and adjacent packed integer.");
        Check(state.Year == 1450 && state.Month == 3 && state.Day == 14, "EOF slot-summary date offsets.");
        Check(state.Events[0] == -42 && state.EventStatuses[199] == 161 && state.Globals[999] == -32768, "Bank endpoints and signed fields.");
        for (int i = 0; i < 8; i++)
        {
            int start = 8555 + i * 33217;
            Check(image.GetAreaInline(i).AsSpan().SequenceEqual(data.AsSpan(start, 417)), "Area inline stride.");
            Check(image.GetAreaPayload(i).AsSpan().SequenceEqual(data.AsSpan(start + 417, 32800)), "Area payload stride; pointer excluded.");
        }
        var altered = image.WithRecoveredState(state);
        Check(altered.Encode().AsSpan().SequenceEqual(data), "Opaque bytes must survive an unchanged projection.");
        state.Money = 200;
        byte[] output = image.WithRecoveredState(state).Encode();
        Check(output[8543] == 0x37 && output[8544] == 0 && output[8545] == 0 && output[8546] == 0, "Known native money200 disk bytes.");
        Check(output[361] == data[361], "Even28000 inversion leaves first party byte unchanged.");
        for (int i = 0; i < data.Length; i++) if (i < 8543 || i > 8546) Check(output[i] == data[i], "Money edit changed unrelated bytes.");
        data[237] ^= 0xff;
        Check(image.ReadRecoveredState().WorldIndex == 2, "Input ownership.");
        output[237] ^= 0xff;
        Check(image.ReadRecoveredState().WorldIndex == 2, "Output ownership.");
        ExpectInvalid(() => LegacySaveImage.Parse(new byte[288670]));
        ExpectInvalid(() => LegacySaveImage.Parse(new byte[288672]));
        var corrupt = image.Encode(); corrupt[0] = 0;
        ExpectInvalid(() => LegacySaveImage.Parse(corrupt));
        Check(state.Money == 200, "Failed parse must not mutate existing state.");
        try { image.GetAreaInline(8); throw new InvalidOperationException("Area index accepted."); }
        catch (ArgumentOutOfRangeException) { }
        return "Legacy persistence static offsets, payload ownership, low-byte XOR, preservation and invalid-input checks passed; authentic fixture parity pending.";
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid legacy image accepted.");
    }
}
