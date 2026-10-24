using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>Recovered manual walking409070 and normal attack40b230; other action handlers remain separate.</summary>
public static class CombatManualControl
{
    public static void ExecuteCommand(
        CombatSceneRuntime runtime,
        CombatActorImage image,
        GameState state,
        uint milliseconds,
        bool globalEffectBusy,
        Action<int> playSound,
        Action<int> showRankNotice,
        Action<int> showConsumedItemNotice
    )
    {
        CombatActorState actor = image.State;
        var info = image.Binding!.Info;
        int category = actor.CommandCategory;
        if (category == 0)
        {
            playSound(info.CommandSound);
            int rank = actor.CommandSelection;
            showRankNotice(rank);
            for (int i = runtime.Scene.PartyStartIndex; i < runtime.Actors.Count; i++)
            {
                runtime.Actors[i].State.ControlRank = rank;
                runtime.Actors[i].State.CommandSelection = rank;
            }

            return;
        }

        if (globalEffectBusy)
        {
            return;
        }

        int first = 0, count = info.Commands.Count;
        while (first < count && info.Commands[first].Category != category)
        {
            first++;
        }

        if (first == count)
        {
            return;
        }

        int index = unchecked(first + actor.CommandSelection + actor.CommandScroll);
        if ((uint)index >= (uint)count)
        {
            throw new InvalidDataException("Selected combat P command exceeds actual INF records.");
        }

        var command = info.Commands[index];
        int cost = command.ManaCost, mp = actor.Mana;
        if (mp < cost)
        {
            int item = 64, slot = -1;
            if (unchecked(mp + 100) >= cost)
            {
                slot = EquipmentSlot(actor, 64);
            }

            if (slot < 0)
            {
                item = 65;
                if (unchecked(mp + 200) < cost)
                {
                    return;
                }

                slot = EquipmentSlot(actor, 65);
                if (slot < 0)
                {
                    return;
                }
            }

            mp = unchecked(mp + (item == 64 ? 100 : 200));
            actor.Mana = mp;
            actor.Equipment[slot] = -1;
            state.OwnedItems[item] = unchecked(state.OwnedItems[item] - 1);
            showConsumedItemNotice(item == 64 ? 4 : 5);
        }

        mp = unchecked(mp - cost);
        actor.Mana = mp;
        actor.DisplayedMana = mp;
        image.CurrentCommandIndex = index;
        image.Animation!.BeginSequenceCommand(command.Animation, 28, milliseconds);
    }

    public static void ContinueAction(CombatActorImage image, ReadOnlySpan<byte> controls, CombatScanInput input, uint milliseconds)
    {
        CombatActorState actor = image.State;
        var info = image.Binding!.Info;
        var cursor = image.Animation!;
        if ((actor.ActionFlags & (int)CombatActionFlags.CanChain) == 0)
        {
            return;
        }

        for (int index = info.FirstVariation; index < info.VariationEnd; index++)
        {
            var attack = info.Attacks[index];
            byte forward = unchecked((sbyte)actor.Facing) == -1 ? controls[0] : controls[1];
            byte back = unchecked((sbyte)actor.Facing) == -1 ? controls[1] : controls[0];
            if (actor.ActionFlags != attack.RequiredFlags)
            {
                continue;
            }

            if (!input.IsHeld(controls[5]))
            {
                if (input.IsHeld(controls[4]) && actor.Motion == (int)CombatMotionKind.Idle && actor.JumpStage is > 15 and < 18)
                {
                    cursor.BeginVariation(index, milliseconds);
                }

                continue;
            }

            if (actor.JumpStage != 0 && actor.Motion != (int)CombatMotionKind.Idle)
            {
                continue;
            }

            int count = 0;
            for (int direction = 0; direction < 4; direction++)
            {
                if (input.IsHeld(controls[direction]))
                {
                    count++;
                }
            }

            int command = attack.Direction;
            byte selected = 0;
            bool match = false;
            if (count == 1)
            {
                switch (command)
                {
                    case 2:
                        selected = controls[3];
                        match = true;
                        break;
                    case 4:
                        selected = back;
                        match = true;
                        break;
                    case 6:
                        selected = forward;
                        match = true;
                        break;
                    case 8:
                        selected = controls[2];
                        match = true;
                        break;
                }
            }
            else if (count == 2)
            {
                switch (command)
                {
                    case 1:
                        if (input.IsHeld(controls[3]))
                        {
                            selected = back;
                            match = true;
                        }

                        break;
                    case 3:
                        if (input.IsHeld(forward))
                        {
                            selected = controls[3];
                            match = true;
                        }

                        break;
                    case 7:
                        if (input.IsHeld(back))
                        {
                            selected = controls[2];
                            match = true;
                        }

                        break;
                    case 9:
                        if (input.IsHeld(controls[2]))
                        {
                            selected = forward;
                            match = true;
                        }

                        break;
                }
            }

            if (match && input.IsHeld(selected))
            {
                CombatHitReaction.CancelOffsetMotion(image);
                cursor.BeginVariation(index, milliseconds);
                return;
            }
        }
    }

