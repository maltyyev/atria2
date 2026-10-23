using System.Buffers.Binary;
using System.Buffers.Text;
using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Progression;

/// <summary>4247b0,426a50 and409a30. Input bytes remain CP1251 glyph IDs; no Unicode reflow.</summary>
public sealed class ExperienceAllocationRaster(SpriteFile etc, BitmapFont smallFont,
    IReadOnlyList<ReadOnlyMemory<byte>> battleLabels, X87Precision precision)
{
    public void Draw(ReadOnlySpan<byte> party, ReadOnlySpan<int> allocation, int selectedActor, int mode,
        short extraReward, Span<ushort> target, int width = 640, int height = 480, int stride = 640)
    {
        if (party.Length < 6 * 280 || allocation.Length != 6 || battleLabels.Count < 50 || smallFont.Width != 8 || smallFont.Height != 16)
            throw new ArgumentException("Incomplete native XP UI assets/state.");
        if (width < 0 || height < 0 || stride < width || (long)stride * height > target.Length) throw new ArgumentException("Invalid XP raster.");
        UiRaster.TintHalf(target, width, height, stride, 124, 114, 452, 196, 0);
        UiRaster.DrawPanel(etc, 110, 100, 13, 5, target, width, height, stride);
        Span<byte> text = stackalloc byte[512];
        for (int actor = 0; actor < 6; actor++)
        {
            ReadOnlySpan<byte> record = party.Slice(actor * 280, 280);
            if (Read(record, 60) == 0) continue;
            int row = 141 + actor * 22;
            if (actor == selectedActor) Fill(target, width, height, stride, 136, row - 14, 204, row + 4, 0x000f);
            smallFont.DrawText(battleLabels[24 + actor].Span, target, width, height, stride, 139, row - 12, NativeUiPalette.Normal[4]);
            int xp = Read(record, 192), next = Read(record, 196), level = Read(record, 200), length = 0;
            Append(xp, text, ref length); text[length++] = (byte)'+'; Append(allocation[actor], text, ref length);
            text[length++] = (byte)'/'; Append(next, text, ref length);
            smallFont.DrawText(text[..length], target, width, height, stride, 419, row - 12, NativeUiPalette.Normal[4]);
            "Lv "u8.CopyTo(text); length = 3;
            if (level is >= 0 and < 10) text[length++] = (byte)' ';
            Append(level, text, ref length);
            smallFont.DrawText(text[..length], target, width, height, stride, 207, row - 12, 0xffe0);
            int earnedEnd = 252 + Math.Min(ExperienceBarMath.Ratio(155, next, xp, precision), 155);
            int awardEnd = Math.Min(unchecked(earnedEnd - ExperienceBarMath.Ratio(-155, next, allocation[actor], precision)), 407);
            if (earnedEnd > 252) Gradient(target, width, height, stride, 252, earnedEnd, row - 10, row, ExperiencePalette.Earned);
            if (awardEnd > earnedEnd) Gradient(target, width, height, stride, earnedEnd, awardEnd, row - 10, row, ExperiencePalette.Award);
            if (awardEnd < 407) Gradient(target, width, height, stride, awardEnd, 407, row - 10, row, ExperiencePalette.Remaining);
        }
        if (extraReward != 0)
        {
            int length = 0;
            Copy(battleLabels[48].Span, text, ref length); text[length++] = (byte)' ';
            Append(extraReward / 10, text, ref length); text[length++] = (byte)' '; Copy(battleLabels[49].Span, text, ref length);
            smallFont.DrawText(text[..length], target, width, height, stride, 351, 294, NativeUiPalette.Normal[20]);
            smallFont.DrawText(text[..length], target, width, height, stride, 349, 292, NativeUiPalette.Normal[0]);
        }
        for (int i = 0; i < 3; i++)
        {
            int x = 149 + i * 85;
            ReadOnlySpan<byte> label = battleLabels[42 + i].Span;
            int nul = label.IndexOf((byte)0); if (nul >= 0) label = label[..nul];
            if (i == mode) Fill(target, width, height, stride, x - 2, 267, x + 3 + label.Length * 8, 286, 0x000f);
            smallFont.DrawText(label, target, width, height, stride, x, 269, NativeUiPalette.Normal[4]);
        }
    }
    private static int Read(ReadOnlySpan<byte> record, int offset) => BinaryPrimitives.ReadInt32LittleEndian(record[offset..]);
    private static void Append(int value, Span<byte> target, ref int cursor)
    {
        if (!Utf8Formatter.TryFormat(value, target[cursor..], out int length)) throw new InvalidDataException("XP label exceeds native format capacity.");
        cursor += length;
    }
    private static void Copy(ReadOnlySpan<byte> value, Span<byte> target, ref int cursor)
    {
        int nul = value.IndexOf((byte)0); if (nul >= 0) value = value[..nul];
        if (value.Length > target.Length - cursor) throw new InvalidDataException("XP label exceeds native format capacity.");
        value.CopyTo(target[cursor..]); cursor += value.Length;
    }
    private static void Fill(Span<ushort> target, int width, int height, int stride, int left, int top, int right, int bottom, ushort color)
    {
        left = Math.Max(0, left); right = Math.Min(width, right); top = Math.Max(0, top); bottom = Math.Min(height, bottom);
        if (right <= left) return;
        for (int y = top; y < bottom; y++) target.Slice(y * stride + left, right - left).Fill(color);
    }
    private static void Gradient(Span<ushort> target, int width, int height, int stride, int left, int right, int top, int bottom, ReadOnlySpan<ushort> palette)
    {
        for (int y = Math.Max(0, top); y <= Math.Min(height - 1, bottom); y++)
            Fill(target, width, height, stride, left, y, right, y + 1, palette[(y - top) & 31]);
    }
}
