namespace Atria.Game.World;

public sealed record ModeStackSnapshot(int Depth, int Current, int[] Slots);
public sealed partial class ModeStack
{
    public ModeStackSnapshot CaptureRuntime() => new(Depth, Current, (int[])modes.Clone());
    public void RestoreRuntime(ModeStackSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Slots is null || value.Slots.Length != SafeCapacity || value.Depth < -1 || value.Depth >= SafeCapacity)
        {
            throw new InvalidDataException("Invalid retained mode stack.");
        }

        value.Slots.CopyTo(modes, 0);
        Depth = value.Depth;
        Current = value.Current;
    }

    // Native writer reads46fd14 + depth*4, three DWORDs before the mode bank.
    public int LegacySaveMode()
    {
        int index = Depth - 3;
        if ((uint)index >= SafeCapacity)
        {
            throw new InvalidDataException("Save stack does not contain the recovered underlying mode slot.");
        }

        return modes[index];
    }
}
