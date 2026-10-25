using System.Text;
using System.Text.Json;
using Atria.Game.Controls;
using Atria.Game.Credits;
using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Game.Termination;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Localization;

public static class LocalizationTests
{
    public static string Run(string resources)
    {
        _ = TextFlowTests.Run(resources);
        var previous = GameText.Current;
        string temporary = Path.Combine(Path.GetTempPath(), "atria-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, LocalizationCatalog.BaseLanguage));
        try
        {
            string shipped = Path.Combine(AppContext.BaseDirectory, "assets", "localization");
            Check(LocalizationCatalog.Validate(shipped).Count == 0, "shipped editable JSON validates");
            foreach (var file in LocalizationCatalog.Defaults)
            {
                Write(file.Key, file.Value);
                foreach (var entry in file.Value.Where(e => e.Key.StartsWith("ui.", StringComparison.Ordinal)))
                {
                    _ = TextTemplate.Parameters(entry.Value);
                }
            }

            var baseline = new LocalizationCatalog(temporary);
            Check(baseline.Issues.Count == 0, "baseline has no warnings");
            Check(TextTemplate.Format("{{{name}}} {count}", ("count", 2), ("name", "test")) == "{test} 2", "named reorder and escaped braces");
            GameText.Use(baseline);
            Check(GameText.Format("ui.persistence.failed", ("message", "文件.sav")).Contains("文件.sav"), "Unicode OS errors cannot crash bitmap error display");
            byte[] Read(string name) => File.ReadAllBytes(Path.Combine(resources, name));
            var items = ItemCatalog.Parse(Read("ATITEM.DAT"));
            var journal = QuestJournalCatalog.Parse(Read("DANGEL.DAT"));
            var messages = LegacyTextTables.ParseMessages(Read("TEXTmsg.dat"));
            var dialogue = DialogueCatalog.Load(Path.Combine(resources, "ROLAND.IDX"), Path.Combine(resources, "ROLAND.DAT"));
            byte[] rawDialogue = dialogue.GetText(0).ToArray();
            byte[] rawItem = items.GetItem(0).Raw.ToArray();
            var indexed = new ScriptEffect(ScriptEffectKind.DialogueKey,
                new ScriptCall("TEST", "dialogue_script2", 0, [new(-1), new(-1), new(dialogue.Entries[0].RawKey)]));
            var before = DialogueRequest.Create(indexed, dialogue);
            Check(GameText.ItemName(items, 0).Span.SequenceEqual(items.GetItem(0).Name.Span), "unchanged legacy bytes remain exact");

            var font = BitmapFont.DecodeEng(Read("eng1.fnt"), true);
            ushort[] beforePixels = new ushort[640 * 480];
            ControlPanel.Text(font, beforePixels, MoveCatalog.Name(0), 20, 20, 0xffff);
            Edit("ui.json", "ui.characters.roland", "Проверка");
            Edit("ui.json", "ui.combat.playerCharacter", "{character}: игрок {player}");
            Edit("dialogue.json", "dialogue.0000.text", "Исправленная реплика");
            Edit("scripts.json", "scripts.AED00.1085.text", "Прямая реплика");
            Edit("items.json", "items.00.name", "Новый предмет");
            Edit("items.json", "items.00.description", "Описание предмета");
            Edit("quests.json", "quests.000.description", "Описание задания");
            Edit("messages.json", "messages.119", "Новый город");
            Edit("credits.json", "credits.000.text", "Автор");
            Edit("endings.json", "endings.1.000.text", "Конец");
            var edited = new LocalizationCatalog(temporary);
            Check(edited.Issues.Count == 0, "valid edits accepted");
            Edit("validation.json", "ui.validation.unknownKey", "Unknown translation key.");
            GameText.Use(edited);
            Check(MoveCatalog.Name(0) == "Проверка", "same assembly reads edited character name");
            Check(GameText.Format("ui.combat.playerCharacter", ("player", 2), ("character", "Роланд")) == "Роланд: игрок 2", "placeholder reorder reaches UI");
            ushort[] afterPixels = new ushort[beforePixels.Length];
            ControlPanel.Text(font, afterPixels, MoveCatalog.Name(0), 20, 20, 0xffff);
            Check(!beforePixels.SequenceEqual(afterPixels), "edited label changes rendered pixels");
            var after = DialogueRequest.Create(indexed, dialogue);
            Check(after.Paragraph == "Исправленная реплика" && before.Lines.Count == after.Lines.Count, "indexed dialogue changes text without changing line/choice count");
            var direct = new ScriptEffect(ScriptEffectKind.DialogueText,
                new ScriptCall("AED00", "dialogue_script", 1085, [new(-1), new(-1), new("unchanged source"u8.ToArray())]));
            Check(DialogueRequest.Create(direct, dialogue).Paragraph == "Прямая реплика", "script-offset translation reaches dialogue");
            Check(Decode(GameText.ItemName(items, 0).Span) == "Новый предмет", "item name display");
            Check(StatusTextList.LoadDescription(1, 0, items, Read("EITEM.DAT"), journal) == "Описание предмета", "inventory description display");
            Check(StatusTextList.LoadDescription(4, 0, items, Read("EITEM.DAT"), journal) == "Описание задания", "quest description display");
            Check(Decode(GameText.Name(messages[119]).Span) == "Новый город", "raw saved location name maps to translated display");
            Check(CreditsText.Parse(Read("CREDIT.DAT")).Flow.Lines[0] == "Автор", "credits display");
            Check(EndingText.Parse(Read("END.DAT"), 1).Flow.Lines[0] == "Конец", "ending display");
            Check(rawItem.SequenceEqual(items.GetItem(0).Raw.ToArray()) && rawDialogue.SequenceEqual(dialogue.GetText(0).ToArray()), "raw item stats and dialogue data remain unchanged");
            Check(GameText.Messages(messages)[119].Span.SequenceEqual(GameText.Encoding.GetBytes("Новый город")) && !messages[119].Span.SequenceEqual(GameText.Encoding.GetBytes("Новый город")), "display view does not mutate source messages");

            CheckRejected("ui.combat.playerCharacter", "Игрок {player}");
            CheckRejected("ui.combat.playerCharacter", "Игрок {wrong}: {character}");
            CheckRejected("ui.combat.playerCharacter", "Игрок {player");
            Edit("ui.json", "ui.characters.roland", "中文");
            Check(new LocalizationCatalog(temporary).Get("ui.characters.roland") == "中文", "Unicode interface text accepted");
            Edit("ui.json", "ui.characters.roland", baseline.Get("ui.characters.roland"));
            Edit("menu.json", "ui.mainMenu.new", "新しいゲーム");
            GameText.Use(new LocalizationCatalog(temporary));
            ushort[] unicodeMenu = new ushort[640 * 480];
            Atria.Render.Scenes.MainMenuPresentation.DrawSelection(0, unicodeMenu, 640, 480, 640);

            Check(unicodeMenu.Any(p => p != 0), "Editable Unicode menu reaches native layout and raster");
            GameText.Use(edited);
            Edit("messages.json", "messages.119", "中文");
            Check(new LocalizationCatalog(temporary).Issues.Any(i => i.Key == "messages.119" && i.IsError), "Unmigrated byte-based displays reject Unicode rather than corrupting it");
            Edit("messages.json", "messages.119", "Новый город");
            CheckRejected("ui.characters.roland", "строка\nстрока");
            CheckRejected("ui.characters.roland", "строка\0");
            CheckRejected("ui.characters.roland", new string('x', 1025));
            Edit("dialogue.json", "dialogue.0000.text", new string('x', 16_385));
            var longDialogue = new LocalizationCatalog(temporary);
            Check(longDialogue.Issues.Any(i => i.IsError && i.Key == "dialogue.0000.text"), "oversized editable paragraph rejected");
            Edit("dialogue.json", "dialogue.0000.text", "Исправленная реплика");

            Write("ui.json", LocalizationCatalog.Defaults["ui.json"].Where(p => p.Key != "ui.characters.roland").ToDictionary());
            var missing = new LocalizationCatalog(temporary);
            Check(missing.Get("ui.characters.roland") == baseline.Get("ui.characters.roland") && missing.Issues.Any(i => i.Key == "ui.characters.roland" && !i.IsError), "missing key falls back with warning");
            File.WriteAllText(PathFor("ui.json"), "{\n  \"тест\": @\n}", Encoding.UTF8);
            var malformed = new LocalizationCatalog(temporary);
            var syntax = malformed.Issues.Single(i => i.File.EndsWith("ui.json", StringComparison.Ordinal));
            Check(syntax.IsError && syntax.Line == 2 && syntax.Column == 11, "JSON line and Unicode character column");
            Check(malformed.Get("ui.characters.roland") == baseline.Get("ui.characters.roland"), "malformed file uses built-in fallback");
            foreach (string json in new[] { "{\"ui.characters.roland\":\"A\",\"ui.characters.roland\":\"B\"}", "{\"ui.characters.roland\":42}", "[]" })
            {
                File.WriteAllText(PathFor("ui.json"), json);
                Check(new LocalizationCatalog(temporary).Issues.Any(i => i.IsError), "duplicate/type/root errors rejected");
            }
            Write("ui.json", LocalizationCatalog.Defaults["ui.json"]);
            var unknown = new Dictionary<string, string>(LocalizationCatalog.Defaults["ui.json"]) { ["ui.typo"] = "wrong" };
            Write("ui.json", unknown);
            Check(new LocalizationCatalog(temporary).Issues.Any(i => i.IsError && i.Key == "ui.typo" && i.Message == "Unknown translation key."), "validated diagnostic translations apply to errors");
            Write("ui.json", LocalizationCatalog.Defaults["ui.json"]);
            File.WriteAllText(PathFor("ui.json"), File.ReadAllText(PathFor("ui.json")), new UTF8Encoding(true));
            Check(new LocalizationCatalog(temporary).Issues.Count == 0, "Notepad UTF-8 BOM accepted");
            Directory.CreateDirectory(Path.Combine(temporary, "en-GB"));
            File.WriteAllText(Path.Combine(temporary, "en-GB", "ui.json"), "{\"ui.characters.roland\":\"Roland\"}");
            var partial = new LocalizationCatalog(temporary, "en-GB");
            Check(partial.Get("ui.characters.roland") == "Roland" && partial.Get("items.00.name") == "Новый предмет", "partial locale falls back through edited Russian to embedded text");
            Check(partial.Issues.Count > 0 && partial.Issues.All(i => !i.IsError), "partial locale missing files/keys are warnings");
            return $"localization: {LocalizationCatalog.Defaults.Values.Sum(f => f.Count)} entries; edits, render pixels, dialogue IDs, fallback, JSON positions/BOM, duplicates, placeholders and font limits passed";

            string PathFor(string file) => Path.Combine(temporary, LocalizationCatalog.BaseLanguage, file);
            void Write(string file, IReadOnlyDictionary<string, string> entries)
            {
                using var stream = File.Create(PathFor(file));
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                foreach (var entry in entries)
                {
                    writer.WriteString(entry.Key, entry.Value);
                }
                writer.WriteEndObject();
            }
            void Edit(string file, string key, string text)
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(PathFor(file)));
                var entries = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
                entries[key] = text;
                Write(file, entries);
            }
            void CheckRejected(string key, string value)
            {
                Edit("ui.json", key, value);
                var invalid = new LocalizationCatalog(temporary);
                Check(invalid.Issues.Any(i => i.IsError && i.Key == key) && invalid.Get(key) == baseline.Get(key), "invalid translation falls back: " + key);
                Edit("ui.json", key, baseline.Get(key));
            }
        }
        finally
        {
            GameText.Use(previous);
            Directory.Delete(temporary, true);
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return GameText.Encoding.GetString(end < 0 ? bytes : bytes[..end]);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Localization: " + message);
        }
    }
}
