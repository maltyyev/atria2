using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Game.State;

namespace Atria.Game.Dialogue;

public readonly record struct DialogueInput(bool ConfirmPressed, bool UpPressed, bool DownPressed);
public enum DialogueInputAction
{
    Confirm,
    Up,
    Down
}

public readonly record struct DialogueFrame(
    int PanelX,
    int PanelY,
    int OpeningOffset,
    bool TextVisible,
    int FirstLine,
    int SelectedLine,
    bool QuestionAppearance,
    int ArrowXOffset,
    int ArrowYOffset
);
/// <summary>Recovered modal state. AdvancePresentation is one native 42c750/42ca80 callback, not a display refresh.</summary>
public sealed class DialogueSession
{
    private static ReadOnlySpan<int> OpeningOffsets => [10, 3, 6, 12, 25, 50, 100, 200];
    public DialogueRequest Request
    {
        get; private set;
    }
    public int FirstLine
    {
        get; private set;
    }
    public int SelectedLine
    {
        get; private set;
    }
    public bool Completed
    {
        get; private set;
    }
    public bool NativeLoopBypassed
    {
        get; private set;
    }
    public int PanelX
    {
        get;
    }
    public bool QuestionInputEnabled
    {
        get;
    }

    private LocalizationCatalog? layoutCatalog;

    public void PrepareLayout(BitmapFont font)
    {
        bool languageChanged = layoutCatalog is not null && !ReferenceEquals(layoutCatalog, GameText.Current);
        int selectedChoice = Request.ChoiceByRow is { Length: > 0 } mapping ? mapping[Math.Clamp(SelectedLine, 0, mapping.Length - 1)] : SelectedLine;
        if (languageChanged)
        {
            Request = Request.Translate();
            FirstLine = 0;
        }
        layoutCatalog = GameText.Current;
        if (Request.DisplayLines is not null)
        {
            return;
        }
        const int availableWidth = 440;
        if (Request.IsQuestion)
        {
            var lines = new List<string>();
            var choices = new List<int>();
            var source = Request.Choices ?? Request.Lines.Select(line => GameText.Decode(line.Span)).ToArray();
            if (source.Count == 0)
            {
                throw new InvalidDataException("A dialogue question requires at least one answer.");
            }
            for (int choice = 0; choice < source.Count; choice++)
            {
                var wrapped = TextFlow.Wrap(source[choice], availableWidth, font.MeasureText);
                lines.AddRange(wrapped);
                choices.AddRange(Enumerable.Repeat(choice, wrapped.Length));
            }
            Request = Request with { DisplayLines = lines.ToArray(), ChoiceByRow = choices.ToArray() };
            if (languageChanged)
            {
                SelectedLine = Math.Max(0, choices.IndexOf(selectedChoice));
                FirstLine = Math.Max(0, SelectedLine - 3);
            }
        }
        else
        {
            string text = Request.Paragraph ?? string.Join("\n", Request.Lines.Select(line => GameText.Decode(line.Span)));
            Request = Request with { DisplayLines = TextFlow.Wrap(text, availableWidth, font.MeasureText) };
        }
    }

    private int opening = 7, arrowStep, arrowTick;
    public void PointerSelect(int x, int y)
    {
        if (Request.IsQuestion && opening <= 2 && x >= PanelX + 16 && x < PanelX + 468 && y >= 401 && y < 465)
        {
            int line = FirstLine + (y - 401) / 16;
            if (line < (Request.DisplayLines?.Length ?? Request.Lines.Count))
            {
                SelectedLine = line;
            }
        }
    }

    // questionInputEnabled is the persistent native 4caed8 flag; dialogue callbacks do not clear it.
    public DialogueSession(DialogueRequest request, bool questionInputEnabled, int previousPanelX = 0, int previousSelection = 0)
    {
        Request = request;
        QuestionInputEnabled = request.IsQuestion || questionInputEnabled;
        SelectedLine = request.IsQuestion ? 0 : previousSelection;
        PanelX = request.Placement switch
        {
            -1 => 80,
            0 => 160,
            1 => 0,
            _ => previousPanelX
        };
    }

