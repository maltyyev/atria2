using System.Buffers.Binary;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Interiors;

public static class InteriorTests
{
    public static string Run(string root)
    {
        static void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException(message);
            }
        }

        byte[] sod = new byte[490];
        "Object Tool ver2.0"u8.CopyTo(sod);
        Put(sod, 30, 1);
        "object.spr\0"u8.CopyTo(sod.AsSpan(47, 13));
        "test.scr\0"u8.CopyTo(sod.AsSpan(94, 13));
        Put(sod, 120, 123);
        Put(sod, 124, 45);
        Put(sod, 120 + 104, 1);
        Put(sod, 120 + 108, 2);
        Put(sod, 120 + 112, 63);
        Put(sod, 120 + 116, 64);
        "test.sod\0"u8.CopyTo(sod.AsSpan(240, 50));
        var scene = SodV2.Parse(sod);
        var commands = new List<InteriorCommand>();
        var controller = new InteriorController();
        controller.Enter(410, scene.GetObject(0), scene, commands.Add);
        Check(controller.EventRange == new InteriorEventRange(1, 2, 63, 64), "Outer object fields26..29 event metadata.");
        Check(commands.Any(c => c.Kind == InteriorCommandKind.QueueEnter && c.Bytes.Span.SequenceEqual("enter"u8)), "Interior enter function queued explicitly.");
        commands.Clear();
        controller.HandleInput(false, commands.Add);
        Check(!controller.FirstEntry && commands[^1].Value == 3 && commands[^1].Value2 == 3, "410 first-entry clears flag and opens type3/menu3.");
        var frame = controller.BuildRenderFrame(true);
        Check(frame.DrawObject && frame.X == 123 && frame.Y == 45, "410 selected sprite coordinates from SOD.");
        controller.Enter(420, scene.GetObject(0), scene, commands.Add);
        commands.Clear();
        controller.HandleInput(false, commands.Add);
        controller.HandleInput(false, commands.Add);
        Check(controller.FirstEntry && commands.Count(c => c.Kind == InteriorCommandKind.OpenActionMenu) == 2
&& !controller.BuildRenderFrame(true).DrawObject, "ASM420 flag remains1 and verified empty render.");
        var state = new GameState();
        state.EventStatuses[63] = 0;
        state.EventStatuses[64] = 1;
        var offer = InteriorEventOffers.Select(state, new(1, 2, 63, 64));
        Check(offer is not null && offer.EventId == 64 && offer.Function.Span.SequenceEqual("suba_064"u8) && state.EventCounter == 64, "Variant2 skips inactive event and builds actual padded handler.");
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var talk = InteriorTalkMenu.Build(state, "test.scr"u8.ToArray(), offer, messages);
        Check(talk.Choices.Count == 1 && talk.Select(0).IsEvent && talk.Select(0).Label.Span.SequenceEqual(messages[316].Span)
&& talk.Select(0).Function.Span.SequenceEqual("suba_064"u8), "Actual event talk label and script dispatch data.");
        state.EventStatuses[63] = 5;
        Check(InteriorEventOffers.Select(state, new(1, 2, 63, 64)) is null, "Status5 stops event offer scan.");
        byte[] itemBytes = File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT"));
        Put(itemBytes, 30, 0);
        Put(itemBytes, 46, 11);
        Put(itemBytes, 84 * 98 + 46, 4);
        var items = ItemCatalog.Parse(itemBytes);
        byte[] shopBytes = new byte[30 * 612];
        Put(shopBytes, 0, 2);
        Put(shopBytes, 4, 0);
        Put(shopBytes, 8, 84);
        Put(shopBytes, 104, 1);
        Put(shopBytes, 108, 2);
        var stock = ShopStock.FromCatalog(ShopCatalog.Parse(shopBytes), 0);
        state = new GameState
        {
            Money = 10
        };
        var session = new ShopSession(state, items);
        session.BeginBuy(stock);
        session.HandleInput(new(Confirm: true));
        var rejected = session.HandleInput(new(Confirm: true));
        Check(rejected.Sound == -1 && session.InsufficientFunds && session.QuantityActive && state.Money == 10, "Insufficient funds is a quantity overlay, no invented purchase.");
        Check(session.HandleInput(new(Confirm: true)).Sound == 4 && !session.QuantityActive && !session.InsufficientFunds, "Second confirm dismisses insufficient funds.");
        state.Money = 100;
        session.HandleInput(new(Confirm: true));
        Check(session.HandleInput(new(Confirm: true)).Sound == 6, "Buy sound after completed transaction.");
        Check(state.Money == 89 && state.OwnedItems[0] == 1 && state.AvailableItems[0] == 1 && stock.Count(0) == 1 && stock.Item(0, 0) == 84, "Buy updates both banks, money and persistent stock compaction.");
        session.HandleInput(new(Confirm: true));
        session.HandleInput(new(HeldUp: true));
        session.HandleInput(new(HeldUp: true));
        session.HandleInput(new(Confirm: true));
        Check(state.Money == 81 && state.ResourceCounterDc88 == 2 && stock.Count(0) == 0 && session.Row == -1, "Special item quantity and native empty-list cursor.");
        state = new GameState();
        state.OwnedItems[0] = state.AvailableItems[0] = 2;
        state.AvailableItems[88] = 99;
        session = new(state, items);
        session.BeginSell();
        Check(session.Count == 1, "Native sell scans only88 IDs and ignores catalog88.");
        session.HandleInput(new(Confirm: true));
        session.HandleInput(new(Confirm: true));
        Check(state.Money == 5 && state.OwnedItems[0] == 1 && state.AvailableItems[0] == 1, "Sale uses truncated half11/2 before quantity multiplication.");
        Put(itemBytes, 46, int.MaxValue);
        items = ItemCatalog.Parse(itemBytes);
        Put(shopBytes, 0, 1);
        Put(shopBytes, 104, 2);
        stock = ShopStock.FromCatalog(ShopCatalog.Parse(shopBytes), 0);
        state = new GameState();
        session = new(state, items);
        session.BeginBuy(stock);
        session.HandleInput(new(Confirm: true));
        session.HandleInput(new(HeldUp: true));
        session.HandleInput(new(HeldUp: true));
        session.HandleInput(new(Confirm: true));
        Check(state.Money == 2 && state.OwnedItems[0] == 2, "Native signed IMUL cost wraps before affordability comparison.");
        ShopRendererTests.Run(root);
        return "Interiors: entry metadata/action/render flags, bounded event-offer order, native buy/sell/special-item/stock and signed-cost fixtures passed; original gameplay/UI parity pending.";
    }

    private static void Put(byte[] b, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offset, 4), value);
}
