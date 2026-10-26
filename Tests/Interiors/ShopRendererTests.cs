using Atria.Localization;
using System.Buffers.Binary;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Interiors;

public static class ShopRendererTests
{
    public static void Run(string root)
    {
        static void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException("Shop renderer: " + message);
            }
        }

        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        var item = SpriteFile.Decode(archive.ReadMember(archive.FindResource("item.spr"u8)));
        var etc = SpriteFile.Decode(archive.ReadMember(archive.FindResource("etc.spr"u8)));
        var small = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true);
        var large = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng.fnt")), false);
        var renderer = new ShopRenderer(item, etc, small, large, items, File.ReadAllBytes(Path.Combine(root, "ITEM_EX.DAT")), messages);
        byte[] shopBytes = new byte[30 * 612];
        var state = new GameState
        {
            Money = int.MaxValue
        };
        var session = new ShopSession(state, items);
        session.BeginBuy(ShopStock.FromCatalog(ShopCatalog.Parse(shopBytes), 0));
        var empty = renderer.BuildPlan(session, state, new(0));
        Check(empty.Count(op => op.Primitive == ShopPrimitive.Panel) == 3, "list, categories and expanded item-detail panels");
        Check(empty.Any(op => op.Text.Span.SequenceEqual(messages[353].Span) && op.X == 160 && op.Y == 228 && op.Style == ShopTextStyle.Title && !op.Small), "empty message353 exact large gradient coordinates");
        var clock = new ShopRenderClock();
        Check(clock.AdvanceLogicalFrame(false).ArrowPhase == 0, "empty render does not advance cursor");
        for (int i = 0; i < 4; i++)
        {
            Check(clock.AdvanceLogicalFrame(true).ArrowPhase == 0, "cursor0 retained four logical frames");
        }

        Check(clock.AdvanceLogicalFrame(true).ArrowPhase == 1, "cursor advances after four logical draws");
        ushort[] pixels = new ushort[640 * 480];
        for (int category = 0; category < 3; category++)
        {
            Array.Clear(shopBytes);
            Put(shopBytes, category * 204, 1);
            Put(shopBytes, category * 204 + 4, category == 2 ? 84 : category == 1 ? 25 : 0);
            Put(shopBytes, category * 204 + 104, 3);
            session.BeginBuy(ShopStock.FromCatalog(ShopCatalog.Parse(shopBytes), 0));
            if (category == 1 && session.Category != 1)
            {
                session.HandleInput(new(Right: true));
            }

            var plan = renderer.BuildPlan(session, state, new(2));
            Check(plan.Any(op => op.Primitive == ShopPrimitive.ItemSprite && op.X == 144 && op.Y == 132), "native list icon origin");
            Check(plan.Any(op => op.Primitive == ShopPrimitive.Text && op.X == 180 && op.Y == 140 && op.Small && op.Style == ShopTextStyle.Selected), "small list name font/coordinates");
            Check(plan.Any(op => op.Primitive == ShopPrimitive.Text && op.X == 356 && op.Y == 137 && !op.Small && op.Style == ShopTextStyle.Selected), "large quantity font/coordinates");
            Check(plan.Any(op => op.Primitive == ShopPrimitive.EtcSprite && op.A == 24 && op.X == 120 && op.Y == 133), "native cursor frame22+phase");
            Check(plan.Any(op => op.X == 480 && op.Y == 350 && op.Small && op.Style == ShopTextStyle.Selected), "buy price below wrapped description");
            if (category == 2)
            {
                Check(plan.Any(op => GameText.Decode(op.Text.Span).Contains(GameText.Decode(messages[340].Span).Trim(), StringComparison.Ordinal) && op.X == 432 && op.Wrap), "special84 description uses actual TEXTmsg340");
            }

            Array.Fill(pixels, (ushort)0x7bef);
            renderer.Draw(session, state, new(2), pixels);
            Check(pixels.Any(p => p != 0x7bef), "real ITEM/ETC/fonts compose");
            session.HandleInput(new(Confirm: true));
            plan = renderer.BuildPlan(session, state, new(0));
            Check(plan.Count(op => op.Primitive == ShopPrimitive.Tint && op.X == 142 && op.Y == 130) == 2, "quantity overlay darkens twice");
            Check(plan.Any(op => op.X == 228 && op.Y == 138 && op.Style == ShopTextStyle.Quantity && !op.Small), "quantity uses recovered separate palette");
            renderer.Draw(session, state, new(0), pixels);
        }

        foreach (int id in new[]
        {
            3,
            46,
            47
        }
        )
        {
            Array.Clear(shopBytes);
            int category = id == 3 ? 0 : 1;
            Put(shopBytes, category * 204, 1);
            Put(shopBytes, category * 204 + 4, id);
            Put(shopBytes, category * 204 + 104, 1);
            session.BeginBuy(ShopStock.FromCatalog(ShopCatalog.Parse(shopBytes), 0));
            if (category == 1 && session.Category != 1)
            {
                session.HandleInput(new(Right: true));
            }

            var plan = renderer.BuildPlan(session, state, new());
            Check(!plan.Any(op => op.Primitive == ShopPrimitive.Text && op.Text.Span.SequenceEqual(messages[57].Span)), "removed modifier absent from actual knife and shoe shop details");
        }

        state.AvailableItems[0] = 1;
        state.OwnedItems[0] = 2;
        session.BeginSell();
        var sell = renderer.BuildPlan(session, state, new(0));
        Check(sell.Any(op => op.X == 432 && op.Y == 128 && op.Small && op.Style == ShopTextStyle.Selected), "sale price uses original small font inside detail panel");
        renderer.Draw(session, state, new(0), pixels);
    }

    private static void Put(byte[] b, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offset, 4), value);
}
