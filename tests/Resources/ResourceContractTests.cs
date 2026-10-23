using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Atria.Resources;

namespace Atria.Tests.Resources;

public static class ResourceContractTests
{
    public static string Run(string originalRoot, string musicRoot)
    {
        int archives = 0, members = 0, sounds = 0, bitmaps = 0, maps = 0, scenes = 0, variants = 0, infos = 0;
        foreach (string path in Directory.EnumerateFiles(originalRoot, "*.000"))
        {
            using BinderArchive archive = BinderArchive.Open(path);
            archives++;
            string name = Path.GetFileName(path).ToUpperInvariant();
            foreach (BinderEntry entry in archive.Entries)
            {
                members++;
                Check(archive.FindExact(entry.RawName.Span) == entry.Index, "Binder exact raw-key lookup.");
                byte[] bytes = archive.ReadMember(entry.Index);
                Check(bytes.Length == entry.Length, "Binder explicit member length.");
                byte[] reference = System.Text.Encoding.ASCII.GetBytes(entry.Name.ToLowerInvariant() + ".scr");
                Check(archive.FindResource(reference) == entry.Index, "Recovered Binder filename normalization.");
                if (name is "WAV.000" or "AT2WAV.000")
                {
                    PcmWave wave = PcmWave.Parse(bytes);
                    Check(wave.Info.Channels == 1 && wave.Info.SampleRate == 22050 && wave.Info.BitsPerSample == 16, "Effect PCM format.");
                    Check(wave.Samples.Length == wave.Info.DataLength, "Effect sample range.");
                    using MemoryStream stream = new(bytes, false);
                    Check(PcmWaveInfo.Read(stream) == wave.Info, "Stream/memory PCM metadata agreement.");
                    sounds++;
                }
                if (name is "BMP.000" or "AT2BMP.000")
                {
                    BmpImage image = BmpImage.Decode(bytes);
                    Check(image.Width * image.Height == image.Pixels.Length, "BMP decoded dimensions.");
                    bitmaps++;
                }
                if (name is "SOD.000" or "AT2SOD.000")
                {
                    if (entry.Name == "FOREST00" && name == "SOD.000")
                    {
                        Reject<NotSupportedException>(() => SodV2.Parse(bytes));
                        variants++;
                    }
                    else { _ = SodV2.Parse(bytes); scenes++; }
                }
                if (name == "AT2AMF.000")
                {
                    AmfMap map = AmfMap.Parse(bytes);
                    Check(map.Width == 10 && map.Height == 10 && map.EmbeddedScene is not null, "AMF full corpus dimensions/inline scene.");
                    if (entry.Name == "GROUND3")
                    {
                        Check(map.GetTile(3, 3).FirstEvent == new MapEventCondition(63, 1), "Quest first encounter contract.");
                        Check(map.GetTile(6, 3).FirstEvent == new MapEventCondition(162, 1), "Quest missing stage162 contract.");
                        Check(map.GetTile(2, 6).FirstEvent == new MapEventCondition(161, 1), "Quest missing stage161 contract.");
                    }
                    maps++;
                }
                if (name == "INF.000")
                {
                    ActorInfo info = ActorInfo.Parse(bytes);
                    Check(info.Records[0].Tag == (byte)'R' && info.Records[1].Tag == (byte)'T' && info.Records[^1].Tag == (byte)'E', "INF recovered tag order.");
                    using BinderArchive ani = BinderArchive.Open(Path.Combine(originalRoot, "ANI.000"));
                    Check(ani.FindResource(info.Records[0].AnimationReference.Span) >= 0, "INF animation reference resolves.");
                    infos++;
                }
            }
        }
        Check(archives == 12 && sounds == 205 && bitmaps == 184 && scenes == 560 && variants == 1 && maps == 29 && infos == 72,
            "Complete supplied archive corpus counts.");

        DialogueCatalog dialogue = DialogueCatalog.Load(Path.Combine(originalRoot, "roland.idx"), Path.Combine(originalRoot, "ROLAND.DAT"));
        Check(dialogue.Entries.Count == 3616, "Dialogue count.");
        long textLength = 0;
        for (int i = 0; i < dialogue.Entries.Count; i++)
        {
            DialogueEntry entry = dialogue.Entries[i];
            Check(entry.Offset == textLength, "Supplied dialogue ranges contiguous.");
            Check(dialogue.TryGetText(entry.RawKey.Span, out ReadOnlyMemory<byte> text) && text.Length == entry.Length, "All raw CP949 dialogue keys resolve.");
            textLength += entry.Length;
        }
        Check(textLength == 340828, "Dialogue data fully covered.");
        Check(!dialogue.TryGetText("nonexistent"u8, out _), "Missing dialogue key reports failure.");

        for (int track = 2; track <= 28; track++)
        {
            using FileStream stream = File.OpenRead(Path.Combine(musicRoot, $"Track{track:00}.wav"));
            PcmWaveInfo info = PcmWaveInfo.Read(stream);
            Check(info.Channels == 2 && info.SampleRate == 44100 && info.BitsPerSample == 16, "CD music PCM format.");
            Check(info.DataOffset + info.DataLength <= stream.Length, "Music bounded sample range.");
        }

        TestScriptOverlap(originalRoot);
        TestTigerCorrection(originalRoot);
        TestMalformedInputs(originalRoot);
        TestBmpOrientationAndPalette();
        TestDatTables(originalRoot);
        return $"Resources: {archives} archives, {members} members, {sounds} PCM effects, {bitmaps} BMPs, {scenes} SODv2 scenes, {maps} maps, {infos} INF records, 3616 dialogues, 27 music tracks; FOREST00 explicitly unsupported; DAT/corruption/overlap/orientation checks passed.";
    }

