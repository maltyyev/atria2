using System.Globalization;
using System.Text.Json;
using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Game.Termination;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Tests.Localization;

public static class TextFlowTests
{
    public static string Run(string resources)
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Text flow: " + message);
        }

        int Measure(string text) => StringInfo.ParseCombiningCharacters(text).Length;
        Check(TextFlow.Wrap("one two three\n\nfour", 7, Measure).SequenceEqual(new[] { "one two", "three", "", "four" }), "words and explicit paragraphs");
        Check(TextFlow.Wrap("a\u0301b\u0301c\u0301", 1, Measure).SequenceEqual(new[] { "a\u0301", "b\u0301", "c\u0301" }), "combining characters stay attached");
        Check(TextFlow.Wrap("中文日本語", 2, Measure).SequenceEqual(new[] { "中文", "日本", "語" }), "unspaced language has no lost characters");
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(resources, "eng1.fnt")), true);
        string longAnswer = string.Join(" ", Enumerable.Repeat("Длинный ответ", 30));
        var state = new GameState();
        var question = new DialogueSession(new(-1, -1, true, []) { Choices = [longAnswer, "Второй ответ"] }, false);
        question.PrepareLayout(font);
        for (int i = 0; i < 7; i++) question.AdvancePresentation();
        int secondRow = Array.IndexOf(question.Request.ChoiceByRow!, 1);
        Check(secondRow > 4 && question.Request.DisplayLines!.All(line => font.MeasureText(line) <= 440), "long answer wraps inside panel");
        for (int i = 0; i < secondRow; i++) question.ApplyInput(new(false, false, true), state);
        Check(question.FirstLine > 0, "long question scrolls");
        question.ApplyInput(new(true, false, false), state);
        Check(question.Completed && state.Globals[10] == 2, "answer is semantic index, not screen row");
        var narrative = new DialogueSession(new(-1, -1, false, []) { Paragraph = longAnswer }, true, previousSelection: 1);
        narrative.PrepareLayout(font);
        for (int i = 0; i < 7; i++) narrative.AdvancePresentation();
        narrative.ApplyInput(new(false, true, false), state);
        int pages = 0;
        while (!narrative.Completed && pages++ < 100) narrative.ApplyInput(new(true, false, false), state);
        Check(pages == (narrative.Request.DisplayLines!.Length + 3) / 4 && state.Globals[10] == 2, "all narrative pages preserve previous choice");

        for (int selection = 1; selection <= 9; selection++)
        {
            var text = EndingText.Parse(File.ReadAllBytes(Path.Combine(resources, "END.DAT")), selection);
            var ending = new EndingController();
            bool done = false;
            void Emit(EndingEffect effect) => done |= effect.Kind == EndingEffectKind.ReturnMainMenu;
            ending.Enter(selection, Emit, text.Flow);
            for (int tick = 0; tick < 30000 && !done; tick++)
            {
                ending.Input(Emit);
                if (!done) ending.AdvanceFrame();
            }
            Check(done, "flowing ending finishes without exhausting picture table");
        }

        string root = Path.Combine(Path.GetTempPath(), "atria-text-upgrade-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "ru-RU");
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "dialogue.json");
            string original = "{\"dialogue.0000.01\":\"  вторая строка   \",\"dialogue.0000.00\":\"Моя правка\"}";
            File.WriteAllText(path, original);
            string bad = Path.Combine(folder, "items.json");
            File.WriteAllText(bad, "{");
            try { TextResourceSchema.UpgradeFiles(root); throw new Exception("Expected malformed JSON rejection"); }
            catch (JsonException) { }
            Check(File.ReadAllText(path) == original, "all files parsed before any replacement");
            File.Delete(bad);
            Check(TextResourceSchema.UpgradeFiles(root) == 1, "upgrade replaces old schema");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Check(document.RootElement.GetProperty("dialogue.0000.text").GetString() == "Моя правка вторая строка", "numeric row order and user edits preserved");
            Check(File.ReadAllText(Directory.GetFiles(folder, "*.bak").Single()) == original, "exact old bytes backed up");
            Check(TextResourceSchema.UpgradeFiles(root) == 0, "migration is idempotent");
        }
        finally { Directory.Delete(root, true); }
        return "text flow: paragraph wrapping, graphemes, semantic answers, pagination, nine endings, atomic migration and backups passed";
    }
}
