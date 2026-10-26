using System.Buffers.Binary;
using Atria.Game.ActionMenu;
using Atria.Game.Exploration;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Tests.Render;

public static class LayoutFeedbackTests
{
    public static string Run(string root, string language = LocalizationCatalog.BaseLanguage)
    {
        var previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization"), language));
        try
        {
            return CheckScreens(root);
        }
        finally
        {
            GameText.Use(previous);
        }
    }

    private static string CheckScreens(string root)
    {
        string output = Path.Combine(root, "..", "audit", GameText.Current.Language == LocalizationCatalog.BaseLanguage ? "layout-093" : "layout-" + GameText.Current.Language);
        Directory.CreateDirectory(output);
        byte[] Read(string file) => File.ReadAllBytes(Path.Combine(root, file));
        var rawMessages = LegacyTextTables.ParseMessages(Read("TEXTmsg.dat"));
        var messages = GameText.Messages(rawMessages);
        var items = ItemCatalog.Parse(Read("ATITEM.DAT"));
        var small = BitmapFont.DecodeEng(Read("eng1.fnt"), true);
        var large = BitmapFont.DecodeEng(Read("eng.fnt"), false);
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        SpriteFile Sprite(ReadOnlySpan<byte> name) => SpriteFile.Decode(archive.ReadMember(archive.FindResource(name)));
        var etc = Sprite("etc.spr"u8);
        var itemSprites = Sprite("item.spr"u8);
        ushort[] pixels = new ushort[640 * 480];
        void Capture(string name) => UnicodeTextTests.SaveBitmap(Path.Combine(output, name + ".bmp"), pixels);
        var overflow = new HashSet<string>();
        void Fits(string text, BitmapFont font, int w, int h, bool wrap)
        {
            var lines = TextBoxRaster.Layout(text, font, w, h, wrap);
            if (lines.Any(line => font.MeasureText(line) + Math.Max(0, font.Width - font.Advance) > w) || lines.Length * font.Height > h)
            {
                overflow.Add($"{w}x{h}: {text}");
            }
        }

        foreach (bool gradient in new[] { false, true })
        {
            foreach (string text in new[] { "Коротко", "Очень длинная надпись с переносом на следующие строки", "中文 日本語 한국어" })
            {
                Array.Fill(pixels, (ushort)0x1234);
                TextBoxRaster.Draw(small, text, pixels, 640, 480, 640, new(100, 100, 230, 160), 0xffff,
                    gradient ? NativeUiPalette.Normal : default, wrap: text.Length > 12);
                Check(pixels.Where((_, i) => i % 640 < 100 || i % 640 >= 230 || i / 640 < 100 || i / 640 >= 160).All(p => p == 0x1234), "Bounded text cannot paint outside its panel");
                Check(pixels.Any(p => p != 0x1234), "Bounded text remains visible");
            }
        }

        foreach (var font in new[] { small, large })
        {
            foreach (bool gradient in new[] { false, true })
            {
                const string sample = "ПРЕЖНИЙ ШРИФТ";
                int lineWidth = font.MeasureText("ПРЕЖНИЙ") + Math.Max(0, font.Width - font.Advance);
                ushort[] expected = new ushort[640 * 480];
                Array.Clear(pixels);
                string[] lines = ["ПРЕЖНИЙ", "ШРИФТ"];
                for (int line = 0; line < lines.Length; line++)
                {
                    if (gradient)
                    {
                        UiRaster.DrawStyledText(font, lines[line], NativeUiPalette.Normal, 0, expected, 640, 480, 640, 100, 100 + line * font.Height);
                    }
                    else
                    {
                        font.DrawText(lines[line], expected, 640, 480, 640, 100, 100 + line * font.Height, 0xffff);
                    }
                }
                TextBoxRaster.Draw(font, sample, pixels, 640, 480, 640, new(100, 100, 100 + lineWidth, 100 + 2 * font.Height), 0xffff,
                    gradient ? NativeUiPalette.Normal : default, wrap: true);
                Check(pixels.SequenceEqual(expected), "Wrapped text uses exact original glyph pixels, dimensions and colors");
            }
        }

        var state = InitialGameState.Create(items, rawMessages);
        var shop = new ShopRenderer(itemSprites, etc, small, large, items, Read("ITEM_EX.DAT"), messages);
        for (int id = 0; id < ItemCatalog.Count; id++)
        {
            Fits(GameText.Decode(GameText.ItemName(items, id).Span), small, 122, 32, true);
            int category = id < 25 ? 0 : id < 55 ? 1 : 2;
            byte[] stock = new byte[30 * 612];
            void Put(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(stock.AsSpan(category * 204 + offset), value);
            Put(0, 1);
            Put(4, id);
            Put(104, 3);
            var session = new ShopSession(state, items);
            session.BeginBuy(ShopStock.FromCatalog(ShopCatalog.Parse(stock), 0));
            if (session.Category != category)
            {
                session.HandleInput(new(Right: true));
            }
            var plan = shop.BuildPlan(session, state, new());
            foreach (var op in plan.Where(p => p.Primitive == ShopPrimitive.Text && p.A > 0))
            {
                Fits(GameText.Decode(op.Text.Span), op.Small ? small : large, op.A, op.B, op.Wrap);
            }
            Array.Fill(pixels, (ushort)0x2104);
            shop.Draw(session, state, new(), pixels);
            if (id is 3 or 58 or 84)
            {
                Capture($"shop-{id}");
            }
        }

        var status = new StatusMenuRenderer(itemSprites, etc, Sprite("charinfo.spr"u8), small, large, items, messages);
        var runtime = new StatusMenuRuntime();
        var journal = QuestJournalCatalog.Parse(Read("DANGEL.DAT"));
        var eventText = Read("EITEM.DAT");
        void Input(StatusMenuInput input) => runtime.Input(input, state, items, eventText, journal, _ => { });
        runtime.Enter(_ => { });
        Input(default);
        Input(default);
        Input(new(NextPage: true));
        Input(new(NextPage: true));
        Input(new(Accept: true));
        Check(runtime.Menu.DetailActive && runtime.Menu.Page == 2, "Actual characteristics page selected");
        for (int actor = 0; actor < 6; actor++)
        {
            foreach (string line in TextFlow.Wrap(GameText.Get($"characters.{actor}.introduction"), StatusMenuLayout.BiographyWidth))
            {
                Check(TextFlow.MeasureSmall(line) <= 248, "Biography uses center panel width");
            }
        }
        var statusPlan = status.BuildPlan(runtime, state, new(), journal);
        foreach (var op in statusPlan.Where(p => p.Primitive == StatusPrimitive.Text && p.A > 0))
        {
            Fits(op.Text, op.Small ? small : large, op.A, op.B, op.Wrap);
        }
        Check(runtime.Description.All(line => TextFlow.MeasureSmall(line) <= 248), "Runtime biography wraps before drawing");
        Array.Fill(pixels, (ushort)0x2104);
        status.DrawPlan(statusPlan, new(), pixels);
        Capture("characteristics");
        for (int i = 0; i < runtime.Description.Length; i++)
        {
            Input(new(NextPage: true));
        }
        Check(runtime.DescriptionOffset == Math.Max(0, runtime.Description.Length - StatusMenuLayout.BiographyLines), "Resized biography can scroll to its final line");
        for (int category = 35; category <= 44; category++)
        {
            Fits(GameText.Decode(messages[category].Span), small, 64, 32, true);
        }

        var empty = LegacySlotMetadata.Empty(messages[361].Span);
        string[] names = ["  Порт Синоп  ", "Деревня Свободный Амлет", "  Древние руины"];
        var slots = names.Select((name, i) => new SaveSlotInfo(i, SaveSlotStatus.Native,
            empty.WithCurrent(GameText.Encoding.GetBytes(name), GameText.Encoding.GetBytes(name), 400, 0, 1, 13, []), new(1450, 3, 14))).ToArray();
        byte[][] before = slots.Select(s => s.Metadata.Encode()).ToArray();
        var saves = new SaveLoadMenuRenderer(etc, small, large, messages);
        var savePlan = saves.BuildPlan(new(SaveLoadMenuKind.Load, 0, false, 0, false, 0, 0), slots);
        for (int i = 0; i < slots.Length; i++)
        {
            var title = savePlan.Single(p => p.Primitive == SaveMenuPrimitive.Text && p.X == 140 && p.Y == 99 + i * 48);
            Check(title.Text == title.Text.Trim().ToUpperInvariant() && title.A == 280, "Save names share left edge, uppercase and reserved level space");
            Check(before[i].SequenceEqual(slots[i].Metadata.Encode()), "Display formatting preserves save bytes");
        }
        Array.Fill(pixels, (ushort)0x2104);
        saves.Draw(savePlan, pixels);
        Capture("saves");

        var actions = new ActionMenuRenderer(etc, small);
        var prompt = new ActionMenuFrame(144, 160, 9, 3, 0, 0, 0,
            [GameText.Encoding.GetBytes("  ВОЙТИ В ПОСЕЛЕНИЕ"), GameText.Encoding.GetBytes("     ОТМЕНИТЬ")]);
        Array.Fill(pixels, (ushort)0x2104);
        actions.Draw(prompt, pixels);
        ushort[] padded = pixels.ToArray();
        Array.Fill(pixels, (ushort)0x2104);
        actions.Draw(prompt with { Labels = prompt.Labels.Select(s => (ReadOnlyMemory<byte>)GameText.Encoding.GetBytes(GameText.Decode(s.Span).Trim())).ToArray() }, pixels);
        Check(pixels.SequenceEqual(padded), "Action labels ignore legacy indentation");
        Capture("town-prompt");

        var worldPrompt = new ActionMenuFrame(208, 128, 5, 5, 1, 0, 0,
            new[] { 275, 276, 277, 278 }.Select(i => messages[i]).ToArray());
        foreach (var label in worldPrompt.Labels)
        {
            Fits(GameText.Decode(label.Span).Trim(), small, 157, 32, true);
        }
        Array.Fill(pixels, (ushort)0x2104);
        actions.Draw(worldPrompt, pixels);
        Capture("world-actions");

        var hud = WorldHudRenderer.Load(root, small);
        state.Mode = 300;
        state.WorldY = 8;
        Array.Clear(pixels);
        hud.Draw(state, GameText.Encoding.GetBytes("Порт Синоп"), pixels);
        ushort[] mixed = pixels.ToArray();
        Array.Clear(pixels);
        hud.Draw(state, GameText.Encoding.GetBytes("ПОРТ СИНОП"), pixels);
        Check(pixels.SequenceEqual(mixed), "Location HUD always uppercase");
        Capture("location-hud");
        Array.Clear(pixels);
        hud.Draw(state, [], pixels);
        int[] titleColumns = Enumerable.Range(7, 16).SelectMany(y => Enumerable.Range(180, 280)
            .Where(x => pixels[y * 640 + x] != mixed[y * 640 + x])).ToArray();
        Check(titleColumns.Length > 0 && Math.Abs(titleColumns.Min() + titleColumns.Max() - 640) <= 3, "Location title ink is centered on the screen");
        Check(overflow.Count == 0, "Overflow: " + string.Join(" | ", overflow));
        return "Layout feedback: all 88 shop items, six biographies, status fields, save alignment/uppercase, HUD uppercase, action indentation, Unicode and panel clipping passed. Captures: " + output;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Layout feedback: " + message);
        }
    }
}
