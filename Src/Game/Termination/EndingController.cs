using Atria.Render.UI;

namespace Atria.Game.Termination;

public sealed record EndingDefinition(int[] Pictures, int[] ThresholdRows, int TotalRows);
public static class EndingCatalog
{
    public static EndingDefinition Get(int selection) => selection switch
    {
        1 => new([0, 16, 13], [25, 46, 1000], 92),
        2 => new([1, 17], [18, 1000], 54),
        3 => new([8, 3], [24, 1000], 58),
        4 => new([9, 11, 10], [30, 55, 1000], 82),
        5 => new([6, 4], [25, 1000], 58),
        6 => new([7, 14], [25, 1000], 64),
        7 => new([5, 12], [24, 100], 73),
        8 => new([15, 12], [15, 100], 45),
        9 => new([9, 11, 10, 6, 11], [30, 37, 50, 111, 1000], 148),
        _ => throw new InvalidDataException("Ending selection is outside native1..9 definitions.")
    };
}

public sealed class EndingText
{
    public const int Rows = 200, RowSize = 80;
    private readonly byte[] bytes;
    private readonly int selection;
    public ScrollingText Flow { get; }
    private EndingText(byte[] bytes, int selection)
    {
        (this.bytes, this.selection) = (bytes, selection);
        Flow = new($"endings.{selection}", 440);
    }
    public ReadOnlySpan<byte> this[int row] => GameText.Legacy($"endings.{selection}.{row:D3}", bytes.AsMemory(checked(row * RowSize), RowSize)).Span;
    public static EndingText Parse(ReadOnlySpan<byte> source, int selection)
    {
        _ = EndingCatalog.Get(selection);
        int offset = checked(100 + (selection - 1) * Rows * RowSize);
        if (offset > source.Length - Rows * RowSize)
        {
            throw new InvalidDataException("Truncated END.DAT section.");
        }

        byte[] rows = source.Slice(offset, Rows * RowSize).ToArray();
        for (int row = 0; row < Rows; row++)
        {
            if (!rows.AsSpan(row * RowSize, RowSize).Contains((byte)0))
            {
                throw new InvalidDataException("Unterminated ending text row.");
            }
        }

        return new(rows, selection);
    }
}

public enum EndingEffectKind
{
    LoadBackground,
    ReturnMainMenu
}

public readonly record struct EndingEffect(EndingEffectKind Kind, int Value = 0);
public sealed class EndingController
{
    private EndingDefinition? definition;
    private EndingDefinition? sourceDefinition;
    private ScrollingText? flowingText;
    private LocalizationCatalog language = GameText.Current;
    private bool pendingPicture;
    public int PictureIndex
    {
        get; private set;
    }
    public int Scroll
    {
        get; private set;
    }
    public int TextY
    {
        get; private set;
    }

    public void Enter(int selection, Action<EndingEffect> emit, ScrollingText? text = null)
    {
        definition = EndingCatalog.Get(selection);
        sourceDefinition = definition;
        flowingText = text;
        language = GameText.Current;
        if (text is not null)
        {
            definition = definition with { ThresholdRows = definition.ThresholdRows.Select(text.MapSourceRow).ToArray(), TotalRows = text.Lines.Count + 9 };
        }
        PictureIndex = Scroll = 0;
        TextY = 360;
        pendingPicture = false;
        emit(new(EndingEffectKind.LoadBackground, definition.Pictures[0]));
    }

    //4210f0 has no player-input queries. Completion and picture changes happen here before draw.
    public void Input(Action<EndingEffect> emit)
    {
        if (!ReferenceEquals(language, GameText.Current) && flowingText is not null && sourceDefinition is not null)
        {
            definition = sourceDefinition with
            {
                ThresholdRows = sourceDefinition.ThresholdRows.Select(flowingText.MapSourceRow).ToArray(),
                TotalRows = flowingText.Lines.Count + 9
            };
            language = GameText.Current;
        }
        var data = definition ?? throw new InvalidOperationException("Ending not entered.");
        if (data.TotalRows * 24 < Scroll)
        {
            emit(new(EndingEffectKind.ReturnMainMenu));
        }

        if (pendingPicture)
        {
            pendingPicture = false;
            if (++PictureIndex >= data.Pictures.Length)
            {
                throw new InvalidDataException("Ending picture cursor exceeds recovered table.");
            }

            emit(new(EndingEffectKind.LoadBackground, data.Pictures[PictureIndex]));
        }
    }

    public int AdvanceFrame()
    {
        var data = definition ?? throw new InvalidOperationException("Ending not entered.");
        if (data.ThresholdRows[PictureIndex] * 24 < Scroll)
        {
            pendingPicture = true;
        }

        int drawn = TextY;
        TextY--;
        Scroll = unchecked(Scroll + 1);
        return drawn;
    }
}