    public static void Attack(CombatActorState actor, CombatInfoCharacter info, CombatAnimationCursor cursor, uint milliseconds)
    {
        actor.CurrentDirectionScan = 0;
        int type = info.Type;
        if (actor.Motion == (int)CombatMotionKind.Run && type != 38 && (type < 40 || type > 43))
        {
            int count = info.VariationEnd;
            if (count > 0)
            {
                cursor.BeginAttack(count - 1, milliseconds);
            }

            return;
        }

        if (actor.ChainMode == 100 && unchecked((int)milliseconds) < actor.ChainDeadline)
        {
            cursor.BeginAttack(actor.AttackIndex + 1, milliseconds);
        }
        else
        {
            cursor.BeginAttack(0, milliseconds);
        }
    }

    public static void Walk(
        CombatActorState actor,
        CombatInfoCharacter info,
        CombatAnimationCursor cursor,
        ReadOnlySpan<byte> controls,
        CombatScanInput input,
        uint milliseconds,
        bool? run = null
    )
    {
        if (controls.Length != 8)
        {
            throw new ArgumentException("Incomplete combat actor or control binding.");
        }

        int now = unchecked((int)milliseconds);
        bool idle = true;
        sbyte horizontal = 0, vertical = 0;
        while (true)
        {
            int mode = 0;
            if (!input.IsHeld(controls[4]))
            {
                if (input.IsHeld(controls[1]))
                {
                    idle = false;
                    horizontal = 1;
                    mode = run is { } requestedRun ? (requestedRun ? 2 : 1) : actor.Motion == (int)CombatMotionKind.Run ? 2 : 1;
                    actor.CurrentDirectionScan = mode == 2 ? 0 : controls[1];
                }
                else if (input.IsHeld(controls[0]))
                {
                    idle = false;
                    horizontal = -1;
                    mode = run is { } requestedRun ? (requestedRun ? 2 : 1) : actor.Motion == (int)CombatMotionKind.Run ? 2 : 1;
                    actor.CurrentDirectionScan = mode == 2 ? 0 : controls[0];
                }

                if (input.IsHeld(controls[2]))
                {
                    idle = false;
                    vertical = -1;
                    actor.CurrentDirectionScan = 0;
                    mode = run is { } requestedRun ? (requestedRun ? 2 : 1) : actor.Motion == (int)CombatMotionKind.Run ? 2 : 1;
                }
                else if (input.IsHeld(controls[3]))
                {
                    idle = false;
                    vertical = 1;
                    actor.CurrentDirectionScan = 0;
                    mode = run is { } requestedRun ? (requestedRun ? 2 : 1) : actor.Motion == (int)CombatMotionKind.Run ? 2 : 1;
                }
            }

            if (!idle)
            {
                actor.Motion = mode;
                if (unchecked((sbyte)actor.Facing) != horizontal && horizontal != 0)
                {
                    actor.Facing = unchecked((byte)horizontal);
                    actor.AnchorX = unchecked(actor.AnchorX - cursor.CurrentSprite.Width * horizontal);
                    cursor.BeginMotion(4, milliseconds);
                    return;
                }

                horizontal = unchecked((sbyte)(info.Motions[mode].StepX * horizontal));
                vertical = unchecked((sbyte)(info.Motions[mode].StepY * vertical));
            }
            else
            {
                mode = actor.Motion == (int)CombatMotionKind.Run ? 3 : 0;
                actor.Motion = mode;
                if (mode == 3)
                {
                    actor.ActionFlags = (int)CombatActionFlags.Busy;
                }
            }

            if (!cursor.IsMotionSequence(mode))
            {
                if (run is null && mode == 1 && actor.PreviousDirectionScan != 0 && actor.CurrentDirectionScan == actor.PreviousDirectionScan && actor.RunTapDeadline > now)
                {
                    actor.Motion = (int)CombatMotionKind.Run;
                    actor.RunTapDeadline = 0;
                    actor.PreviousDirectionScan = 0;
                    // Native restarts input decisions with the already accumulated direction bytes.
                    continue;
                }

                if (mode != 1 || actor.PreviousDirectionScan == 0)
                {
                    actor.PreviousDirectionScan = actor.CurrentDirectionScan;
                    actor.RunTapDeadline = unchecked(now + 180);
                }
                else
                {
                    actor.PreviousDirectionScan = 0;
                    actor.RunTapDeadline = 0;
                }

                cursor.BindManualMotion(mode, milliseconds);
            }

            if (cursor.FrameIndex >= cursor.SequenceFrameCount)
            {
                actor.FrameIndex = 0;
            }

            if (actor.MovementDeadline < now && (cursor.CurrentFrame.FinalDword & 2) == 0)
            {
                actor.AnchorX = unchecked(actor.AnchorX + horizontal);
                actor.Depth = unchecked(actor.Depth + vertical);
                actor.MovementDeadline = unchecked(actor.MovementInterval + now);
            }

            return;
        }
    }

    private static int EquipmentSlot(CombatActorState actor, int item)
    {
        for (int i = 0; i < 6; i++)
        {
            if (actor.Equipment[i] == item)
            {
                return i;
            }
        }

        return -1;
    }
}