    private static void TestScriptOverlap(string root)
    {
        using BinderArchive archive = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
        int library = archive.FindResource("32_libra.scr"u8);
        int shop = archive.FindResource("32_shop.scr"u8);
        byte[] a = archive.ReadMember(library), b = archive.ReadMember(shop);
        Check(a.Length == 2063 && b.Length == 377 && a.AsSpan(a.Length - 6).SequenceEqual(b.AsSpan(0, 6)), "Known six-byte overlap preserved per member.");
        a[^1] = 0;
        Check(archive.ReadMember(shop).AsSpan().SequenceEqual(b), "Returned member buffers independent and source read-only.");
    }

    private static void TestTigerCorrection(string root)
    {
        using BinderArchive archive = BinderArchive.Open(Path.Combine(root, "AT2SOD.000"));
        byte[] source = archive.ReadMember(archive.FindExact("32TIGER"u8));
        byte[] corrected = KnownResourceCorrections.CorrectTigerLibraryReference(source);
        int offset = KnownResourceCorrections.TigerLibraryScriptOffset;
        Check(corrected.AsSpan(0, offset).SequenceEqual(source.AsSpan(0, offset)) &&
            corrected.AsSpan(offset + 12).SequenceEqual(source.AsSpan(offset + 12)), "Tiger correction preserves all neighboring fields.");
        Check(SodV2.Parse(corrected).GetObject(4).GetString(1).Span.SequenceEqual("32_libra.scr"u8), "Tiger library reference repaired.");
        Check(SodV2.Parse(source).GetObject(4).GetString(1).Span.SequenceEqual("32_libar.scr"u8), "Original Tiger buffer unchanged.");
        source[0] ^= 1;
        Reject<InvalidDataException>(() => KnownResourceCorrections.CorrectTigerLibraryReference(source));
    }

    private static void TestMalformedInputs(string root)
    {
        byte[] badIndex = File.ReadAllBytes(Path.Combine(root, "roland.idx"));
        byte[] text = File.ReadAllBytes(Path.Combine(root, "ROLAND.DAT"));
        BinaryPrimitives.WriteUInt32LittleEndian(badIndex.AsSpan(52), uint.MaxValue);
        Reject<InvalidDataException>(() => DialogueCatalog.Parse(badIndex, text));

        using BinderArchive wav = BinderArchive.Open(Path.Combine(root, "WAV.000"));
        byte[] wave = wav.ReadMember(0);
        Reject<InvalidDataException>(() => PcmWave.Parse(wave.AsMemory(0, wave.Length - 1)));
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(4), uint.MaxValue);
        Reject<InvalidDataException>(() => PcmWave.Parse(wave));

