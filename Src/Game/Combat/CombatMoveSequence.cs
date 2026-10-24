using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>One requested directional move, driven by original ANI progress and INF prerequisites.</summary>
public sealed class CombatMoveSequence
{
    private CombatActorImage? actor;
    private int[] steps = [];
    private int next, sequence, flags;
    private uint began;
    public bool Active => actor is not null;

    public void Cancel()
    {
        actor = null;
        steps = [];
        next = 0;
    }

    public static int[]? Plan(CombatInfoCharacter info, int initialFlags, string id)
    {
        string[] parts = id.Split(':');
        if (parts.Length is not (2 or 3) || parts[0] != "v" || !int.TryParse(parts[1], out int direction))
        {
            return null;
        }

        int required = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out required))
        {
            return null;
        }

        var pending = new Queue<(int Flags, int[] Path)>();
        var seen = new HashSet<int>
        {
            initialFlags
        };
        pending.Enqueue((initialFlags, []));
        while (pending.TryDequeue(out var node))
        {
            if (((CombatActionFlags)node.Flags & CombatActionFlags.CanChain) == 0)
            {
                continue;
            }

            foreach (int i in info.DirectionalVariations(node.Flags))
            {
                var attack = info.Attacks[i];
                int d = attack.Direction;
                int[] path = [.. node.Path, i];
                if (d == direction && (required == 0 || required == node.Flags))
                {
                    return path;
                }

                int after = attack.ResultFlags;
                if (seen.Add(after))
                {
                    pending.Enqueue((after, path));
                }
            }
        }

        return null;
    }

    public bool Start(CombatActorImage image, string id, uint now)
    {
        Cancel();
        var a = image.State;
        var info = image.Binding!.Info;
        if (!a.ActionState.CanStartGroundAttack)
        {
            return false;
        }

        if (Plan(info, info.Attacks[0].ResultFlags, id) is null)
        {
            return false;
        }

        CombatManualControl.Attack(a, info, image.Animation!, now);
        var plan = Plan(info, (int)a.ActionState.Flags, id);
        if (plan is null)
        {
            return false;
        }

        actor = image;
        steps = plan;
        next = 0;
        Remember(now);
        return true;
    }

    public bool Advance(CombatActorImage image, uint now)
    {
        if (actor is null)
        {
            return false;
        }

        var state = image.State.ActionState;
        if (!ReferenceEquals(actor, image) || image.Animation!.SequenceIndex != sequence || (int)state.Flags != flags
            || state.Motion != CombatMotionKind.Attack || !state.GroundedAndAlive)
        {
            Cancel();
            return false;
        }

        // Do not chain at render frequency or skip the prerequisite's wind-up frames.
        var animation = image.Animation;
        if (animation.FrameIndex < animation.SequenceFrameCount - 1 || unchecked((int)(now - began)) < Math.Max(1, state.FrameInterval))
        {
            return true;
        }

        var info = image.Binding!.Info;
        var attack = info.Attacks[steps[next]];
        if (!CombatSemanticControl.Variation(image, attack.Direction, attack.RequiredFlags, now))
        {
            Cancel();
            return false;
        }

        if (++next == steps.Length)
        {
            Cancel();
        }
        else
        {
            Remember(now);
        }

        return true;
    }

    private void Remember(uint now)
    {
        sequence = actor!.Animation!.SequenceIndex;
        flags = (int)actor.State.ActionState.Flags;
        began = now;
    }

}
