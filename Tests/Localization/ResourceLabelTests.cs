using System.Text;
using System.Text.Json;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Script;
using Atria.Resources;

namespace Atria.Tests.Localization;

internal static class ResourceLabelTests
{
    public static string Run(string root)
    {
        var previous = GameText.Current;
        string temporary = Path.Combine(AppContext.BaseDirectory, "probe", "resource-labels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var originalStream = typeof(ResourceLabelTests).Assembly.GetManifestResourceStream("Atria.Tests.OriginalNames.json")!;
            var original = JsonSerializer.Deserialize<Dictionary<string, string>>(originalStream)!;
            int sceneCount = 0, peopleCount = 0;
            using var scenes = BinderArchive.Open(Path.Combine(root, "AT2SOD.000"));
            foreach (var entry in scenes.Entries)
            {
                byte[] raw = scenes.ReadMember(entry.Index);
                if (!SodV2.HasV2Signature(raw)) continue;
                var scene = SodV2.Parse(raw);
                for (int i = 0; i < scene.Count; i++)
                {
                    string name = TextResourceSchema.Normalize(GameText.Decode(scene.GetObject(i).GetString(4).Span));
                    if (name.Length == 0) continue;
                    string? key = ResourceLabelKeys.Scene(entry.Name.ToLowerInvariant() + ".sod", i);
                    Check(key is not null && TextResourceSchema.Normalize(original[key]) == name, "scene identity matches source resource: " + entry.Name + "/" + i);
                    sceneCount++;
                }
            }

            using var scripts = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
            foreach (var entry in scripts.Entries)
            {
                var program = ScriptProgram.Parse(entry.Name, scripts.ReadMember(entry.Index));
                for (int i = 0; i < program.Tokens.Count; i++)
                {
                    if (program.Tokens[i].Kind != ScriptTokenKind.Identifier || program.Tokens[i].Text != "people_name") continue;
                    var arguments = new List<ScriptToken>();
                    for (int j = i + 1; program.Tokens[j].Text != ")"; j++)
                        if (program.Tokens[j].Kind == ScriptTokenKind.String) arguments.Add(program.Tokens[j]);
                    Check(arguments.Count % 3 == 0, "literal people triples");
                    for (int j = 0; j < arguments.Count; j += 3)
                    {
                        string? key = ResourceLabelKeys.Person(Encoding.ASCII.GetBytes(entry.Name.ToLowerInvariant() + ".scr"), arguments[j + 2].RawBytes);
                        string name = TextResourceSchema.Normalize(GameText.Decode(arguments[j].RawBytes));
                        Check(key is not null && TextResourceSchema.Normalize(original[key]) == name, "person identity matches source declaration: " + entry.Name);
                        peopleCount++;
                    }
                }
            }
            Check(sceneCount == 134 && peopleCount == 174, "complete shipped label corpus covered");
            using (var keys = GameData.Open("ResourceLabelKeys"))
            {
                foreach (string language in new[] { "ru-RU", "be-BY" })
                {
                    var catalog = new LocalizationCatalog(Path.GetFullPath(Path.Combine(root, "..", "Localization")), language);
                    foreach (var group in keys.RootElement.EnumerateObject())
                        foreach (var row in group.Value.EnumerateObject())
                            Check(catalog.TryGet(row.Value.GetString()!, out string value) && value.Length > 0, "binding has a translation: " + row.Name);
                }
            }

            string sceneKey = ResourceLabelKeys.Scene("01MERCEN", 0)!;
            string personKey = ResourceLabelKeys.Person("32_LIBRA"u8, "black_talk"u8)!;
            foreach (string language in new[] { "ru-RU", "be-BY" })
            {
                string folder = Path.Combine(temporary, language);
                Directory.CreateDirectory(folder);
                foreach (var file in LocalizationCatalog.Defaults)
                {
                    var values = new Dictionary<string, string>(file.Value);
                    if (values.ContainsKey(sceneKey)) values[sceneKey] = language == "be-BY" ? "Праверачны будынак" : "Проверочное здание";
                    if (values.ContainsKey(personKey)) values[personKey] = language == "be-BY" ? "Праверачны суразмоўца" : "Проверочный собеседник";
                    if (values.ContainsKey("messages.361")) values["messages.361"] = language == "be-BY" ? "Вольнае месца" : "Свободное место";
                    File.WriteAllText(Path.Combine(folder, file.Key), JsonSerializer.Serialize(values));
                }
            }

            // Existing save schema stores handler identity, even when its display name is stale or edited.
            var state = new GameState { People = [new("obsolete caption"u8.ToArray(), "old name"u8.ToArray(), "black_talk"u8.ToArray())] };
            byte[] savedState = NativeGameStateCodec.Encode(state);
            var restored = NativeGameStateCodec.Decode(savedState);
            var messages = GameText.Messages(LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
            var talk = InteriorTalkMenu.Build(restored, "32_libra.scr"u8.ToArray(), null, messages);
            Check(talk.Choices.Single().TextKey == personKey, "old saved handler reconstructs the translation key");
            Check(talk.Select(0).Function.Span.SequenceEqual("black_talk"u8) && talk.Select(0).ScriptMember.Span.SequenceEqual("32_libra.scr"u8), "translation preserves script dispatch");

            var town = TownSceneAssets.Load(root, "01mercen.sod"u8);
            byte[] altered = scenes.ReadMember(scenes.FindResource("01MERCEN"u8));
            altered.AsSpan(SodV2.HeaderSize + 120 + 4 * 50, 50).Clear();
            "obsolete caption"u8.CopyTo(altered.AsSpan(SodV2.HeaderSize + 120 + 4 * 50));
            var alteredTown = town with { Scene = SodV2.Parse(altered) };
            using var sprites = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
            using var animations = BinderArchive.Open(Path.Combine(root, "ANI.000"));
            var etc = SpriteFile.Decode(sprites.ReadMember(sprites.FindResource("ETC"u8)));
            var effects = AnimationFile.Decode(animations.ReadMember(animations.FindResource("CON_EFF"u8)));
            var effectSprites = SpriteFile.Decode(sprites.ReadMember(sprites.FindResource(effects.SpriteReference)));
            var small = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
            var large = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false);
            var renderer = new SaveLoadMenuRenderer(etc, small, large, messages);
            var empty = new SaveSlotInfo(0, SaveSlotStatus.Empty, LegacySlotMetadata.Empty("obsolete caption"u8), default);
            byte[] emptyBefore = empty.Metadata.Encode();
            var frame = new TownFrame(0, false, false, true, 0, 0, 0, false, false, 0, []);
            ushort[] pixels = new ushort[640 * 480], alteredPixels = new ushort[640 * 480];
            ushort[]? russianPixels = null;
            foreach (string language in new[] { "ru-RU", "be-BY", "ru-RU" })
            {
                GameText.Use(new LocalizationCatalog(temporary, language));
                Check(GameText.Decode(talk.Choices[0].Label.Span) == GameText.Get(personKey), "cached talk menu follows language and edited JSON");
                TownComposer.Draw(town, frame, 1, etc, small, effects, effectSprites, pixels);
                TownComposer.Draw(alteredTown, frame, 1, etc, small, effects, effectSprites, alteredPixels);
                Check(pixels.AsSpan().SequenceEqual(alteredPixels), "town label rendering ignores changed source text");
                if (russianPixels is null) russianPixels = pixels.ToArray();
                else Check(pixels.SequenceEqual(russianPixels) == (language == "ru-RU"), "town label raster switches languages and returns exactly");
                foreach (var kind in new[] { SaveLoadMenuKind.Load, SaveLoadMenuKind.Save })
                {
                    var plan = renderer.BuildPlan(new(kind, 0, false, 0, false, 0, 0), [empty]);
                    Check(plan.Any(op => op.X == 140 && op.Y == 99 && op.Text == GameText.Get("messages.361").ToUpperInvariant()), "empty slot ignores historical caption");
                }
            }
            Check(savedState.AsSpan().SequenceEqual(NativeGameStateCodec.Encode(restored)), "display and language changes preserve saved state bytes");
            Check(emptyBefore.AsSpan().SequenceEqual(empty.Metadata.Encode()), "empty slot metadata stays unchanged");
            Check(AutosaveReason.FromSaved("Перед боем") == AutosaveReason.BeforeBattle, "historical reason migration remains available");
            return "Resource labels: all134 scene labels/174 people declarations, edited JSON, stale saved names, script dispatch, live-language raster and empty slots passed.";
        }
        finally
        {
            GameText.Use(previous);
            foreach (string folder in Directory.GetDirectories(temporary))
            {
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
            Directory.Delete(temporary);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Resource label IDs: " + message);
    }
}