    public DialogueFrame AdvancePresentation()
    {
        if (Completed || NativeLoopBypassed)
        {
            throw new InvalidOperationException("Dialogue has finished.");
        }

        int offset = opening > 0 ? OpeningOffsets[opening] : 0;
        if (opening > 0)
        {
            opening--;
        }

        bool visible = opening <= 2;
        var frame = new DialogueFrame(PanelX, 384, offset, visible, FirstLine, SelectedLine, Request.IsQuestion, arrowStep, -arrowStep);
        if (visible && ++arrowTick > 1)
        {
            arrowTick = 0;
            arrowStep++;
            if (arrowStep > 6)
            {
                arrowStep = 0;
            }
        }

        return frame;
    }

    // Native4219b0 calls input before the modal draw callback; key booleans are rising edges (4048c0).
    public IReadOnlyList<int> ApplyInput(DialogueInput input, GameState state)
    {
        var sounds = new List<int>(3);
        ApplyInput(action => action switch
        {
            DialogueInputAction.Confirm => input.ConfirmPressed,
            DialogueInputAction.Up => input.UpPressed,
            _ => input.DownPressed
        }, state, sounds.Add);
        return sounds;
    }

    // Query lazily: native early returns leave lower-priority keys' edge latches untouched.
    public void ApplyInput(Func<DialogueInputAction, bool> readPressed, GameState state, Action<int> sound)
    {
        if (Completed || NativeLoopBypassed)
        {
            throw new InvalidOperationException("Dialogue has finished.");
        }

        if (Request.ChoiceByRow is { } choiceByRow)
        {
            if (readPressed(DialogueInputAction.Confirm))
            {
                if (opening > 2)
                {
                    return;
                }
                sound(5);
                state.Globals[10] = checked((short)(choiceByRow[SelectedLine] + 1));
                Completed = true;
                return;
            }
            int direction = readPressed(DialogueInputAction.Up) ? -1 : readPressed(DialogueInputAction.Down) ? 1 : 0;
            if (direction != 0 && opening <= 2)
            {
                int selected = Math.Clamp(SelectedLine + direction, 0, choiceByRow.Length - 1);
                if (selected != SelectedLine)
                {
                    SelectedLine = selected;
                    FirstLine = Math.Clamp(FirstLine, Math.Max(0, selected - 3), selected);
                    sound(1);
                }
            }
            return;
        }

        if (readPressed(DialogueInputAction.Confirm))
        {
            if (opening > 2)
            {
                return;
            }

            sound(5);
            if ((Request.DisplayLines?.Length ?? Request.Lines.Count) <= FirstLine + 4)
            {
                Completed = true;
                FirstLine = 0;
                if (QuestionInputEnabled)
                {
                    state.Globals[10] = unchecked((short)(SelectedLine + 1));
                }
            }

            FirstLine += 4; // Native increments even after clearing the modal flag.
        }

        // A narrative following a question retains its answer, regardless of display wrapping.
        if (Request.DisplayLines is not null)
        {
            return;
        }

        if (readPressed(DialogueInputAction.Up))
        {
            if (opening > 2 || !QuestionInputEnabled)
            {
                return;
            }

            sound(1);
            if (SelectedLine > 0)
            {
                SelectedLine--;
            }
        }

        if (readPressed(DialogueInputAction.Down) && opening <= 2 && QuestionInputEnabled)
        {
            sound(1);
            if (SelectedLine < Request.Lines.Count - 1)
            {
                SelectedLine++;
            }
        }
    }

    public void BypassNativeLoop(GameState state)
    {
        if (state.EndingResetFlag == 0)
        {
            throw new InvalidOperationException("The native modal bypass requires pending46fd6c.");
        }

        NativeLoopBypassed = true;
    }
}
