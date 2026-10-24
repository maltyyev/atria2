using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Resources;

internal static class StateModelTests
{
    public static string Run()
    {
        var random = new Random(0x415920);
        for (int trial = 0; trial < 32; trial++)
        {
            var actorImage = new byte[CombatActorCodec.ImageSize];
            random.NextBytes(actorImage);
            var actor = CombatActorCodec.Decode(actorImage);
            Check(CombatActorCodec.Encode(actor).AsSpan().SequenceEqual(actorImage), "Actor codec must retain every original byte.");
            actor.Health = int.MinValue;
            actor.Equipment[5] = -1;
            actor.StatusBank[2].Deadline = int.MaxValue;
            BinaryPrimitives.WriteInt32LittleEndian(actorImage.AsSpan(0x170), int.MinValue);
            BinaryPrimitives.WriteInt32LittleEndian(actorImage.AsSpan(0x21c), -1);
            BinaryPrimitives.WriteInt32LittleEndian(actorImage.AsSpan(0x274), int.MaxValue);
            Check(CombatActorCodec.Encode(actor).AsSpan().SequenceEqual(actorImage), "Named actor writes must change only established fields.");

            var partyImage = new byte[GameState.PartyCount * PartyCodec.RecordSize];
            random.NextBytes(partyImage);
            var party = new GameState();
            PartyCodec.Decode(partyImage, party.Party);
            Check(PartyCodec.Encode(party.Party).AsSpan().SequenceEqual(partyImage), "Party codec must retain original names, reserved bytes and signed values.");
            var copy = new GameState();
            PartyCodec.Copy(party.Party, copy.Party);
            copy.Party[0].Health ^= int.MinValue;
            copy.Party[0].Equipment[0] ^= int.MinValue;
            copy.Party[0].Name[0] ^= byte.MaxValue;
            Check(PartyCodec.Encode(party.Party).AsSpan().SequenceEqual(partyImage), "Transactional party copies must not share mutable arrays.");
            party.Party[9].Experience = int.MaxValue;
            BinaryPrimitives.WriteInt32LittleEndian(partyImage.AsSpan(9 * 280 + 192), int.MaxValue);
            Check(PartyCodec.Encode(party.Party).AsSpan().SequenceEqual(partyImage), "Experience changes must not rewrite unrelated save bytes.");
        }

        Reject(() => CombatActorCodec.Decode(new byte[CombatActorCodec.ImageSize - 1]));
        Reject(() => PartyCodec.Decode(new byte[PartyCodec.RecordSize], new GameState().Party));
        Reject(() => CombatStatusCodec.Decode(new byte[CombatStatusCodec.RecordSize - 1], new CombatStatusSlot[1]));
        return "Managed state models: 32 full-image parity cases, isolated field writes, transactional copy ownership and truncated input rejection passed.";
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed model image was accepted.");
    }

    private static void Check(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }
}
