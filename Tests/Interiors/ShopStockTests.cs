using System.Buffers.Binary;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Interiors;

public static class ShopStockTests
{
    public static string Run(string root, string? saveFile = null)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(root, "SHOP.DAT"));
        var catalog = ShopCatalog.Parse(original);
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        for (int index = 0; index < ShopCatalog.Count; index++)
        {
            var stock = ShopStock.FromCatalog(catalog, index);
            byte[] before = stock.Encode();
            var browse = new ShopSession(new GameState(), items);
            browse.BeginBuy(stock);
            Check(browse.VisibleRows().All(row => row.ItemId >= 0), "all original shops open without invalid displayed IDs");
            Check(stock.Encode().SequenceEqual(before), "browsing does not rewrite persisted stock");
        }

        var bellot = ShopStock.FromCatalog(catalog, 11);
        Check(bellot.Count(2) == 3 && bellot.Item(2, 1) == -1 && bellot.Quantity(2, 1) == 0,
            "real Bellot stock contains the reported empty middle slot");
        var state = new GameState { Money = 100000 };
        var session = new ShopSession(state, items);
        session.BeginBuy(bellot);
        session.HandleInput(new(Right: true));
        session.HandleInput(new(Right: true));
        Check(session.Count == 2 && session.VisibleRows().Select(row => row.ItemId).SequenceEqual(new[] { 58, 84 }),
            "valid items on both sides of the hole retain their order");
        session.HandleInput(new(Confirm: true));
        Check(session.HandleInput(new(Confirm: true)).Sound == 6, "purchase after the hole is removed succeeds");
        Check(state.OwnedItems[58] == 1 && state.Money == 100000 - items.GetItem(58).GetInteger(4)
            && bellot.Count(2) == 2 && bellot.Quantity(2, 0) == 9 && bellot.Item(2, 1) == 84 && bellot.Quantity(2, 1) == 50,
            "purchase changes only the chosen quantity, money and inventory");
        session.BeginBuy(LegacyShopProjection.DecodeAll(LegacyShopProjection.EncodeAll(
            Enumerable.Range(0, ShopCatalog.Count).Select(i => ShopStock.FromCatalog(catalog, i)).ToArray()))[11]);

        // Empty first categories must be skipped after filtering, including a completely empty shop.
        byte[] empty = new byte[ShopCatalog.Count * ShopCatalog.RecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(empty, 1);
        BinaryPrimitives.WriteInt32LittleEndian(empty.AsSpan(4), -1);
        session.BeginBuy(ShopStock.FromCatalog(ShopCatalog.Parse(empty), 0));
        Check(session.Category == 2 && session.Count == 0 && session.SelectedItemId == -1,
            "cursor is initialized after empty rows are removed");
        Check(session.HandleInput(new(Confirm: true)).Sound == 4, "empty shop cannot purchase an item");
        BinaryPrimitives.WriteInt32LittleEndian(empty.AsSpan(104), 1);
        try
        {
            session.BeginBuy(ShopStock.FromCatalog(ShopCatalog.Parse(empty), 0));
            throw new Exception("Expected invalid stocked item rejection");
        }
        catch (InvalidDataException) { }

        if (saveFile is not null)
        {
            byte[] savedBytes = File.ReadAllBytes(saveFile);
            var saved = NativeSaveImage.Parse(savedBytes);
            byte[] before = saved.Encode();
            foreach (var stock in saved.Shops)
            {
                new ShopSession(saved.State, items).BeginBuy(stock);
            }
            Check(saved.Encode().SequenceEqual(before) && File.ReadAllBytes(saveFile).SequenceEqual(savedBytes),
                "all shops in the reported save open without modifying its state or file");
        }

        Check(File.ReadAllBytes(Path.Combine(root, "SHOP.DAT")).SequenceEqual(original), "original catalog is unchanged");
        return "Shop stock: all30 catalogs, Bellot hole/order/purchase, empty categories, invalid stocked IDs and saved stock passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Shop stock: " + message);
    }
}
