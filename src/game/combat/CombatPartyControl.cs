using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>404930 controlled actor cycling and404910 visual selection flash.</summary>
public static class CombatPartyControl
{
    //42e3b0 initializes native4cc3b0 to RGB565ffff.
    public static int Cycle(IReadOnlyList<CombatActorImage> actors, int partyStart, int currentIndex, ushort flashColor = 0xffff)
    {
        if (actors.Count - partyStart != 6 || currentIndex < partyStart || currentIndex >= actors.Count)
            throw new ArgumentException("Combat control requires the actual six-slot party tail.");
        CombatActorImage current = actors[currentIndex]; byte[] source = current.Fields;
        if (I(source, 0x230) != 1)
        {
            for (int i = partyStart; i < actors.Count; i++) W(actors[i].Fields, 0x230, 0);
            W(source, 0x230, 1); return currentIndex;
        }
        byte control = source[0xa4]; int player = current.ControlPlayer;
        if (control == 0 || player < 0) throw new InvalidOperationException("Selected combat actor has no actual control binding.");
        W(source, 0x230, 0);
        int first = partyStart;
        while (first < actors.Count && actors[first].Fields[0xa4] != control) first++;
        current.ControlPlayer = -1; source[0xa4] = 0;
        int selected = first + 1;
        while (selected < actors.Count && !(I(actors[selected].Fields, 0x224) != 0 && actors[selected].Fields[0xa4] == 0 && I(actors[selected].Fields, 0x170) > 0)) selected++;
        if (selected >= actors.Count)
        {
            selected = partyStart;
            while (selected < actors.Count && !(I(actors[selected].Fields, 0x224) != 0 && actors[selected].Fields[0xa4] == 0)) selected++;
        }
        if (selected == actors.Count) throw new InvalidDataException("Native combat control cycling has no bounded active destination.");
        CombatActorImage destination = actors[selected];
        destination.Fields[0xa4] = control; destination.ControlPlayer = player;
        W(destination.Fields, 0x230, 1); Flash(destination.Fields, flashColor);
        return selected;
    }

    public static void Flash(Span<byte> actor, ushort color)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Invalid combat actor image.");
        BinaryPrimitives.WriteInt32LittleEndian(actor.Slice(0x23c, 4), 60);
        BinaryPrimitives.WriteUInt32LittleEndian(actor.Slice(0x294, 4), color);
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
}
