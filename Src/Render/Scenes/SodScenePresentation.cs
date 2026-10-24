using System.Buffers.Binary;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Render.Scenes;

public static class SodScenePresentation
{
    // Diskrecord begins at120; fields map directly to runtime0x48fd30.. in0x41d810.
    public static (int X, int Y) GetSelectionAnchor(SodV2 scene, SpriteFile sprites, int objectIndex)
    {
        SodObject item = scene.GetObject(objectIndex);
        SpriteFrame frame = GetFrame(item, sprites);
        int x = checked(item.GetInteger(0) + (frame.Width >> 1));
        int y = checked(item.GetInteger(1) + (frame.Height >> 1) - 32);
        return (x, y);
    }

    public static int SelectableCount(SodV2 scene)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(scene.Header.Span[73..]);
        if (count < 0 || count > scene.Count)
        {
            throw new InvalidDataException("SOD selectable count exceeds object count.");
        }

        return count;
    }

    // Only the selected object is an overlay in town mode0x41d810; background already contains buildings.
    public static void DrawSelected(SodV2 scene, SpriteFile sprites, int selectedObject, Span<ushort> target, int width, int height, int stride, RasterClip? clip = null)
    {
        SodObject item = scene.GetObject(selectedObject);
        SpriteRaster.Draw565(GetFrame(item, sprites), target, width, height, stride, item.GetInteger(0), item.GetInteger(1), SpriteBlend.HalfType3, clip: clip);
    }

    // Call after town effects, only when the caller's dialogue/modal gates permit the cursor.
    public static void DrawSelectionLabel(
        SodV2 scene,
        SpriteFile sprites,
        SpriteFile etc,
        BitmapFont smallFont,
        int selectedObject,
        int motionX,
        int motionY,
        Span<ushort> target,
        int width,
        int height,
        int stride,
        RasterClip? clip = null
    )
    {
        if (smallFont.Width != 8 || smallFont.Height != 16 || smallFont.Advance != 8)
        {
            throw new ArgumentException("Town labels require ENG1.");
        }

        if (etc.Frames.Length <= 13)
        {
            throw new InvalidDataException("ETC cursor frame is absent.");
        }

        var anchor = GetSelectionAnchor(scene, sprites, selectedObject);
        int x = checked(anchor.X + motionX), y = checked(anchor.Y + motionY);
        SpriteRaster.Draw565(etc.Frames[13], target, width, height, stride, x, y, SpriteBlend.Copy, clip: clip);
        ReadOnlySpan<byte> label = scene.GetObject(selectedObject).GetString(4).Span;
        int length = label.Length * 8;
        int textX = checked(x + (anchor.X + 32 + length < 631 ? 32 : -length));
        smallFont.DrawText(label, target, width, height, stride, checked(textX + 1), checked(y - 1), 0, clip);
        smallFont.DrawText(label, target, width, height, stride, textX, checked(y - 2), 0xFFE0, clip);
    }

    // Reproduce0x425b80's adjacent-swap rule, including the one-pixel tolerance and repeated full passes.
    // Call only for scene modes that actually consume this order; town selection cycles direct indices.
    public static int[] BuildDepthOrder(SodV2 scene, SpriteFile sprites)
    {
        int[] order = Enumerable.Range(0, scene.Count).ToArray();
        int[] bottoms = new int[scene.Count];
        for (int i = 0; i < scene.Count; i++)
        {
            SodObject item = scene.GetObject(i);
            bottoms[i] = unchecked(item.GetInteger(1) + GetFrame(item, sprites).Height);
        }

        for (int pass = 0; pass < order.Length; pass++)
        {
            bool changed = false;
            for (int i = 0; i < order.Length - 1; i++)
            {
                if (bottoms[order[i + 1]] < unchecked(bottoms[order[i]] - 1))
                {
                    (order[i], order[i + 1]) = (order[i + 1], order[i]);
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return order;
    }

    public static SpriteFrame GetFrame(SodObject item, SpriteFile sprites)
    {
        int index = item.GetInteger(2);
        if ((uint)index >= (uint)sprites.Frames.Length)
        {
            throw new InvalidDataException("SOD refers to an absent sprite frame.");
        }

        return sprites.Frames[index];
    }
}
