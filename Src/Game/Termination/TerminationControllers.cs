namespace Atria.Game.Termination;

public enum QuitKey
{
    Right,
    Left,
    Cancel,
    Confirm
}

public enum QuitEffectKind
{
    Sound,
    ReturnMenuRetained,
    RequestWindowClose
}

public readonly record struct QuitEffect(QuitEffectKind Kind, int Value = 0);
public sealed class QuitController
{
    public int Selection { get; private set; } = 1;

    public void Initialize() => Selection = 1;
    public void Input(Func<QuitKey, bool> query, Action<QuitEffect> emit)
    {
        if (query(QuitKey.Right))
        {
            if (Selection != 2)
            {
                emit(new(QuitEffectKind.Sound, 1));
                Selection = 2;
            }

            return;
        }

        if (query(QuitKey.Left))
        {
            emit(new(QuitEffectKind.Sound, 1));
            Selection = 1;
            return;
        }

        if (query(QuitKey.Cancel))
        {
            emit(new(QuitEffectKind.Sound, 4));
            emit(new(QuitEffectKind.ReturnMenuRetained));
            return;
        }

        if (query(QuitKey.Confirm))
        {
            emit(new(QuitEffectKind.Sound, 3));
            emit(new(Selection == 1 ? QuitEffectKind.RequestWindowClose : QuitEffectKind.ReturnMenuRetained));
        }
    }
}

public sealed partial class DefeatController
{
    public int DrawCounter
    {
        get; private set;
    } // Native BSS4c69bc retained across entries; no entry reset.

    public void Input(Func<bool> confirm, Action resetAndReturn)
    {
        if (confirm())
        {
            resetAndReturn();
        }
    }

    public void AdvanceFrame()
    {
        if (DrawCounter < 20)
        {
            DrawCounter++;
        }
    }
}
