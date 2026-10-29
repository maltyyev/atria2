using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Game.Dialogue;

/// <summary>Owned raw display bytes. CP949 lookup keys never pass through display decoding.</summary>
public sealed record DialogueRequest(int Placement, int Face, bool IsQuestion, IReadOnlyList<ReadOnlyMemory<byte>> Lines)
{
    public string? Paragraph
    {
        get; init;
    }
    public IReadOnlyList<string>? Choices
    {
        get; init;
    }
    public string[]? DisplayLines
    {
        get; init;
    }
    public int[]? ChoiceByRow
    {
        get; init;
    }
    public string? TextKey
    {
        get; init;
    }
    public string[]? ChoiceKeys
    {
        get; init;
    }

    public DialogueRequest Translate() => this with
    {
        Paragraph = TextKey is null ? Paragraph : GameText.Paragraph(TextKey, Paragraph ?? ""),
        Choices = ChoiceKeys is null ? Choices : ChoiceKeys.Select((key, index) => GameText.Paragraph(key, Choices![index])).ToArray(),
        DisplayLines = null,
        ChoiceByRow = null
    };

    public const int LineCapacity = 100;
    public const int LineByteCapacity = 128;
    public static DialogueRequest Create(ScriptEffect effect, DialogueCatalog catalog)
    {
        ScriptCall call = effect.Call;
        bool indexed = effect.Kind is ScriptEffectKind.DialogueKey or ScriptEffectKind.QuestionKey;
        bool question = effect.Kind is ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey;
        if (effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey or ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey))
        {
            throw Error("DIALOGUE_EFFECT", "The effect is not a dialogue callback.");
        }

        if (call.Arguments.Count < 2 || indexed && call.Arguments.Count != 3)
        {
            throw Error("ARGUMENT_COUNT", "Invalid dialogue arguments.");
        }

        int Scalar(int i) => call.Arguments[i].IsString ? throw Error("ARGUMENT_TYPE", "Expected dialogue scalar.") : call.Arguments[i].Integer;
        ReadOnlyMemory<byte> Bytes(int i) => call.Arguments[i].IsString ? call.Arguments[i].Bytes : throw Error("ARGUMENT_TYPE", "Expected raw dialogue bytes.");
        if (call.Function == "dialogue_text")
        {
            if (call.Arguments.Count != 3)
            {
                throw Error("ARGUMENT_COUNT", "Localized dialogue requires a resource key.");
            }

            string key = System.Text.Encoding.ASCII.GetString(Bytes(2).Span);
            return new(Scalar(0), Scalar(1), false, [])
            {
                TextKey = key,
                Paragraph = GameText.Get(key)
            };
        }
        var lines = new List<ReadOnlyMemory<byte>>();
        byte[] bank = new byte[LineCapacity * LineByteCapacity];
        int lineCount = 0;
        string textKey = $"scripts.{call.Member}.{call.ByteOffset}";
        var sourceLines = new List<ReadOnlyMemory<byte>>();
        if (indexed)
        {
            ReadOnlyMemory<byte> key = Bytes(2);
            if (key.Length > 39)
            {
                throw Error("STRING_CAPACITY", "Dialogue key exceeds recovered destination.");
            }

            if (!catalog.TryGetText(key.Span, out ReadOnlyMemory<byte> text, out int entryIndex))
            {
                throw Error("MISSING_DIALOGUE", "Dialogue key was not found.");
            }

            textKey = $"dialogue.{entryIndex:D4}";
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text.Span[i] == 13 && i + 1 < text.Length && text.Span[i + 1] == 10)
                {
                    Add(text.Slice(start, i - start));
                    i++;
                    start = i + 1;
                }
            }

            Add(text[start..]); // Preserve the final empty choice/line.
        }
        else
        {
            for (int i = 2; i < call.Arguments.Count; i++)
            {
                Add(Bytes(i));
            }
        }

        for (int i = 0; i < lineCount; i++)
        {
            ReadOnlyMemory<byte> tail = bank.AsMemory(i * LineByteCapacity);
            int end = tail.Span.IndexOf((byte)0);
            if (end < 0)
            {
                throw Error("DIALOGUE_CAPACITY", "Dialogue has no terminator within its owned bank.");
            }

            lines.Add(tail[..end]);
        }

        return new(Scalar(0), Scalar(1), question, lines.AsReadOnly())
        {
            TextKey = question ? null : textKey + ".text",
            ChoiceKeys = question ? sourceLines.Select((_, i) => indexed ? $"{textKey}.choice.{i:D2}" : $"{textKey}.{i + 2}").ToArray() : null,
            Paragraph = question ? null : GameText.Paragraph(textKey + ".text", TextResourceSchema.Normalize(string.Join(" ", sourceLines.Select(line => GameText.Decode(line.Span))))),
            Choices = question ? sourceLines.Select((line, i) => GameText.Paragraph(indexed ? $"{textKey}.choice.{i:D2}" : $"{textKey}.{i + 2}", GameText.Decode(line.Span).Trim())).ToArray() : null
        };
        void Add(ReadOnlyMemory<byte> line)
        {
            sourceLines.Add(line);
            int offset = lineCount * LineByteCapacity;
            int length = indexed ? line.Length : Math.Min(line.Length, LineByteCapacity);
            if (lineCount == LineCapacity || length > bank.Length - offset)
            {
                throw Error("DIALOGUE_CAPACITY", "Dialogue exceeds the recovered line bank.");
            }
            // Native indexed text writes past individual128-byte slots; later rows overwrite
            // overlapping bytes. This occurs in shipped DAT and is reproduced inside owned storage.
            line.Span[..length].CopyTo(bank.AsSpan(offset));
            lineCount++;
        }

        ScriptException Error(string code, string detail) => new(code, call.Member, call.ByteOffset, detail);
    }
}
