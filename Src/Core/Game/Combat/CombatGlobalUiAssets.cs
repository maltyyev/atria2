using Atria.Core.Presentation.Sprites;
using Atria.Core.Resources;
using Atria.Core.Resources.Codecs;
using Atria.Core.Resources.Localization;

namespace Atria.Core.Game.Combat;

public sealed record CombatGlobalUiAssets(BitmapFont Font, BattleMessages Messages)
{
    public static CombatGlobalUiAssets Load(string root)
    {
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true);
        var messages = new BattleMessages(GameText.Messages(LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root, "BATTLE.DAT"))), "battle"));
        return new(font, messages);
    }
}
