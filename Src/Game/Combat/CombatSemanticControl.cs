using System.Buffers.Binary;
using Atria.Game.Controls;
using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>Semantic requests preserve the original manual eligibility and execute exactly one transition.</summary>
public static class CombatSemanticControl
{
    public static bool Variation(CombatActorImage image, int direction, int requiredFlags, uint now)
    {
        CombatActorState a = image.State;
        var info = image.Binding!.Info;
        int flags = a.ActionFlags;
        if ((flags & 32) == 0 || (a.JumpStage != 0 && a.Motion != (int)CombatMotionKind.Idle))
        {
            return false;
        }

        if (requiredFlags != 0 && requiredFlags != flags)
        {
            return false;
        }

        for (int i = info.FirstVariation; i < info.VariationEnd; i++)
        {
            var attack = info.Attacks[i];
            if (attack.RequiredFlags != flags || attack.Direction != direction)
            {
                continue;
            }

            CombatHitReaction.CancelOffsetMotion(image);
            image.Animation!.BeginVariation(i, now);
            return true;
        }

        return false;
    }

    public static bool AirAttack(CombatActorImage image, uint now)
    {
        CombatActorState a = image.State;
        var info = image.Binding!.Info;
        int flags = a.ActionFlags;
        if ((flags & 32) == 0 || a.Motion != (int)CombatMotionKind.Idle || a.JumpStage is not (16 or 17))
        {
            return false;
        }

        bool result = false;
        // Original loop can visit duplicate rows; preserve its final binding.
        for (int i = info.FirstVariation; i < info.VariationEnd; i++)
        {
            if (a.ActionFlags == info.Attacks[i].RequiredFlags)
            {
                image.Animation!.BeginVariation(i, now);
                result = true;
            }
        }

        return result;
    }

    public static bool Execute(
        string id,
        CombatSceneRuntime runtime,
        CombatActorImage image,
        GameState state,
        uint now,
        bool busy,
        Action<int> sound,
        Action<int> rank,
        Action<int> item
    )
    {
        CombatActorState a = image.State;
        var info = image.Binding!.Info;
        if (id.StartsWith("v:", StringComparison.Ordinal))
        {
            string[] parts = id.Split(':');
            return parts.Length is 2 or 3 && int.TryParse(parts[1], out int d)
&& Variation(image, d, parts.Length == 3 && int.TryParse(parts[2], out int f) ? f : 0, now);
        }

        if (a.ActionFlags >= 2)
        {
            return false;
        }

        if (id.StartsWith("s:", StringComparison.Ordinal) && int.TryParse(id.AsSpan(2), out int strategy) && strategy is >= 0 and < 5)
        {
            a.CommandCategory = 0;
            a.CommandSelection = strategy;
            a.CommandScroll = 0;
            CombatManualControl.ExecuteCommand(runtime, image, state, now, busy, sound, rank, item);
            return true;
        }

        int index = MoveCatalog.FindCommand(info, id);
        if (index < 0 || busy)
        {
            return false;
        }

        int category = info.Commands[index].Category;
        if (category > a.MagicRank)
        {
            return false;
        }

        int first = index;
        while (first > 0 && info.Commands[(first - 1)].Category == category)
        {
            first--;
        }

        int oldCat = a.CommandCategory, oldRank = a.CommandSelection, oldOffset = a.CommandScroll;
        int oldMp = a.Mana;
        a.CommandCategory = category;
        a.CommandSelection = index - first;
        a.CommandScroll = 0;
        CombatManualControl.ExecuteCommand(runtime, image, state, now, busy, sound, rank, item);
        bool fired = image.CurrentCommandIndex == index && a.Motion == (int)CombatMotionKind.Attack && a.ActionFlags == (int)CombatActionFlags.Command;
        if (!fired && oldMp == a.Mana)
        {
            a.CommandCategory = oldCat;
            a.CommandSelection = oldRank;
            a.CommandScroll = oldOffset;
        }

        return fired;
    }

    public static int Direction(InputRouter input, int player, byte facing)
    {
        int x = (input.Held(player, GameAction.Right) ? 1 : 0) - (input.Held(player, GameAction.Left) ? 1 : 0);
        int y = (input.Held(player, GameAction.Up) ? 1 : 0) - (input.Held(player, GameAction.Down) ? 1 : 0);
        if (facing == 255)
        {
            x = -x;
        }

        return (x, y) switch
        {
            (-1, -1) => 1,
            (0, -1) => 2,
            (1, -1) => 3,
            (-1, 0) => 4,
            (1, 0) => 6,
            (-1, 1) => 7,
            (0, 1) => 8,
            (1, 1) => 9,
            _ => 0
        };
    }

}