        using BinderArchive amf = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
        byte[] map = amf.ReadMember(0);
        BinaryPrimitives.WriteInt32LittleEndian(map.AsSpan(13), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(map.AsSpan(17), int.MaxValue);
        Reject<InvalidDataException>(() => AmfMap.Parse(map));

        using BinderArchive sod = BinderArchive.Open(Path.Combine(root, "AT2SOD.000"));
        byte[] scene = sod.ReadMember(0);
        BinaryPrimitives.WriteUInt32LittleEndian(scene.AsSpan(30), uint.MaxValue);
        Reject<InvalidDataException>(() => SodV2.Parse(scene));

        using BinderArchive inf = BinderArchive.Open(Path.Combine(root, "INF.000"));
        byte[] actor = inf.ReadMember(0);
        Reject<InvalidDataException>(() => ActorInfo.Parse(actor.AsMemory(0, actor.Length - 1)));
        byte[] badSound = new byte[45];
        actor.AsSpan(0, 39).CopyTo(badSound);
        badSound[39] = (byte)'X';
        BinaryPrimitives.WriteUInt32LittleEndian(badSound.AsSpan(40), uint.MaxValue);
        badSound[44] = (byte)'E';
        Reject<InvalidDataException>(() => ActorInfo.Parse(badSound));
        actor[0] = (byte)'?';
        Reject<InvalidDataException>(() => ActorInfo.Parse(actor));
        Reject<InvalidDataException>(() => ActorInfo.Parse(new byte[] { (byte)'X',255,255,255,255,(byte)'E' }));

        // Deliberately corrupt a temporary archive; never write fixtures into originalRoot.
        string scratch = Path.Combine(Path.GetTempPath(), $"atria-binder-{Guid.NewGuid():N}.000");
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, "SCRIPT.000"));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), uint.MaxValue);
            File.WriteAllBytes(scratch, bytes);
            Reject<InvalidDataException>(() => { using BinderArchive unused = BinderArchive.Open(scratch); });
            File.WriteAllBytes(scratch, bytes.AsSpan(0, 20).ToArray());
            Reject<EndOfStreamException>(() => { using BinderArchive unused = BinderArchive.Open(scratch); });
        }
        finally { if (File.Exists(scratch)) File.Delete(scratch); }
    }

    private static void TestBmpOrientationAndPalette()
    {
        // 1x2 bottom-up 24-bit image: blue stored first, red stored second; each row has one padding byte.
        byte[] bmp = MakeBmp(1, 2, 24, 54, 8);
        bmp[54] = 255;
        bmp[60] = 255;
        BmpImage image = BmpImage.Decode(bmp);
        Check(image.Pixels.SequenceEqual(new uint[] { 0xffff0000, 0xff0000ff }), "BMP bottom-up, padding and BGRA channels.");
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -2);
        image = BmpImage.Decode(bmp);
        Check(image.Pixels.SequenceEqual(new uint[] { 0xff0000ff, 0xffff0000 }), "BMP top-down orientation.");
        Reject<InvalidDataException>(() => BmpImage.Decode(bmp.AsSpan(0, bmp.Length - 1)));

        byte[] indexed = MakeBmp(1, 1, 8, 62, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(indexed.AsSpan(46), 2);
        indexed[59] = 255; // Palette[1] green; reserved byte deliberately nonzero.
        indexed[61] = 11;
        indexed[62] = 1;
        Check(BmpImage.Decode(indexed).Pixels[0] == 0xff00ff00, "BMP indexed palette/reserved-byte behavior.");
        indexed[62] = 2;
        Reject<InvalidDataException>(() => BmpImage.Decode(indexed));
    }

    private static void TestDatTables(string root)
    {
        byte[] items = File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT"));
        ItemCatalog catalog = ItemCatalog.Parse(items);
        Check(catalog.GetItem(0).Category == 0 && catalog.GetItem(0).PurchasePrice == 10, "ATITEM independently observed first category/price.");
        Check(catalog.GetItem(0).Name.Span.SequenceEqual(Convert.FromHexString("20cac8cdc6c0cb2020202020202020")), "ATITEM CP1251 name bytes retained.");
        for (int i = 0; i < ItemCatalog.Count; i++)
        {
            _ = catalog.GetItem(i).GetInteger(16);
            for (int line = 0; line < 5; line++) _ = catalog.GetDescriptionLine(i, line);
        }
        Reject<InvalidDataException>(() => ItemCatalog.Parse(items.AsMemory(0, 35903)));
        Reject<ArgumentOutOfRangeException>(() => catalog.GetItem(88));

        byte[] shopBytes = File.ReadAllBytes(Path.Combine(root, "SHOP.DAT"));
        ShopCatalog shops = ShopCatalog.Parse(shopBytes);
        Check(shops.GetGroup(0, 0).Count == 2 && shops.GetGroup(0, 0).GetItemId(0) == 0 &&
            shops.GetGroup(0, 0).GetItemId(1) == 18 && shops.GetGroup(0, 0).GetQuantity(0) == 5,
            "SHOP independent first group inventory fixture.");
        for (int shop = 0; shop < ShopCatalog.Count; shop++)
            for (int group = 0; group < 3; group++)
            {
                ShopGroup stock = shops.GetGroup(shop, group);
                for (int i = 0; i < stock.Count; i++)
                    Check(stock.GetItemId(i) >= -1 && stock.GetItemId(i) < ItemCatalog.Count && stock.GetQuantity(i) >= 0,
                        "Supplied shop slots retain item IDs or the original empty-slot sentinel.");
            }
        BinaryPrimitives.WriteInt32LittleEndian(shopBytes, 26);
        Reject<InvalidDataException>(() => ShopCatalog.Parse(shopBytes));

        QuestJournalCatalog journal = QuestJournalCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "DANGEL.DAT")));
        for (int i = 0; i < QuestJournalCatalog.Count; i++)
        {
            _ = journal.GetHeading(i, 0); _ = journal.GetHeading(i, 1);
            for (int line = 0; line < 5; line++) _ = journal.GetDescriptionLine(i, line);
        }
        byte[] intros = File.ReadAllBytes(Path.Combine(root, "CHARINTR.DAT"));
        for (int character = 0; character < 6; character++)
            for (int line = 0; line < 10; line++) _ = DatTables.GetCharacterIntroductionLine(intros, character, line);
        byte[] extras = File.ReadAllBytes(Path.Combine(root, "ITEM_EX.DAT"));
        for (int item = 0; item < 88; item++)
            for (int line = 0; line < 3; line++) _ = DatTables.GetItemExtraLine(extras, item, line);
        byte[] credits = File.ReadAllBytes(Path.Combine(root, "CREDIT.DAT"));
        for (int line = 0; line < 400; line++) _ = DatTables.GetCreditLine(credits, line);
        byte[] endings = File.ReadAllBytes(Path.Combine(root, "END.DAT"));
        for (int line = 0; line < 200; line++) _ = DatTables.GetEndingLine(endings, 1, line);
        _ = DatTables.GetEventItemDescriptionLine(File.ReadAllBytes(Path.Combine(root, "EITEM.DAT")), 0, 4);
        Reject<ArgumentOutOfRangeException>(() => DatTables.GetEndingLine(endings, 0, 0));

        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        Check(messages.Count == 372 && messages[0].Span.SequenceEqual(Convert.FromHexString("d0cecbc0cdc4")), "Original372-message load and CP1251 first text.");
        var battle = LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root, "BATTLE.DAT")));
        Check(battle.Count == 54 && battle[5].IsEmpty && !battle[0].IsEmpty && !battle[49].IsEmpty && battle[50].IsEmpty,
            "Battle33 records placed into original sparse54 slots.");
        Reject<InvalidDataException>(() => LegacyTextTables.ParseMessages("short\r\n"u8));
    }

    private static byte[] MakeBmp(int width, int height, ushort bits, int offset, int pixels)
    {
        byte[] bytes = new byte[offset + pixels];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), offset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), bits);
        return bytes;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} for malformed/unsupported input.");
    }
}
