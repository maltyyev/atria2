namespace Atria.Game.Persistence.Compatibility;

/// <summary>Read-only version-one layout; it does not drive current screen navigation.</summary>
public sealed record LegacyModeStackSnapshot(int Depth, int Current, int[] Slots)
{
    private const int SlotCount = 11;
    private const int SavedWorldOffset = 3;

    public void Validate()
    {
        if (Slots is not { Length: SlotCount } || Depth < -1 || Depth >= SlotCount)
            throw new InvalidDataException("Invalid historical mode stack.");
    }

    public int UnderlyingSavedMode()
    {
        Validate();
        int index = Depth - SavedWorldOffset;
        if ((uint)index >= SlotCount)
            throw new InvalidDataException("Historical save stack lacks its world slot.");

        return Slots[index];
    }
}
