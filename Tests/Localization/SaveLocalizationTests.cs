using System.Security.Cryptography;
using Atria.Game;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Localization;

internal static class SaveLocalizationTests
{
    public static void Verify(string root, SessionRuntimeSnapshot template, DungeonControllerSnapshot controller)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "probe", "save-localization-" + Guid.NewGuid().ToString("N"));
        var previous = GameText.Current;
        Directory.CreateDirectory(directory);
        try
        {
            var store = new NativeSaveStore(directory, root);
            var shops = LegacyShopProjection.DecodeAll(new byte[30 * 612]);
            var state = new GameState { Year = 1450, Month = 3, Day = 14 };
            var areas = new WorldAreaStore();
            var legacyBytes = new byte[LegacySaveImage.Size];
            "ATRIA-II SAVE FILE (c)Jamie system development\0"u8.CopyTo(legacyBytes);
            var legacy = LegacySaveImage.Parse(legacyBytes);

            // Deliberately unrecognizable captions prove names come from IDs, including old native contexts.
            LegacySlotMetadata Metadata(int mode, int area, int town) => LegacySlotMetadata.Empty([])
                .WithCurrent("obsolete caption"u8, "unused"u8, mode, area, town, 8, " [ПОЛЕ]"u8);
            void Write(int slot, int mode, LegacySlotMetadata metadata, SessionRuntimeSnapshot? runtime = null)
            {
                state.Mode = mode;
                var context = runtime is null ? new NativeRuntimeContext("fixture", 1, [1]) : SessionRuntimeCodec.Encode(runtime);
                store.WriteNative(slot, NativeSaveImage.Capture(state, mode, areas, shops, metadata, context));
            }

            Write(0, GameMode.World, Metadata(GameMode.World, 5, 1));
            Write(1, GameMode.Town, Metadata(GameMode.Town, 0, 32), template);
            var oldLegacy = legacy.WithSavedMode(GameMode.World).WithSlotMetadata(Metadata(GameMode.World, 6, 1));
            File.WriteAllBytes(store.GetSlotPath(2), oldLegacy.Encode());

            using var maps = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
            var expectedDungeons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["AED00"] = 173, ["AED01"] = 187,
                ["ED00"] = 185, ["ED01"] = 186, ["ED02"] = 187,
                ["ED03"] = 188, ["ED04"] = 189, ["ED05"] = 190
            };
            for (int i = 1; i <= 12; i++) expectedDungeons.Add($"MD{i}", 172 + i);
            foreach (var entry in expectedDungeons)
            {
                var map = AmfMap.Parse(maps.ReadMember(maps.FindResource(System.Text.Encoding.ASCII.GetBytes(entry.Key + ".amf"))));
                Check(LocationTitle.Dungeon(map).MessageId == entry.Value, "real dungeon title ID: " + entry.Key);
            }
            var nonTitleMap = AmfMap.Parse(maps.ReadMember(maps.FindResource("ED06.amf"u8)));
            try
            {
                LocationTitle.Dungeon(nonTitleMap);
                throw new InvalidOperationException("Dormant ED06 must not use the torch-loss message as a title.");
            }
            catch (InvalidDataException) { }

            var dungeonMap = WorldMapContext.Parse(maps.ReadMember(maps.FindResource("MD3.amf"u8)));
            _ = new DungeonMap(dungeonMap);
            var mapSnapshot = new SessionMapSnapshot("MD3.amf"u8.ToArray(), dungeonMap.GetHeader(), dungeonMap.GetTiles());
            var dungeonSnapshot = template with
            {
                SavedMode = GameMode.Dungeon,
                ExactResume = true,
                CurrentMap = mapSnapshot,
                CurrentMapKind = 2,
                Location = null,
                Town = new TownController().CaptureRuntime(),
                ScriptMember = "MD3.scr"u8.ToArray(),
                Dungeon = new(mapSnapshot, 0, "MD3.scr"u8.ToArray(), 0, 0, 0, controller, default, [], [], [], [], 0, 0, 0, 0, 0, true),
                SaveReason = "Перед боем"
            };
            Write(3, GameMode.Dungeon, Metadata(GameMode.Dungeon, 0, 1), dungeonSnapshot);
            Write(4, GameMode.Town, Metadata(GameMode.Town, 0, 32), template with { SaveReason = "Пасля бою" });
            Write(5, GameMode.Town, Metadata(GameMode.Town, 0, 32), template with { SaveReason = AutosaveReason.QuestComplete });
            Write(6, GameMode.Town, Metadata(GameMode.Town, 0, 32), template with { SaveReason = "Custom historical caption" });
            File.WriteAllBytes(store.GetSlotPath(7), legacy.WithSavedMode(GameMode.Town)
                .WithSlotMetadata(Metadata(GameMode.Town, 0, 32)).Encode());

            var hashes = Directory.GetFiles(directory, "*.sav").ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
            var cached = store.InspectLibrary(false).OrderBy(row => row.Index).ToArray();
            Check(cached.All(row => row.Status is SaveSlotStatus.Native or SaveSlotStatus.LegacyUnverified), "old and new saves inspect successfully");
            Check(cached[3].Location == new LocationTitle(175), "old dungeon save uses AMF identity");
            Check(cached[3].Reason == AutosaveReason.BeforeBattle && cached[4].Reason == AutosaveReason.AfterBattle
                && cached[5].Reason == AutosaveReason.QuestComplete, "old captions migrate and new reason IDs survive serialization");
            Check(cached[6].Reason == "Custom historical caption", "unknown historical caption preserved");

            var messages = GameText.Messages(LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
            var small = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
            var large = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false);
            using var sprites = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
            var renderer = new SaveLoadMenuRenderer(SpriteFile.Decode(sprites.ReadMember(sprites.FindResource("ETC"u8))), small, large, messages);
            string localization = Path.GetFullPath(Path.Combine(root, "..", "Localization"));
            foreach (string language in new[] { "be-BY", "ru-RU", "be-BY" })
            {
                GameText.Use(new LocalizationCatalog(localization, language));
                string suffix = GameText.Get("messages.309");
                Check(cached[0].Location!.Value.Text == GameText.Get("messages.303") + " " + suffix, "world save separates title and suffix");
                Check(cached[1].Location!.Value.Text == GameText.Get("messages.151"), "town save uses town ID");
                Check(cached[2].Location!.Value.Text == GameText.Get("messages.304") + " " + suffix, "original legacy world save uses area ID");
                Check(cached[3].Location!.Value.Text == (language == "be-BY" ? "Радасць" : "Радость"), "dungeon translation is not old raw name");
                Check(cached[7].Location == cached[1].Location, "original legacy town save uses the same ID as native saves");
                Check(ReferenceEquals(cached[3], store.InspectLibrary(false).Single(row => row.Index == 3)), "metadata cache remains valid across language changes");
                for (int i = 0; i < cached.Length; i++)
                {
                    var frame = new SaveLoadMenuFrame(SaveLoadMenuKind.Load, 0, false, 0, false, 0, 0);
                    var plan = renderer.BuildPlan(frame, [cached[i]]);
                    Check(plan.Any(op => op.X == 140 && op.Text == cached[i].Location!.Value.Text.ToUpperInvariant()), "save renderer resolves cached ID in active language");
                    if (cached[i].Reason.Length > 0)
                        Check(plan.Any(op => op.Y == 438 && op.Text.EndsWith(AutosaveReason.Text(cached[i].Reason))), "reason renderer uses active language");
                }
            }

            var loader = new SessionSnapshotLoader(root, new GameState(), new LegacyRandom(1), small,
                LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))),
                ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT"))), _ => { });
            var saved = store.ReadSlot(3)!.Native!;
            Check(loader.Prepare(saved.Context, saved.State).DungeonTitle == new LocationTitle(175), "restored dungeon retains stable HUD title");
            foreach (var file in hashes)
                Check(SHA256.HashData(File.ReadAllBytes(file.Key)).AsSpan().SequenceEqual(file.Value), "inspecting/translating never rewrites saves");
            Check(AutosaveReason.Text("Custom historical caption") == "Custom historical caption", "unknown caption display is lossless");
            foreach (var (russian, belarusian, id) in new[]
            {
                ("Перед боем", "Перад боем", AutosaveReason.BeforeBattle),
                ("После боя", "Пасля бою", AutosaveReason.AfterBattle),
                ("Задание завершено", "Заданне завершана", AutosaveReason.QuestComplete),
                ("Переход между локациями", "Пераход паміж лакацыямі", AutosaveReason.Location),
                ("Новая область карты", "Новая вобласць мапы", AutosaveReason.WorldArea)
            })
            {
                Check(AutosaveReason.FromSaved(russian) == id && AutosaveReason.FromSaved(belarusian) == id
                    && AutosaveReason.FromSaved(id) == id, "all historical reasons migrate to stable IDs");
            }
            Console.WriteLine("Save localization: 20 dungeon resources, native/legacy identities, reason migration, cached ru/be switching, dungeon reload and unchanged saves passed.");
        }
        finally
        {
            GameText.Use(previous);
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Save localization: " + message);
    }
}
