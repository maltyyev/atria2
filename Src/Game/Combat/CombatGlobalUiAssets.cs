using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Combat;

public sealed record CombatGlobalUiAssets(BitmapFont Font, BattleMessages Messages)
{
    public static CombatGlobalUiAssets Load(string root)
    {
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        var messages = new BattleMessages(GameText.Messages(LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root, "BATTLE.DAT"))), "battle"));
        return new(font, messages);
    }
}
