using System.Buffers.Binary;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;
using Atria.Game.Controls;

namespace Atria.Game.Combat;

public sealed record CombatSelectionAssets(SpriteFile Etc, SpriteFile Player, BitmapFont Font,
    IReadOnlyList<ReadOnlyMemory<byte>> Labels)
{
    public static CombatSelectionAssets Load(string root)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "SPR.000"));
        SpriteFile Read(ReadOnlySpan<byte> key)
        {
            int index = archive.FindResource(key);
            if (index < 0) throw new FileNotFoundException("Required prebattle UI sprite is missing.");
            return SpriteFile.Decode(archive.ReadMember(index));
        }
        return new(Read("ETC"u8), Read("PLAYER"u8),
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true),
            LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root, "BATTLE.DAT"))));
    }
}

/// <summary>424100 exact640x480 prebattle UI over a caller-composed battle backdrop.</summary>
public sealed class CombatSelectionRaster(CombatSelectionAssets assets)
{
    public void DrawControls(InputRouter input,Span<ushort> target)
    {
        Fill(target,70,330,570,404,0x1082);
        ControlPanel.Text(assets.Font,target,"Вверх / вниз: герой; щелчок: участие",82,336,0xffff,60);
        ControlPanel.Text(assets.Font,target,input.Hint(0,GameAction.Attack)+": включить / исключить героя",82,354,0xffff,60);
        ControlPanel.Text(assets.Font,target,"Начать бой: "+input.Hint(0,GameAction.Confirm),130,380,0xffe0,54);
    }
    public void Draw(CombatPartySelection selection, Span<ushort> target)
    {
        if (target.Length != 640 * 480 || assets.Font.Width != 8 || assets.Font.Height != 16 || assets.Labels.Count < 42 || assets.Player.Frames.Length < 6)
            throw new ArgumentException("Incomplete native prebattle UI state/assets.");
        Panel(100, 110, 4, 3, target); Panel(292, 110, 2, 3, target); Panel(420, 110, 2, 3, target);
        Span<byte> text = stackalloc byte[512];
        for (int actor = 0; actor < 6; actor++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(selection.Party.Span[(actor * 280 + 256)..]) == 0) continue;
            int y = 135 + actor * 20;
            for (int player = 0; player < selection.PlayerCount; player++)
                if (selection.Leaders[player] == actor) Fill(target, 126, y - 2, 266, y + 16, player == 0 ? (ushort)0x000f : (ushort)0x07ff);
            ReadOnlySpan<byte> name = assets.Labels[24 + actor].Span;
            ReadOnlySpan<byte> status = assets.Labels[selection.Selected[actor] == 0 ? 31 : 30].Span;
            int length = 0; Copy(name, text, ref length); text[length++] = (byte)' '; Copy(status, text, ref length);
            assets.Font.DrawText(text[..length], target, 640, 480, 640, 129, y,
                selection.Selected[actor] == 0 ? NativeUiPalette.Normal[23] : NativeUiPalette.Normal[4]);
        }
        for (int player = 0; player < selection.PlayerCount; player++)
        {
            SpriteFrame portrait = assets.Player.Frames[selection.Leaders[player]];
            SpriteRaster.Draw565(portrait, target, 640, 480, 640, 356 + player * 128 - (portrait.Width >> 1), 255 - portrait.Height, SpriteBlend.Copy);
        }
        Panel(100, 270, 12, 0, target);
        ReadOnlySpan<byte> message = assets.Labels[36 + selection.ErrorCode].Span;
        int nul = message.IndexOf((byte)0); if (nul >= 0) message = message[..nul];
        assets.Font.DrawText(message, target, 640, 480, 640, 325 - message.Length * 4, 295,
            selection.ErrorCode == 0 ? NativeUiPalette.Normal[7] : (ushort)0xf98c);
    }
    private void Panel(int x, int y, int horizontal, int vertical, Span<ushort> target)
    {
        UiRaster.TintHalf(target, 640, 480, 640, x + 14, y + 14, horizontal * 32 + 36, vertical * 32 + 36, 0);
        UiRaster.DrawPanel(assets.Etc, x, y, horizontal, vertical, target);
    }
    private static void Copy(ReadOnlySpan<byte> value, Span<byte> target, ref int length)
    {
        int nul = value.IndexOf((byte)0); if (nul >= 0) value = value[..nul];
        if (value.Length > target.Length - length) throw new InvalidDataException("Prebattle name/status exceeds native format capacity.");
        value.CopyTo(target[length..]); length += value.Length;
    }
    private static void Fill(Span<ushort> target, int left, int top, int right, int bottom, ushort color)
    { for (int y = top; y < bottom; y++) target.Slice(y * 640 + left, right - left).Fill(color); }
}
