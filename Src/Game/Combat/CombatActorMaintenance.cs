using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Small recovered per-actor stages; caller owns native actor visitation/order.</summary>
public static class CombatActorMaintenance
{
    public static void RegenerateAndUpdateDisplay(CombatActorState actor, uint milliseconds)
    {
        unchecked
        {
            int now = (int)milliseconds;
            if (actor.RecoverableHealth > 0 && actor.HealthRecoveryDeadline < now)
            {
                actor.Health = actor.Health + 1;
                actor.RecoverableHealth = actor.RecoverableHealth - 1;
                actor.HealthRecoveryDeadline = actor.HealthRecoveryInterval + now;
            }

            int shown = actor.DisplayedHealth, hp = actor.Health;
            if (hp + 10 < shown)
            {
                actor.DisplayedHealth = shown - 10;
            }
            else if (hp < shown)
            {
                actor.DisplayedHealth = shown - 1;
            }
        }
    }

    public static int ModifyActionInterval(CombatActorState actor, int interval)
    {
        return actor.Haste != 0 ? interval >> 1 : interval;
    }

    public static void CaptureSecondaryAction(CombatActorState actor, AnimationFrame frame)
    {
        if (frame.Words.Length != Atria.Render.Codecs.AnimationFrame.WordCount)
        {
            throw new ArgumentException("Incomplete combat animation frame.");
        }

        if (unchecked((short)frame.SecondaryEffect) == 0)
        {
            return;
        }

        actor.SecondaryEffectDuration = frame.EffectDuration;
        actor.SecondaryEffect = frame.SecondaryEffect;
    }

}
