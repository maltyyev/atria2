using Atria.Game.Combat;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Resources;
using Atria.Tests.Render;

namespace Atria.Tests.Combat;

internal static class CombatSelectionAssetsTests
{
    public static string Run(string root, string? savedGame = null)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var state = savedGame is null
            ? InitialGameState.Create(items, LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))))
            : NativeSaveImage.Parse(File.ReadAllBytes(savedGame)).State;
        var before = NativeGameStateCodec.Encode(state);
        var assets = CombatSelectionAssets.Load(root);
        var raster = new CombatSelectionRaster(assets);
        var pixels = new ushort[640 * 480];

        // The production catalog includes unrecruited heroes; render it unchanged before recruitment.
        foreach (int players in new[] { 1, 2 })
        {
            var selection = new CombatPartySelection(state, players);
            Array.Clear(pixels);
            raster.Draw(selection, pixels);
            selection.ApplyInput((player, input) => input == CombatSelectionInput.Toggle
                && selection.Selected[selection.Leaders[player]] == 0, _ => { });
            selection.ApplyInput((_, input) => input == CombatSelectionInput.Finish, _ => { });
            if (!selection.Completed)
            {
                throw new InvalidOperationException("Party cannot confirm its chosen battle leaders.");
            }
        }
        if (!before.AsSpan().SequenceEqual(NativeGameStateCodec.Encode(state)))
        {
            throw new InvalidOperationException("Selection rendering mutated the saved party.");
        }

        var recruited = NativeGameStateCodec.Decode(before);
        int eiren = recruited.HeroCatalog.Find("eiren_wald");
        recruited.Heroes[eiren].MemberStatus = recruited.Heroes[eiren].SelectedForBattle = 1;
        foreach (int players in new[] { 1, 2 })
        {
            for (int player = 0; player < players; player++)
            {
                var selection = new CombatPartySelection(recruited, players);
                selection.PointerSelect(0, 0);
                if (!selection.PointerSelect(player, eiren))
                {
                    throw new InvalidOperationException("Recruited necromancer cannot lead a player.");
                }
                Array.Clear(pixels);
                raster.Draw(selection, pixels);
            }
        }
        int portrait = recruited.HeroCatalog[eiren].Portrait;
        if (!assets.Player.Frames[portrait].Runs.Any(run => run.Alpha.Length != 0))
        {
            throw new InvalidOperationException("Authored selection portrait lost its transparency.");
        }
        string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "combat-selection-0152"));
        Directory.CreateDirectory(output);
        UnicodeTextTests.SaveBitmap(Path.Combine(output, "selection.bmp"), pixels);
        CombatSelectionModalTests.Run();
        return "Selection assets PASS: actual catalog before/after recruitment, both player portraits, saved state unchanged, modal confirmation.";
    }
}
