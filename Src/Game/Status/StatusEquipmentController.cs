using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Status;

/// <summary>Native416130 equipment modal state. Renderers read these exact domains.</summary>
public sealed class StatusEquipmentController
{
    public int Slot
    {
        get; private set;
    }
    public int Dialog
    {
        get; private set;
    }
    public bool RemoveSelected
    {
        get; private set;
    }
    public bool ChangeSelected
    {
        get; private set;
    }
    public bool ListActive
    {
        get; private set;
    }
    public bool Conflict
    {
        get; private set;
    }
    public bool Unavailable
    {
        get; private set;
    }
    public int Scroll
    {
        get; private set;
    }
    public int Row
    {
        get; private set;
    }
    public int SelectionFlash
    {
        get; private set;
    }

    public void AdvanceSelectionFlash()
    {
        if (SelectionFlash > 0)
        {
            SelectionFlash--;
        }
    }

    private int[] candidates = [];
    public IReadOnlyList<int> Candidates => candidates;

    public void Enter()
    {
        Slot = Dialog = Scroll = Row = SelectionFlash = 0;
        RemoveSelected = ChangeSelected = ListActive = Conflict = Unavailable = false;
        candidates = [];
    }

    public void ActorChanged(GameState state, int actor)
    {
        int capacity = state.Heroes[actor].EquipmentCapacity;
        if ((uint)capacity > 6)
        {
            throw new InvalidDataException("Status capacity exceeds six slots.");
        }

        if (Slot >= capacity)
        {
            Slot = capacity - 1;
        }
    }

    private void OpenList(GameState state, ItemCatalog items, int actor)
    {
        candidates = StatusEquipment.Candidates(state, items, actor, Slot);
        ListActive = true;
        Scroll = Row = 0;
    }

    private void ResetModal()
    {
        ListActive = false;
        Dialog = 0;
        RemoveSelected = ChangeSelected = false;
    }

    private void RecoverList(GameState state, ItemCatalog items, int actor, Action<int> sound)
    {
        OpenList(state, items, actor);
        sound(4);
        ChangeSelected = true;
        RemoveSelected = false;
        Conflict = Unavailable = false;
        Dialog = 2;
    }

    // Native actions:0 down,1 up,2 right,3 left,4 accept,5 cancel. Returns true when detail closes.
    public bool Input(int action, GameState state, ItemCatalog items, int actor, Action<int> sound, Action recompose)
    {
        switch (action)
        {
            case 0:
            case 1:
                if (Dialog == 1)
                {
                    break;
                }

                if (Conflict)
                {
                    sound(4);
                    break;
                }

                if (!ListActive)
                {
                    if (action == 0 && Slot < state.Heroes[actor].EquipmentCapacity - 1)
                    {
                        sound(1);
                        Slot++;
                    }
                    else if (action == 1 && Slot > 0)
                    {
                        sound(1);
                        Slot--;
                    }
                }
                else if (action == 0 && Scroll + Row < candidates.Length - 1)
                {
                    sound(1);
                    if (Row < 2)
                    {
                        Row++;
                    }
                    else
                    {
                        Scroll++;
                    }
                }
                else if (action == 1 && Scroll + Row > 0)
                {
                    sound(1);
                    if (Row > 0)
                    {
                        Row--;
                    }
                    else
                    {
                        Scroll--;
                    }
                }

                break;
            case 2:
                if (!RemoveSelected)
                {
                    sound(1);
                    ChangeSelected = false;
                    RemoveSelected = true;
                    SelectionFlash = 1;
                }

                break;
            case 3:
                if (!ChangeSelected)
                {
                    sound(1);
                    ChangeSelected = true;
                    RemoveSelected = false;
                    SelectionFlash = 1;
                }

                break;
            case 4:
                if (ListActive && candidates.Length < 1 || Conflict || Unavailable)
                {
                    RecoverList(state, items, actor, sound);
                    break;
                }

                if (Dialog == 0)
                {
                    sound(3);
                    Dialog = 1;
                    ChangeSelected = true;
                    RemoveSelected = false;
                    break;
                }

                if (RemoveSelected)
                {
                    var result = StatusEquipment.Remove(state, items, actor, Slot);
                    sound(result == StatusEquipResult.Changed ? 6 : 4);
                    candidates = StatusEquipment.Candidates(state, items, actor, Slot);
                    recompose();
                    ResetModal();
                    break;
                }

                if (ChangeSelected)
                {
                    Dialog = 2;
                }

                if (!ListActive)
                {
                    OpenList(state, items, actor);
                    sound(3);
                    break;
                }

                var equipped = StatusEquipment.Equip(state, items, actor, Slot, candidates[Scroll + Row]);
                if (equipped == StatusEquipResult.Changed)
                {
                    sound(6);
                    candidates = StatusEquipment.Candidates(state, items, actor, Slot);
                }
                else
                {
                    sound(4);
                    Conflict = equipped == StatusEquipResult.ConflictingAccessory;
                    Unavailable = equipped == StatusEquipResult.Unavailable;
                }

                recompose();
                ResetModal();
                break;
            case 5:
                sound(4);
                if (Dialog == 1)
                {
                    Dialog = 0;
                    break;
                }

                if (Conflict)
                {
                    OpenList(state, items, actor);
                    ChangeSelected = true;
                    Conflict = false;
                    RemoveSelected = false;
                    Unavailable = false;
                    Dialog = 2;
                    break;
                }

                if (!ListActive)
                {
                    Dialog = 0;
                    recompose();
                    return true;
                }

                recompose();
                ResetModal();
                break;
        }

        return false;
    }

    public bool CanChangeActor => Dialog != 1 && !Conflict && !ListActive;
}
