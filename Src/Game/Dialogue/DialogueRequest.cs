using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Game.Dialogue;

/// <summary>Owned raw display bytes. CP949 lookup keys never pass through display decoding.</summary>
public sealed record DialogueRequest(int Placement, int Face, bool IsQuestion, IReadOnlyList<ReadOnlyMemory<byte>> Lines)
{
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
        var lines = new List<ReadOnlyMemory<byte>>();
        byte[] bank = new byte[LineCapacity * LineByteCapacity];
        int lineCount = 0;
        if (indexed)
        {
            ReadOnlyMemory<byte> key = Bytes(2);
            if (key.Length > 39)
            {
                throw Error("STRING_CAPACITY", "Dialogue key exceeds recovered destination.");
            }

            if (!catalog.TryGetText(key.Span, out ReadOnlyMemory<byte> text))
            {
                throw Error("MISSING_DIALOGUE", "Dialogue key was not found.");
            }

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

            Add(text[start..]); // Native 42c5c0 keeps the empty line after a final CRLF.
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

        return new(Scalar(0), Scalar(1), question, lines.AsReadOnly());
        void Add(ReadOnlyMemory<byte> line)
        {
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
