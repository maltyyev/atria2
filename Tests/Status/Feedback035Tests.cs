using System.Security.Cryptography;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Resources;

namespace Atria.Tests.Status;

public static class Feedback035Tests
{
    public static string Run(string root, string? fixture = null)
    {
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException("Feedback035: " + message);
            }
        }

        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var state = InitialGameState.Create(items, messages);
        state.OwnedItems[60] = 2;
        state.OwnedItems[62] = 2;
        state.AvailableItems[62] = 1;
        state.SetPartyInt(1, 240, 62);
        InventoryMaintenance.ReconcileBattleSupplies(state);
        Check(state.OwnedItems[60] == 0 && !new StatusTextList(state, 1).Ids.Contains(60), "phantom exhausted supply removed from inventory");
        Check(state.OwnedItems[62] == 2 && state.AvailableItems[62] == 1 && state.GetPartyInt(1, 240) == 62, "unrelated genuine equipped and free consumables retained");
        state.SetPartyInt(0, 228, 60);
        state.AvailableItems[60] = 1;
        state.OwnedItems[60] = 99;
        InventoryMaintenance.ReconcileBattleSupplies(state);
        Check(state.OwnedItems[60] == 2, "migration preserves reserved and free units");
        var bytes = NativeGameStateCodec.Encode(state);
        InventoryMaintenance.ReconcileBattleSupplies(state);
        Check(bytes.AsSpan().SequenceEqual(NativeGameStateCodec.Encode(state)), "migration is idempotent");
        foreach (int gold in new[]
        {
            0,
            1,
            5,
            6,
            200,
            int.MaxValue
        }
        )
        {
            state.Money = gold;
            state.Day = 30;
            state.Month = 12;
            state.Year = 1450;
            for (int step = 0; step < 100; step++)
            {
                ExplorationRules.AdvanceTravelDay(state);
            }

            Check(state.Money == gold && state.Year == 1451, "100 travel days preserve any gold balance while calendar advances");
        }

        if (fixture is not null)
        {
            byte[] original = File.ReadAllBytes(fixture);
            var image = NativeSaveImage.Parse(original);
            var context = SessionRuntimeCodec.Decode(image.Context);
            InventoryMaintenance.ReconcileBattleSupplies(image.State);
            Check(image.State.OwnedItems[60] == 0 && image.State.AvailableItems[60] == 0, "real0.3.4 fixture has no remaining herbal dust");
            Check(!new StatusTextList(image.State, 1).Ids.Contains(60), "real0.3.4 phantom item absent from inventory");
            Check(image.State.OwnedItems[62] == 2 && image.State.AvailableItems[62] == 1 && image.State.OwnedItems[63] == 2
&& image.State.AvailableItems[63] == 1, "real remaining consumables preserved");
            var encoded = System.Text.Encoding.UTF8.GetString(SessionRuntimeCodec.Encode(context).Payload.Span);
            Check(!encoded.Contains("Trap", StringComparison.Ordinal), "old snapshot accepted; removed fields omitted from new serialization");
            Check(original.AsSpan().SequenceEqual(File.ReadAllBytes(fixture)), "user fixture untouched");
            Console.WriteLine("Read-only0.3.4 fixture SHA256=" + Convert.ToHexString(SHA256.HashData(original)));
        }

        return "Feedback0.3.5: exhausted supply migration, real reserved items, idempotence, travel gold and old snapshot decode passed.";
    }
}
