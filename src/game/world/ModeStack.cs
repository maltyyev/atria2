namespace Atria.Game.World;

public readonly record struct ModeTransition(int Mode, int Depth, bool RestoreFlag);

/// <summary>429260/429290 mode storage. Bounds are native-memory safety guards, not new game rules.</summary>
public sealed partial class ModeStack
{
    // 46fd20..46fd48 precede unrelated field46fd4c. Do not allow writes into other state.
    public const int SafeCapacity = 11;
    private readonly int[] modes = new int[SafeCapacity];
    public int Depth { get; private set; }
    public int Current { get; private set; } = 200;
    public ModeStack() { modes[0] = 200; }
    public ModeTransition Push(int mode)
    {
        int next = checked(Depth + 1);
        if ((uint)next >= SafeCapacity) throw new InvalidOperationException("Mode stack exceeds recovered storage boundary.");
        modes[next] = mode; Depth = next; Current = mode;
        return new(mode, Depth, false);
    }
    public void ResetDepth() => Depth=-1;
    public ModeTransition StartNewGame()
    {
        // 425990 writes depth=-1, then429260(400). Existing other slots are not cleared.
        Depth = -1; return Push(400);
    }
    public bool TryPop(out ModeTransition transition)
    {
        if (Depth < 1) { transition = default; return false; }
        int mode = modes[Depth - 1];
        int next = Depth - 1;
        bool flag = mode == 320;
        if (flag)
        {
            if (Depth < 2) throw new InvalidDataException("Mode320 return would read outside recovered stack.");
            mode = modes[Depth - 2]; next = Depth - 2;
        }
        else if (mode == 300) next = Depth - 2;
        Depth = next; Current = mode;
        transition = new(mode, Depth, flag); return true;
    }
}
