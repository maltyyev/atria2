using Atria.Game.State;

namespace Atria.Game.Status;

public readonly record struct StatusMenuInput(
    bool Cancel = false,
    bool Accept = false,
    bool PreviousPage = false,
    bool NextPage = false,
    bool PreviousActor = false,
    bool NextActor = false,
    bool CycleActor = false
);
public enum StatusMenuEffect
{
    Sound,
    ReleaseSprites,
    PopMode,
    SetRestoreFlag,
    Recompose,
    MarkDirty,
    BeginPage,
    SelectActor
}

public readonly record struct StatusMenuCommand(StatusMenuEffect Kind, int Value = 0);
/// <summary>Native414f50/416f90 root controller. Detail handlers must execute synchronously.</summary>
public sealed class StatusMenuController
{
    public int Page
    {
        get; private set;
    }
    public int PageFlash
    {
        get; private set;
    }

    public void AdvancePageFlash()
    {
        if (PageFlash > 0)
        {
            PageFlash--;
        }
    }

    public int SelectedActor
    {
        get; private set;
    }
    public bool DetailActive
    {
        get; private set;
    }
    public int InputDelay
    {
        get; private set;
    }

    public void PointerPage(int page)
    {
        if (!DetailActive && page is >= 0 and < 5)
        {
            Page = page;
        }
    }

    public void Enter(Action<StatusMenuCommand> emit)
    {
        Page = PageFlash = 0;
        SelectedActor = 0;
        DetailActive = false;
        InputDelay = 2;
        emit(new(StatusMenuEffect.SelectActor, 0));
        emit(new(StatusMenuEffect.Recompose));
    }

    public void SetDetailActive(bool value) => DetailActive = value;
    public void SetSelectedActor(int actor)
    {
        if (actor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actor));
        }

        SelectedActor = actor;
    }

    public void Input(StatusMenuInput input, GameState state, Action<StatusMenuCommand> emit, Action<StatusMenuInput> detailInput)
    {
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(detailInput);
        if (InputDelay != 0)
        {
            InputDelay--;
            return;
        }

        if (DetailActive)
        {
            detailInput(input);
            return;
        }

        if (input.Cancel)
        {
            emit(new(StatusMenuEffect.Sound, 4));
            emit(new(StatusMenuEffect.ReleaseSprites));
            emit(new(StatusMenuEffect.PopMode));
            emit(new(StatusMenuEffect.SetRestoreFlag, 1));
            return;
        }

        if (input.Accept)
        {
            emit(new(StatusMenuEffect.Sound, 3));
            emit(new(StatusMenuEffect.BeginPage, Page));
            return;
        }

        if (input.PreviousPage)
        {
            if (Page > 0)
            {
                emit(new(StatusMenuEffect.Sound, 1));
                Page--;
                PageFlash = 1;
                emit(new(StatusMenuEffect.MarkDirty, 1));
            }

            return;
        }

        if (input.NextPage)
        {
            if (Page < 4)
            {
                emit(new(StatusMenuEffect.Sound, 1));
                Page++;
                PageFlash = 1;
                emit(new(StatusMenuEffect.MarkDirty, 1));
            }

            return;
        }

        int actor = SelectedActor;
        if (input.PreviousActor)
        {
            for (int i = actor - 1; i >= 0; i--)
            {
                if (state.Heroes[i].MemberStatus != 0)
                {
                    actor = i;
                    break;
                }
            }
        }
        else if (input.NextActor)
        {
            for (int i = actor + 1; i < state.Heroes.Length; i++)
            {
                if (state.Heroes[i].MemberStatus != 0)
                {
                    actor = i;
                    break;
                }
            }
        }
        else if (input.CycleActor)
        {
            bool found = false;
            for (int step = 1; step <= state.Heroes.Length; step++)
            {
                int i = (actor + step) % state.Heroes.Length;
                if (state.Heroes[i].MemberStatus != 0)
                {
                    actor = i;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidDataException("Native status actor cycle requires an active party member in the roster.");
            }
        }

        if (actor != SelectedActor)
        {
            SelectedActor = actor;
            emit(new(StatusMenuEffect.SelectActor, actor));
            emit(new(StatusMenuEffect.Sound, 1));
        }
    }
}
