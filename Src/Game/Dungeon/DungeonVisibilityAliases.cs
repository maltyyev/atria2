using System.Buffers.Binary;

namespace Atria.Game.Dungeon;

/// <summary>Bounded semantic mirror of207d0's native out-of-bank reads. No native pointers are dereferenced.</summary>
public static class DungeonVisibilityAliases
{
    public static int Read(
        int index,
        DungeonController controller,
        int returnX,
        int returnY,
        int torchAnimationCounter,
        int retainedTreasureKind,
        int retainedTreasureAmount,
        int retainedTreasureItem,
        int retainedPadding,
        int actionTileFlag,
        int adjustmentCounter,
        ReadOnlySpan<byte> retainedText
    )
    {
        if ((uint)index < 100)
        {
            return controller.Visibility.Values[index];
        }

        if (index >= -26 && index <= -7)
        {
            int offset = index + 106; //visibility begins106 DWORDs after20*5 torch bank.
            var t = controller.Visibility.Torches[offset / 5];
            return (offset % 5) switch
            {
                0 => t.Active ? 1 : 0,
                1 => t.X,
                2 => t.Y,
                3 => t.Cost,
                4 => t.Remaining,
                _ => throw new InvalidDataException()
            };
        }

        if (index is >= 101 and <= 109)
        {
            return controller.Visibility.ScratchNeighbor(index - 101);
        }

        return index switch
        {
            -6 => returnY,
            -5 => returnX,
            -4 => retainedTreasureKind,
            -3 => 0,
            -2 => torchAnimationCounter,
            -1 => controller.ExitPending ? 1 : 0,
            100 => 0, //opaque native SPR heap pointer is never the integer2; retain comparison semantics only.
            110 => 0,
            111 => retainedTreasureAmount,
            112 => retainedTreasureItem,
            113 => controller.TorchCost,
            114 => 0,
            115 => retainedPadding,
            116 => actionTileFlag,
            117 => adjustmentCounter,
            >= 118 and <= 120 => retainedText.Length >= 12 ? BinaryPrimitives.ReadInt32LittleEndian(retainedText.Slice((index - 118) * 4, 4)) : throw new ArgumentException("Adjacent text alias requires12 retained bytes."),
            _ => throw new InvalidDataException("Dungeon darkness index exceeds recovered adjacent bank.")
        };
    }
}
