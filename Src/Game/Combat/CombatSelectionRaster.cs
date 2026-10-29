using System.Buffers.Binary;
using Atria.Game.Controls;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Combat;

public sealed record CombatSelectionAssets(SpriteFile Etc, SpriteFile Player, BitmapFont Font, IReadOnlyList<ReadOnlyMemory<byte>> Labels)
{
    public static CombatSelectionAssets Load(string root)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "SPR.000"));
        SpriteFile Read(ReadOnlySpan<byte> key)
        {
            int index = archive.FindResource(key);
            if (index < 0)
            {
                throw new FileNotFoundException("Required prebattle UI sprite is missing.");
            }

            return SpriteFile.Decode(archive.ReadMember(index));
        }

        const int FreedCompanionPortrait = 3;
        var portraits = Read("PLAYER"u8);
        var companion = SpriteFile.Decode(File.ReadAllBytes(Path.Combine(root, "..", "interface", "EirenFaces.spr")));
        portraits = portraits with
        {
            Frames = [.. portraits.Frames, companion.Frames[FreedCompanionPortrait]]
        };
        return new(
            Read("ETC"u8),
            portraits,
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true),
            GameText.Messages(LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root, "BATTLE.DAT"))), "battle")
        );
    }
}

/// <summary>424100 exact640x480 prebattle UI over a caller-composed battle backdrop.</summary>
public sealed class CombatSelectionRaster(CombatSelectionAssets assets)
{
    public void DrawControls(InputRouter input, Span<ushort> target)
    {
        Fill(target, 70, 330, 570, 404, 0x1082);
        ControlPanel.Text(assets.Font, target, GameText.Get("ui.combat.selectHero"), 82, 336, 0xffff, 60);
        ControlPanel.Text(assets.Font, target, GameText.Format("ui.combat.toggleHero", ("button", input.Hint(0, GameAction.Attack))), 82, 354, 0xffff, 60);
        ControlPanel.Text(assets.Font, target, GameText.Format("ui.combat.start", ("button", input.Hint(0, GameAction.Confirm))), 130, 380, 0xffe0, 54);
    }

    public void Draw(CombatPartySelection selection, Span<ushort> target)
    {
        if (target.Length != 640 * 480 || assets.Font.Width != 8 || assets.Font.Height != 16 || assets.Labels.Count < 42
            || Enumerable.Range(0, selection.HeroCatalog.Count).Any(i => selection.HeroCatalog[i].Portrait >= assets.Player.Frames.Length))
        {
            throw new ArgumentException("Incomplete native prebattle UI state/assets.");
        }

        Panel(100, 110, 4, 3, target);
        Panel(292, 110, 2, 3, target);
        Panel(420, 110, 2, 3, target);
        for (int actor = selection.FirstVisible; actor < Math.Min(selection.Party.Count, selection.FirstVisible + CombatPartySelection.VisibleRows); actor++)
        {
            if (selection.Party[actor].PendingAction == 0)
            {
                continue;
            }

            int y = 135 + (actor - selection.FirstVisible) * 20;
            for (int player = 0; player < selection.PlayerCount; player++)
            {
                if (selection.Leaders[player] == actor)
                {
                    Fill(target, 126, y - 2, 266, y + 16, player == 0 ? (ushort)0x000f : (ushort)0x07ff);
                }
            }

            ReadOnlySpan<byte> status = assets.Labels[selection.Selected[actor] == 0 ? 31 : 30].Span;
            string text = selection.HeroCatalog[actor].Name + " " + GameText.Decode(status);
            assets.Font.DrawText(text, target, 640, 480, 640, 129, y, selection.Selected[actor] == 0 ? NativeUiPalette.Normal[23] : NativeUiPalette.Normal[4]);
        }

        if (selection.Party.Count > CombatPartySelection.VisibleRows)
        {
            string range = $"{selection.FirstVisible + 1}–{Math.Min(selection.Party.Count, selection.FirstVisible + CombatPartySelection.VisibleRows)} / {selection.Party.Count}";
            assets.Font.DrawText(range, target, 640, 480, 640, 129, 254, NativeUiPalette.Normal[23]);
        }

        for (int player = 0; player < selection.PlayerCount; player++)
        {
            SpriteFrame portrait = assets.Player.Frames[selection.HeroCatalog[selection.Leaders[player]].Portrait];
            SpriteRaster.Draw565(portrait, target, 640, 480, 640, 356 + player * 128 - (portrait.Width >> 1), 255 - portrait.Height,
                portrait.Runs.Any(run => run.Alpha.Length != 0) ? SpriteBlend.AlphaPercent : SpriteBlend.Copy);
        }

        Panel(100, 270, 12, 0, target);
        ReadOnlySpan<byte> message = assets.Labels[36 + selection.ErrorCode].Span;
        int nul = message.IndexOf((byte)0);
        if (nul >= 0)
        {
            message = message[..nul];
        }

        assets.Font.DrawText(message, target, 640, 480, 640, 325 - message.Length * 4, 295, selection.ErrorCode == 0 ? NativeUiPalette.Normal[7] : (ushort)0xf98c);
    }

    private void Panel(int x, int y, int horizontal, int vertical, Span<ushort> target)
    {
        UiRaster.TintHalf(target, 640, 480, 640, x + 14, y + 14, horizontal * 32 + 36, vertical * 32 + 36, 0);
        UiRaster.DrawPanel(assets.Etc, x, y, horizontal, vertical, target);
    }

    private static void Fill(Span<ushort> target, int left, int top, int right, int bottom, ushort color)
    {
        for (int y = top; y < bottom; y++)
        {
            target.Slice(y * 640 + left, right - left).Fill(color);
        }
    }
}
