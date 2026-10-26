using System.Text.Json;
using Atria.Game.Combat;
using Atria.Game.Controls;
using Atria.Game.Dialogue;
using Atria.Game.Settings;
using Atria.Game.State;
using Atria.Localization;
using Atria.Platform.Win32;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Localization;

public static class LiveLanguageTests
{
    public static string Run(string resources)
    {
        var previous = GameText.Current;
        string temporary = Path.Combine(AppContext.BaseDirectory, "probe", "live-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (string language in new[] { "ru-RU", "be-BY" })
            {
                string folder = Path.Combine(temporary, language);
                Directory.CreateDirectory(folder);
                foreach (var file in LocalizationCatalog.Defaults)
                {
                    var values = new Dictionary<string, string>(file.Value);
                    if (language == "be-BY")
                    {
                        Replace("ui.settings.title", "Налады");
                        Replace("messages.001", "Міст");
                        Replace("battle.018", "Паўза");
                        Replace("battle.003", "Новы ранг");
                        Replace("items.00.name", "Кінжал");
                        Replace("dialogue.0001.text", "[Міст] Роланд, даруй, але...");
                        Replace("ui.common.on", "Так");
                        Replace("messages.275", "Рушыць далей");
                        Replace("messages.316", "Пра заданне");
                        Replace("ui.moves.prefix", "Беларускі прыём: {direction}{prerequisite}");
                        Replace("ui.common.off", string.Join(" ", Enumerable.Repeat("Вельмі доўгі адказ", 20)));
                    }
                    using (var stream = File.Create(Path.Combine(folder, file.Key)))
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartObject();
                        foreach (var entry in values) writer.WriteString(entry.Key, entry.Value);
                        writer.WriteEndObject();
                    }
                    void Replace(string key, string value)
                    {
                        if (values.ContainsKey(key)) values[key] = value;
                    }
                }
            }

            var russian = new LocalizationCatalog(temporary);
            var belarusian = new LocalizationCatalog(temporary, "be-BY");
            Check(!russian.Issues.Any(issue => issue.IsError) && !belarusian.Issues.Any(issue => issue.IsError), "valid language fixtures");
            GameText.Use(russian);
            var labels = GameText.Messages(LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(resources, "TEXTmsg.dat"))));
            var talkChoice = new Atria.Game.Interiors.InteriorTalkChoice(labels[316], "TEST"u8.ToArray(), "suba_064"u8.ToArray(), true, "messages.316");
            var battle = new BattleMessages(GameText.Messages(LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(resources, "BATTLE.DAT"))), "battle"));
            var itemNames = GameText.ItemNames(ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(resources, "ATITEM.DAT"))));
            var moves = new MoveCatalog(resources);
            var actions = new Atria.Game.ActionMenu.ActionMenuController();
            actions.OpenWorldReveal(labels);
            actions.SetSelection(1);
            string russianMove = moves.All(0)[0].Name;
            var notice = new CombatGlobalUiRuntime(new(0), battle);
            notice.ShowRankNotice(3);
            var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(resources, "ENG1.FNT")), true);
            var dialogue = DialogueCatalog.Load(Path.Combine(resources, "ROLAND.IDX"), Path.Combine(resources, "ROLAND.DAT"));
            var effect = new ScriptEffect(ScriptEffectKind.DialogueKey,
                new Atria.Script.ScriptCall("TEST", "dialogue_script2", 0, [new(-1), new(-1), new(dialogue.Entries[1].RawKey)]));
            var narrative = new DialogueSession(DialogueRequest.Create(effect, dialogue), false);
            narrative.PrepareLayout(font);
            var question = new DialogueSession(new(-1, -1, true, [])
            {
                ChoiceKeys = ["ui.common.on", "ui.common.off"],
                Choices = [russian.Get("ui.common.on"), russian.Get("ui.common.off")]
            }, true);
            question.PrepareLayout(font);
            var state = new GameState();
            state.Globals[10] = 77;
            for (int tick = 0; tick < 8; tick++) question.AdvancePresentation();
            question.ApplyInput(new(false, false, true), state);

            GameText.Use(belarusian);
            narrative.PrepareLayout(font);
            question.PrepareLayout(font);
            Check(GameText.Decode(labels[1].Span) == "Міст", "already loaded message list switches");
            Check(GameText.Decode(talkChoice.Label.Span) == "Пра заданне" && talkChoice.Function.Span.SequenceEqual("suba_064"u8), "event talk label switches without changing its script handler");
            Check(GameText.Decode(itemNames[0].Span) == "Кінжал", "already loaded item names switch");
            var actionFrame = actions.AdvanceLogicalFrame()!;
            Check(GameText.Decode(actionFrame.Labels[0].Span) == "Рушыць далей" && actionFrame.Selection == 1, "open action menu retains selection and translates labels");
            Check(moves.All(0)[0].Name.StartsWith("Беларускі прыём:", StringComparison.Ordinal), "move catalog invalidates translated names");
            Check(GameText.Decode(battle.Pause.Span) == "Паўза" && GameText.Decode(notice.NoticeText.Span) == "Новы ранг", "live combat labels and active notice switch");
            Check(string.Join(" ", narrative.Request.DisplayLines!).Contains("даруй"), "active original indexed dialogue switches");
            Check(question.Request.DisplayLines!.Length > 2 && question.Request.ChoiceByRow![question.SelectedLine] == 1, "rewrapping retains semantic answer");
            Check(state.Globals[10] == 77 && !question.Completed && !narrative.Completed, "language refresh does not answer or advance a script");
            question.ApplyInput(new(true, false, false), state);
            Check(state.Globals[10] == 2, "confirmation after translation returns original answer index");
            GameText.Use(russian);
            narrative.PrepareLayout(font);
            Check(narrative.Request.Paragraph == russian.Get("dialogue.0001.text"), "dialogue can switch back");
            Check(moves.All(0)[0].Name == russianMove, "cached move catalog remains usable after round trip");

            CheckPreferencesAndMenu(resources, temporary, russian, belarusian, moves, font);
            foreach (bool small in new[] { true, false })
            {
                var originalFont = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(resources, small ? "ENG1.FNT" : "ENG.FNT")), small);
                foreach (char letter in "ІіЎў")
                    Check(originalFont.GetGlyph(GameText.Encoding.GetBytes(letter.ToString())[0]).ContainsAnyExcept((byte)0), "original font covers " + letter);
            }
            return "Live language: cached labels, active dialogue/answer identity, combat notice, settings selection, preference compatibility and Belarusian original glyphs passed.";
        }
        finally
        {
            GameText.Use(previous);
            Directory.Delete(temporary, true);
        }
    }

    private static void CheckPreferencesAndMenu(string resources, string temporary, LocalizationCatalog russian, LocalizationCatalog belarusian, MoveCatalog moves, BitmapFont font)
    {
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(resources, "BATTLE.CFG")));
        var controls = ControlProfile.Defaults(bindings);
        var audio = new NativeSettingsState();
        var store = new UserPreferencesStore(Path.Combine(temporary, "config"), resources);
        store.Write(new(controls, audio, bindings));
        string oldPreferences = File.ReadAllText(store.FilePath).Replace("  \"language\": \"ru-RU\",", "");
        File.WriteAllText(store.FilePath, oldPreferences);
        Check(store.Read(bindings).Language == "ru-RU", "old preferences need no reset");
        using var archive = BinderArchive.Open(Path.Combine(resources, "AT2SPR.000"));
        var tiles = SpriteFile.Decode(archive.ReadMember(archive.FindResource("ETC"u8)));
        using var input = new InputRouter(controls, bindings, new NoPads());
        var menu = new ControlSettingsMenu(input, bindings, moves, tiles, font, audio, (_, _) => throw new Exception("Unexpected apply"), language =>
        {
            store.Write(new(controls, audio, bindings, language));
            GameText.Use(language == "be-BY" ? belarusian : russian);
        });
        bool[] keys = new bool[256], pressed = new bool[256], mouse = new bool[3];
        input.Poll(keys, pressed, mouse, true, 0);
        menu.Update();
        keys[205] = true;
        input.Poll(keys, pressed, mouse, true, 1);
        menu.Update();
        Check(GameText.Current.Language == "be-BY" && store.Read(bindings).Language == "be-BY" && !menu.Closed, "language changes immediately without closing settings");
        keys[205] = false;
        input.Poll(keys, pressed, mouse, true, 2);
        menu.Update();
        keys[1] = true;
        input.Poll(keys, pressed, mouse, true, 3);
        menu.Update();
        Check(menu.Closed && store.Read(bindings).Language == "be-BY", "back does not lose immediately applied language");
        File.WriteAllText(store.FilePath, File.ReadAllText(store.FilePath).Replace("be-BY", "../invalid"));
        Check(store.Read(bindings).Language == "ru-RU" && store.Read(bindings).Audio.Music == audio.Music, "invalid language falls back without losing preferences");
    }

    private sealed class NoPads : IGamepadDevice
    {
        public PadSample Read(int slot) => default;
        public void Vibrate(int slot, ushort left, ushort right) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Live language: " + message);
    }
}
