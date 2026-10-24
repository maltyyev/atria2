using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Status;

/// <summary>414f50/415430/416a20 integration with real catalog descriptions and equipment effects.</summary>
public sealed class StatusMenuRuntime
{
    public StatusMenuController Menu { get; } = new();
    public StatusEquipmentController Equipment { get; } = new();
    public StatusTextList? TextList
    {
        get; private set;
    }
    public ReadOnlyMemory<byte>[] Description { get; private set; } = [];

    public void Enter(Action<StatusMenuCommand> emit)
    {
        TextList = null;
        Description = [];
        Equipment.Enter();
        Menu.Enter(emit);
    }

    public void Input(
        StatusMenuInput input,
        GameState state,
        ItemCatalog items,
        ReadOnlyMemory<byte> eventItemText,
        QuestJournalCatalog journal,
        Action<StatusMenuCommand> emit
    )
    {
        void Command(StatusMenuCommand command)
        {
            if (command.Kind == StatusMenuEffect.BeginPage)
            {
                Menu.SetDetailActive(true);
                Description = [];
                if (Menu.Page == 0)
                {
                    Equipment.Enter();
                }
                else if (Menu.Page is 1 or 3 or 4)
                {
                    TextList = new(state, Menu.Page);
                }
                else
                {
                    TextList = null;
                }

                emit(new(StatusMenuEffect.Recompose));
            }

            emit(command);
        }

        void Sound(int id) => emit(new(StatusMenuEffect.Sound, id));
        void Recompose() => emit(new(StatusMenuEffect.Recompose));
        void Detail(StatusMenuInput value)
        {
            int action = value.Cancel ? 5 : value.Accept ? 4 : value.PreviousPage ? 1 : value.NextPage ? 0 : value.PreviousActor ? 3 : value.NextActor ? 2 : -1;
            bool closed = false;
            if (action is 0 or 1 or 4 or 5)
            {
                if (Menu.Page == 0)
                {
                    closed = Equipment.Input(action, state, items, Menu.SelectedActor, Sound, Recompose);
                }
                else if (Menu.Page is 1 or 3 or 4)
                {
                    closed = TextList!.Input(action, Sound, id => Description = StatusTextList.LoadDescription(Menu.Page, id, items, eventItemText, journal));
                }
                else if (action == 5)
                {
                    Sound(4);
                    Recompose();
                    closed = true;
                }

                if (closed)
                {
                    Menu.SetDetailActive(false);
                }

                if (closed && Menu.Page is 1 or 3 or 4)
                {
                    Recompose();
                }

                return;
            }

            //416a20 keeps horizontal selection in the equipment confirmation modal.
            if (Menu.Page == 0 && !Equipment.CanChangeActor)
            {
                if (Equipment.Dialog == 1 && action is 2 or 3)
                {
                    Equipment.Input(action, state, items, Menu.SelectedActor, Sound, Recompose);
                }

                return;
            }

            if (action < 0 && !value.CycleActor)
            {
                return;
            }

            int current = Menu.SelectedActor, next = current;
            if (action == 3)
            {
                for (int i = current - 1; i >= 0; i--)
                {
                    if (state.GetMemberStatus(i) != 0)
                    {
                        next = i;
                        break;
                    }
                }
            }
            else if (action == 2)
            {
                for (int i = current + 1; i < 6; i++)
                {
                    if (state.GetMemberStatus(i) != 0)
                    {
                        next = i;
                        break;
                    }
                }
            }
            else if (value.CycleActor)
            {
                bool found = false;
                for (int step = 1; step <= 6; step++)
                {
                    int i = (current + step) % 6;
                    if (state.GetMemberStatus(i) != 0)
                    {
                        next = i;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidDataException("Status cycle requires an active first-six actor.");
                }
            }

            if (next != current)
            {
                Menu.SetSelectedActor(next);
                emit(new(StatusMenuEffect.SelectActor, next));
                Sound(1);
                if (Menu.Page == 0)
                {
                    Equipment.ActorChanged(state, next);
                }
            }
        }

        Menu.Input(input, state, Command, Detail);
    }
}
