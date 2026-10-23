using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>406010 displacement and405640 bound-frame effects, before4061b0 increments its frame counter.</summary>
public static class CombatAnimationFrameEffects
{
    public static void ApplyDisplacement(Span<byte> actor)
    {
        Validate(actor);
        unchecked
        {
            if (I(actor, 0xb1) == 3) Move(actor, (sbyte)actor[0xfa]);
            int reaction = I(actor, 0xf6), velocity = I(actor, 0xe6);
            if (reaction is 4 or 5 or 7)
            {
                Move(actor, velocity); Decelerate(actor, velocity); return;
            }
            if (reaction is 10 or 13)
            {
                Move(actor, velocity);
                if (velocity > 0) W(actor, 0xe6, velocity - 1);
                return;
            }
            if (reaction == 14 && I(actor, 0xea) > 0)
            {
                int count = I(actor, 0xea) - 1; W(actor, 0xea, count);
                W(actor, 0x7c, I(actor, 0x7c) + velocity);
                if ((count & 1) != 0 && velocity > 1) W(actor, 0xe6, velocity - 1);
            }
            if (I(actor, 0xb1) == 14)
            {
                velocity = I(actor, 0xe6); Move(actor, velocity); Decelerate(actor, velocity);
            }
        }
    }

    public static void ApplyEvents(Span<byte> actor, CombatInfoCharacter info, AnimationFrame frame,
        int visualDetail, Func<int> nextNativeRandom, Action<int> playNumericSound,
        Action<int> spawnParticle, Action<int, int> applyFrameEffect)
    {
        Validate(actor);
        if (frame.Words.Length != 28) throw new InvalidDataException("Incomplete actual combat animation frame.");
        actor[0xdd] = 1;
        int sound = frame.Words[7];
        if (sound != 0) playNumericSound(sound);
        int soundIndex = frame.Words[17];
        if (soundIndex != 0)
        {
            // Native has nine inline DWORD slots; b30 after them is a runtime pointer.
            if ((uint)soundIndex >= 9) throw new InvalidDataException("Combat frame sound index exceeds native inline INF slots.");
            playNumericSound(info.Read(0xad0 + soundIndex * 4));
        }
        if (visualDetail < 3 && frame.Words[18] != 0) spawnParticle(frame.Words[18]);
        if ((frame.FinalDword & 4) != 0)
        {
            W(actor, 0x170, 0); W(actor, 0x1ec, 0); W(actor, 0x224, 0);
        }
        if (I(actor, 0x240) != 0) W(actor, 0x240, unchecked(I(actor, 0x240) - 1));
        if (I(actor, 0x2b4) != 0)
        {
            int roll = nextNativeRandom();
            if ((uint)roll > 32767) throw new InvalidOperationException("Combat RNG must supply native15-bit samples.");
            if ((roll & 255) < 30 && I(actor, 0x170) > 1) W(actor, 0x170, I(actor, 0x170) - 1);
        }
        if (frame.Words[20] != 0) applyFrameEffect(frame.Words[20], frame.Words[19]);
    }

    private static void Move(Span<byte> actor, int delta)
    {
        W(actor, 0x7c, unchecked(I(actor, 0x7c) + delta)); W(actor, 0x70, unchecked(I(actor, 0x70) + delta));
    }
    private static void Decelerate(Span<byte> actor, int velocity)
    {
        if (velocity > 1) W(actor, 0xe6, velocity - 1);
        else if (velocity < -1) W(actor, 0xe6, velocity + 1);
    }
    private static int I(ReadOnlySpan<byte> actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.Slice(offset, 4));
    private static void W(Span<byte> actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.Slice(offset, 4), value);
    private static void Validate(Span<byte> actor)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Invalid combat actor image.");
    }
}
