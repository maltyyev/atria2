namespace Atria.Game.Persistence;

public enum SaveLoadMenuKind
{
    Save = 220,
    Load = 230
}

public enum SaveLoadMenuEffect
{
    Sound,
    PopMode,
    RestoreBackground,
    SaveSlot,
    LoadSlot,
    RefreshSlots
}

public readonly record struct SaveLoadMenuCommand(SaveLoadMenuEffect Kind, int Value = 0);
public readonly record struct SaveLoadMenuInput(bool Down = false, bool Up = false, bool Accept = false, bool Cancel = false, bool Left = false, bool Right = false);
public readonly record struct SaveLoadMenuFrame(SaveLoadMenuKind Kind, int Selection, bool Confirmation, int ConfirmationChoice, bool EmptyNotice, int Countdown, int Flash, int Top = 0);
/// <summary>42b060/422660 input and42bfd0/423050 logical draw countdown. Disk operations execute on following input tick.</summary>
public sealed class SaveLoadMenuController
{
    public SaveLoadMenuKind Kind
    {
        get; private set;
    }
    public int Selection
    {
        get; private set;
    }
    public bool Confirmation
    {
        get; private set;
    }
    public int ConfirmationChoice
    {
        get; private set;
    }
    public bool EmptyNotice
    {
        get; private set;
    }
    public int Countdown
    {
        get; private set;
    }
    public int Flash
    {
        get; private set;
    }
    public bool PendingOperation
    {
        get; private set;
    }
    public string? Failure
    {
        get; private set;
    }
    public int Count { get; private set; } = 5;
    public int Top
    {
        get; private set;
    }

    public void SetCount(int count)
    {
        Count = count;
        Selection = Math.Clamp(Selection, 0, Math.Max(0, count - 1));
        EnsureVisible();
    }

    private void EnsureVisible()
    {
        Top = Math.Clamp(Top, Math.Max(0, Selection - 4), Selection);
    }

    public void PointerSelect(int x, int y)
    {
        if (Countdown > 0 || PendingOperation || Failure is not null)
        {
            return;
        }

        if (Confirmation)
        {
            if (y is >= 186 and < 218 && x is >= 250 and < 390)
            {
                ConfirmationChoice = x < 315 ? 0 : 1;
            }
        }
        else if (!EmptyNotice && Count > 0 && x is >= 128 and < 530 && y is >= 96 and < 336)
        {
            Selection = Math.Min(Top + (y - 96) / 48, Count - 1);
        }
    }

    public void Enter(SaveLoadMenuKind kind, int rememberedSelection, Action<SaveLoadMenuCommand> emit)
    {
        if (kind is not (SaveLoadMenuKind.Save or SaveLoadMenuKind.Load) || rememberedSelection < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rememberedSelection));
        }

        Kind = kind;
        Selection = rememberedSelection;
        Confirmation = false;
        ConfirmationChoice = 0;
        EmptyNotice = false;
        Countdown = Flash = 0;
        PendingOperation = false;
        Failure = null;
        Top = 0;
        emit(new(SaveLoadMenuEffect.RefreshSlots));
        EnsureVisible();
    }

    public void ReportFailure(string failure)
    {
        Failure = failure;
        PendingOperation = false;
        Countdown = 0;
        Confirmation = false;
    }

    public void AcknowledgeFailure() => Failure = null;
    public void Input(SaveLoadMenuInput input, IReadOnlyList<LegacySlotMetadata> slots, Action<SaveLoadMenuCommand> emit)
    {
        SetCount(slots.Count);
        if (Failure is not null)
        {
            return;
        }

        if (PendingOperation)
        {
            PendingOperation = false;
            emit(new(Kind == SaveLoadMenuKind.Save ? SaveLoadMenuEffect.SaveSlot : SaveLoadMenuEffect.LoadSlot, Selection));
            if (Kind == SaveLoadMenuKind.Load || Failure is not null)
            {
                return;
            }

            emit(new(SaveLoadMenuEffect.RefreshSlots));
        }

        if (Countdown > 0)
        {
            return;
        }

        if (!Confirmation)
        {
            // Native root priority: down,up,accept,cancel, including bounded keys consuming the branch.
            if (input.Down)
            {
                if (Selection < Count - 1)
                {
                    emit(new(SaveLoadMenuEffect.Sound, 1));
                    Flash = 1;
                    Selection++;
                    EnsureVisible();
                }

                return;
            }

            if (input.Up)
            {
                if (Selection > 0)
                {
                    emit(new(SaveLoadMenuEffect.Sound, 1));
                    Flash = 1;
                    Selection--;
                    EnsureVisible();
                }

                return;
            }

            if (input.Accept)
            {
                if (Count == 0)
                {
                    return;
                }

                emit(new(SaveLoadMenuEffect.Sound, 3));
                if (Kind == SaveLoadMenuKind.Load)
                {
                    if (EmptyNotice)
                    {
                        EmptyNotice = false;
                        return;
                    }

                    if (slots[Selection].Present == 0)
                    {
                        EmptyNotice = true;
                        return;
                    }
                }

                Confirmation = true;
                ConfirmationChoice = 0;
                return;
            }

            if (input.Cancel)
            {
                emit(new(SaveLoadMenuEffect.Sound, 4));
                if (Kind == SaveLoadMenuKind.Load && EmptyNotice)
                {
                    EmptyNotice = false;
                    return;
                }

                emit(new(SaveLoadMenuEffect.PopMode));
                emit(new(SaveLoadMenuEffect.RestoreBackground, 1));
                return;
            }
        }
        else
        {
            // Native modal priority:left,right,accept,cancel. Left/right sound even when already selected.
            if (input.Left)
            {
                emit(new(SaveLoadMenuEffect.Sound, 1));
                ConfirmationChoice = 0;
                return;
            }

            if (input.Right)
            {
                emit(new(SaveLoadMenuEffect.Sound, 1));
                ConfirmationChoice = 1;
                return;
            }

            if (input.Accept)
            {
                emit(new(SaveLoadMenuEffect.Sound, 3));
                if (ConfirmationChoice == 0)
                {
                    Countdown = 20;
                }

                Confirmation = false;
                return;
            }

            if (input.Cancel)
            {
                emit(new(SaveLoadMenuEffect.Sound, 4));
                Confirmation = false;
                return;
            }
        }
    }

    public SaveLoadMenuFrame AdvanceLogicalFrame()
    {
        var frame = new SaveLoadMenuFrame(Kind, Selection, Confirmation, ConfirmationChoice, EmptyNotice, Countdown, Flash, Top);
        if (Countdown > 0 && --Countdown == 0)
        {
            PendingOperation = true;
        }

        if (Flash > 0)
        {
            Flash--;
        }

        return frame;
    }
}
