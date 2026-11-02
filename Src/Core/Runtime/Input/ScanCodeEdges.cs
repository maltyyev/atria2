namespace Atria.Core.Runtime.Input;

/// <summary>Shared per-scan query latch4048c0. Query only keys actually visited by native priority flow.</summary>
public sealed class ScanCodeEdges
{
    private readonly bool[] consumed = new bool[256];
    public void Reset() => Array.Clear(consumed);
    public void Reset(byte scan) => consumed[scan] = false;
    //4048c0 can poll the physical source twice on the first rising observation.
    public bool Query(Func<byte, bool> currentKeys, byte scan)
    {
        if (!currentKeys(scan))
        {
            consumed[scan] = false;
        }
        else if (!consumed[scan] && currentKeys(scan))
        {
            consumed[scan] = true;
            return true;
        }

        return false;
    }

    public bool Query(ReadOnlySpan<bool> currentKeys, byte scan)
    {
        if (currentKeys.Length != 256)
        {
            throw new ArgumentException("Expected256 scan states.");
        }

        bool down = currentKeys[scan];
        if (!down)
        {
            consumed[scan] = false;
            return false;
        }

        if (consumed[scan])
        {
            return false;
        }

        consumed[scan] = true;
        return true;
    }
}
