using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

public sealed class CombatCameraShake
{
    public int Amplitude { get; private set; }
    public int Remaining { get; private set; }
    public int Phase { get; private set; }
    public void ApplyFrame(uint flags)
    {
        if ((flags & 0x100) != 0) { Amplitude = 5; Remaining = 10; }
        else if ((flags & 0x200) != 0) { Amplitude = 10; Remaining = 15; }
        else if ((flags & 0x400) != 0) { Amplitude = 15; Remaining = 20; }
        else if ((flags & 0x800) != 0) { Amplitude = 20; Remaining = 25; }
        else return;
        Phase = 0;
    }
    public int AdvanceVisual()
    {
        if(Remaining==0) return 0;
        Phase=unchecked(Phase+1);int offset=(Phase&3)<2?Amplitude:0;
        if(Remaining<=Phase) { Amplitude=0;Remaining=0;Phase=0; }
        return offset;
    }
}

/// <summary>4061b0 ordered frame advance against actual animation, reaction and jump bindings.</summary>
public static class CombatAnimationRuntime
{
    public static void Advance(CombatActorImage image, GameState state, uint milliseconds, int visualDetail,
        Func<int> nextNativeRandom, Action<int> playSound, Action<int> spawnParticle,
        Action<int, int> applyFrameEffect, Action<int> showConsumedItemNotice,
        Action cycleControlledActor, CombatCameraShake camera)
    {
        byte[] actor = image.Fields;
        CombatAnimationCursor cursor = image.Animation ?? throw new InvalidOperationException("Combat actor has no actual animation binding.");
        CombatInfoCharacter info = image.Binding?.Info ?? throw new InvalidOperationException("Combat actor has no actual INF binding.");
        CombatHitEligibility.RebuildRectangles(actor, cursor.CurrentSprite);
        if (I(actor, 0xb9) != 0) { CombatJumpRuntime.Step(actor, info, cursor, milliseconds); return; }
        int now = unchecked((int)milliseconds);
        if (I(actor, 0xbd) > now) return;
        W(actor, 0xbd, unchecked(I(actor, 0xc1) + now));
        CombatAnimationFrameEffects.ApplyDisplacement(actor);
        CombatAnimationFrameEffects.ApplyEvents(actor, info, cursor.CurrentFrame, visualDetail, nextNativeRandom,
            playSound, spawnParticle, applyFrameEffect);
        if ((I(actor, 0xad) & 0x600) != 0) { CombatAirborneReaction.Advance(actor, cursor, spawnParticle); return; }
        WS(actor, 0xd5, unchecked((short)(S(actor, 0xd5) + 1)));
        if (I(actor, 0x54) != 0) cursor.BeginSequenceCommand(I(actor, 0x54), 30, milliseconds);
        camera.ApplyFrame(cursor.CurrentFrame.FinalDword); W(actor, 0x341, 0);
        if (S(actor, 0xd5) < cursor.SequenceFrameCount) return;
        W(actor, 0x11c, 0); WS(actor, 0xd5, 0);
        if (I(actor, 0xb1) == 11 && I(actor, 0x1ec) == 0) { W(actor, 0x224, 0); return; }
        if (I(actor, 0xb1) == 15) { CombatJumpRuntime.Transition(actor, info, cursor, 16, milliseconds); return; }
        int reaction = I(actor, 0xf6);
        if (reaction is 7 or 10 or 13 or 15 or 16 or 17)
        {
            if (!CombatDownedRecovery.Complete(actor, info, cursor, state, nextNativeRandom, playSound,
                showConsumedItemNotice, cycleControlledActor)) return;
        }
        else if ((I(actor, 0xad) & 0x140) != 0)
        {
            int anchor = I(actor, 0x7c);
            if (unchecked((sbyte)actor[0xfa]) == -1) anchor = unchecked(anchor + cursor.CurrentSprite.Width);
            W(actor, 0x70, anchor);
        }
        if (I(actor, 0xad) != 1) W(actor, 0xad, 0);
        W(actor, 0x54, 0); W(actor, 0xf6, 0);
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static short S(byte[] actor, int offset) => BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(offset, 2));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
    private static void WS(byte[] actor, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset, 2), value);
}
