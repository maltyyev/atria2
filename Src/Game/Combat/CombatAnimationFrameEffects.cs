using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>406010 displacement and405640 bound-frame effects, before4061b0 increments its frame counter.</summary>
public static class CombatAnimationFrameEffects
{
    public static void ApplyDisplacement(CombatActorState actor)
    {
        unchecked
        {
            if (actor.Motion == 3)
            {
                Move(actor, (sbyte)actor.Facing);
            }

            int reaction = actor.Reaction, velocity = actor.HorizontalVelocity;
            if (reaction is 4 or 5 or 7)
            {
                Move(actor, velocity);
                Decelerate(actor, velocity);
                return;
            }

            if (reaction is 10 or 13)
            {
                Move(actor, velocity);
                if (velocity > 0)
                {
                    actor.HorizontalVelocity = velocity - 1;
                }

                return;
            }

            if (reaction == 14 && actor.VelocityDecay > 0)
            {
                int count = actor.VelocityDecay - 1;
                actor.VelocityDecay = count;
                actor.SpriteX = actor.SpriteX + velocity;
                if ((count & 1) != 0 && velocity > 1)
                {
                    actor.HorizontalVelocity = velocity - 1;
                }
            }

            if (actor.Motion == 14)
            {
                velocity = actor.HorizontalVelocity;
                Move(actor, velocity);
                Decelerate(actor, velocity);
            }
        }
    }

    public static void ApplyEvents(
        CombatActorState actor,
        CombatInfoCharacter info,
        AnimationFrame frame,
        int visualDetail,
        Func<int> nextNativeRandom,
        Action<int> playNumericSound,
        Action<int> spawnParticle,
        Action<int, int> applyFrameEffect
    )
    {
        if (frame.Words.Length != Atria.Render.Codecs.AnimationFrame.WordCount)
        {
            throw new InvalidDataException("Incomplete actual combat animation frame.");
        }

        actor.HitLatch = 1;
        int sound = frame.Sound;
        if (sound != 0)
        {
            playNumericSound(sound);
        }

        int soundIndex = frame.CharacterSoundSlot;
        if (soundIndex != 0)
        {
            // Native has nine inline DWORD slots; b30 after them is a runtime pointer.
            if ((uint)soundIndex >= 9)
            {
                throw new InvalidDataException("Combat frame sound index exceeds native inline INF slots.");
            }

            playNumericSound(info.Sounds[soundIndex]);
        }

        if (visualDetail < 3 && frame.Particle != 0)
        {
            spawnParticle(frame.Particle);
        }

        if ((frame.FinalDword & 4) != 0)
        {
            actor.Health = 0;
            actor.Rank = 0;
            actor.Active = 0;
        }

        if (actor.Invulnerable != 0)
        {
            actor.Invulnerable = unchecked(actor.Invulnerable - 1);
        }

        if (actor.Poison != 0)
        {
            int roll = nextNativeRandom();
            if ((uint)roll > 32767)
            {
                throw new InvalidOperationException("Combat RNG must supply native15-bit samples.");
            }

            if ((roll & 255) < 30 && actor.Health > 1)
            {
                actor.Health = actor.Health - 1;
            }
        }

        if (frame.Effect != 0)
        {
            applyFrameEffect(frame.Effect, frame.EffectArgument);
        }
    }

    private static void Move(CombatActorState actor, int delta)
    {
        actor.SpriteX = unchecked(actor.SpriteX + delta);
        actor.AnchorX = unchecked(actor.AnchorX + delta);
    }

    private static void Decelerate(CombatActorState actor, int velocity)
    {
        if (velocity > 1)
        {
            actor.HorizontalVelocity = velocity - 1;
        }
        else if (velocity < -1)
        {
            actor.HorizontalVelocity = velocity + 1;
        }
    }

}
