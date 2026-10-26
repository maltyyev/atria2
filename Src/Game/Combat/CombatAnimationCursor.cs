using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Managed ANI bindings for408b40. No native pointers are stored in actor images.</summary>
public sealed class CombatAnimationCursor(CombatActorState actor, CombatInfoCharacter info, CombatAnimationAssets assets)
{
    private int sequence = -1;
    private int boundFrame;
    private int auxiliarySequence = -1;
    private SpriteFrame? displayedSprite;
    private AnimationFrame? frameOverride;
    public int SequenceIndex => sequence;
    public CombatInfoCharacter Info => info;
    public int BoundFrameIndex => boundFrame;

    public int FrameIndex
    {
        get
        {
            return actor.FrameIndex;
        }
    }

    public AnimationFrame CurrentFrame
    {
        get
        {
            if (frameOverride is not null)
            {
                return frameOverride;
            }

            if ((uint)sequence >= (uint)assets.Animation.Sequences.Length)
            {
                throw new InvalidOperationException("Actor has no actual bound animation sequence.");
            }

            AnimationFrame[] frames = assets.Animation.Sequences[sequence].Frames;
            int index = boundFrame;
            if ((uint)index >= (uint)frames.Length)
            {
                throw new InvalidDataException("Combat animation cursor exceeds its actual sequence.");
            }

            return frames[index];
        }
    }

    public SpriteFrame CurrentSprite
    {
        get
        {
            if (displayedSprite is not null)
            {
                return displayedSprite;
            }

            int index = CurrentFrame.SpriteFrameIndex;
            if ((uint)index >= (uint)assets.Sprites.Frames.Length)
            {
                throw new InvalidDataException("Combat ANI refers to an absent sprite frame.");
            }

            return assets.Sprites.Frames[index];
        }
    }

    //409070 compares sequence pointers, then changes deadlines/frame binding only on a new sequence.
    public bool IsMotionSequence(int mode) => sequence == info.Motions[mode].Animation;
    public int SequenceFrameCount => assets.Animation.Sequences[sequence].Frames.Length;

    public void BindManualMotion(int mode, uint milliseconds)
    {
        BindMotion(mode);
        actor.FrameIndex = 0;
        actor.MovementInterval = info.Motions[mode].MovementInterval;
        actor.MovementDeadline = unchecked(info.Motions[mode].MovementInterval + (int)milliseconds);
        int interval = CombatActorMaintenance.ModifyActionInterval(actor, info.Motions[mode].FrameInterval);
        actor.FrameInterval = interval;
        actor.AnimationDeadline = unchecked(interval + (int)milliseconds);
    }

    //4073a0: direct sequence command uses this actor's actual animation group.
    public void BeginSequenceCommand(int selected, int interval, uint milliseconds)
    {
        ValidateSequence(selected);
        sequence = selected;
        boundFrame = 0;
        displayedSprite = null;
        frameOverride = null;
        actor.Motion = (int)CombatMotionKind.Attack;
        actor.FrameIndex = 0;
        actor.ActionFlags = (int)CombatActionFlags.Command;
        actor.ActionSpriteHeight = CurrentSprite.Height;
        actor.ChainMode = 0;
        int adjusted = CombatActorMaintenance.ModifyActionInterval(actor, interval);
        actor.FrameInterval = adjusted;
        actor.AnimationDeadline = unchecked(adjusted + (int)milliseconds);
    }

    //405260 binds reaction frame0 without changing the live D5 counter or deadline.
    public void BindReactionSequence(bool rebuildAnchor = true, bool bindLiveCounter = false)
    {
        int reaction = actor.Reaction;
        if ((uint)reaction >= (uint)info.Reactions.Count)
        {
            throw new InvalidDataException("Combat reaction exceeds actual INF records.");
        }

        int selected = info.Reactions[reaction].Animation;
        ValidateSequence(selected);
        sequence = selected;
        boundFrame = 0;
        displayedSprite = null;
        frameOverride = null;
        if (FrameIndex < 0)
        {
            throw new InvalidDataException("Reaction frame cannot be negative.");
        }
        // Some attackers request entry frame 2, but a victim's reaction can contain only one frame.
        if (FrameIndex >= assets.Animation.Sequences[selected].Frames.Length)
        {
            actor.FrameIndex = 0;
        }
        if (bindLiveCounter)
        {
            boundFrame = FrameIndex;
        }

        if (!rebuildAnchor)
        {
            return;
        }

        int x = actor.SpriteX;
        if (unchecked((sbyte)actor.Facing) == -1)
        {
            x = unchecked(x + CurrentSprite.Width);
        }

        actor.AnchorX = x;
    }

    //4063e0 changes2c5 while leaving the bound2d5/2d1 pointers intact until projection.
    public void SelectGuardSequence()
    {
        var attack = info.Guard;
        AnimationFrame oldFrame = CurrentFrame;
        SpriteFrame oldSprite = CurrentSprite;
        int selected = attack.Animation;
        ValidateSequence(selected);
        sequence = selected;
        frameOverride = oldFrame;
        displayedSprite = oldSprite;
        actor.AttackIndex = 0;
        actor.FrameIndex = 0;
    }

    public void BeginVariation(int index, uint milliseconds)
    {
        if (index < info.FirstVariation || index >= info.VariationEnd)
        {
            throw new InvalidDataException("Combat variation exceeds actual INF V records.");
        }

        var attack = info.Attacks[index];
        int selected = attack.Animation;
        ValidateSequence(selected);
        sequence = selected;
        boundFrame = 0;
        frameOverride = null;
        displayedSprite = null;
        actor.Motion = (int)CombatMotionKind.Attack;
        actor.AttackIndex = 0;
        actor.FrameIndex = 0;
        actor.ActionFlags = attack.ResultFlags;
        actor.ActionSpriteHeight = CurrentSprite.Height;
        int interval = CombatActorMaintenance.ModifyActionInterval(actor, attack.FrameInterval);
        actor.FrameInterval = interval;
        actor.AnimationDeadline = unchecked(interval + (int)milliseconds);
    }

    public void BeginMotion(int mode, uint milliseconds)
    {
        if ((uint)mode >= (uint)info.Motions.Count)
        {
            throw new InvalidDataException("Combat motion is outside actual INF motion records.");
        }

        int selected = info.Motions[mode].Animation;
        if ((uint)selected >= (uint)assets.Animation.Sequences.Length || assets.Animation.Sequences[selected].Frames.Length == 0)
        {
            throw new InvalidDataException("Combat motion refers to an absent ANI sequence.");
        }

        int sprite = assets.Animation.Sequences[selected].Frames[0].SpriteFrameIndex;
        if ((uint)sprite >= (uint)assets.Sprites.Frames.Length)
        {
            throw new InvalidDataException("Combat motion refers to an absent sprite frame.");
        }

        int interval = CombatActorMaintenance.ModifyActionInterval(actor, info.Motions[mode].FrameInterval);
        sequence = selected;
        boundFrame = 0;
        displayedSprite = null;
        frameOverride = null;
        actor.Motion = mode;
        actor.FrameIndex = 0;
        actor.ActionFlags = (int)CombatActionFlags.Busy;
        actor.ActionSpriteHeight = assets.Sprites.Frames[sprite].Height;
        actor.FrameInterval = interval;
        actor.AnimationDeadline = unchecked(interval + (int)milliseconds);
        // Native2c5/2d5/2d1 sequence/frame/sprite pointers are represented by this managed binding.
    }

    //411860 intentionally does not update84 or the existing animation deadlineBD.
    public void BeginAutomaticIdle(int mode)
    {
        BindMotion(mode);
        actor.Motion = mode;
        actor.ActionFlags = (int)CombatActionFlags.Busy;
        actor.FrameInterval = CombatActorMaintenance.ModifyActionInterval(actor, info.Motions[mode].FrameInterval);
    }

    //40d7b0 called after automatic movement chooses its actual INF motion record.
    public void BeginAutomaticMovement(int mode, int dx, int dy, uint milliseconds)
    {
        int xStep = info.Motions[mode].StepX, yStep = info.Motions[mode].StepY;
        if (xStep == 0 || yStep == 0)
        {
            throw new InvalidDataException("Combat INF movement divisor is zero.");
        }

        int nx = dx / xStep, ny = dy / yStep;
        BindMotion(mode);
        actor.Motion = mode;
        actor.ActionFlags = 1;
        actor.MovementStepX = dx < 0 ? unchecked(-xStep) : xStep;
        actor.MovementStepY = dy < 0 ? unchecked(-yStep) : yStep;
        actor.MovementTicksX = unchecked((short)(unchecked((short)((nx ^ (nx >> 31)) - (nx >> 31)))));
        actor.MovementTicksY = unchecked((short)(unchecked((short)((ny ^ (ny >> 31)) - (ny >> 31)))));
        int movementInterval = info.Motions[mode].MovementInterval;
        actor.MovementInterval = movementInterval;
        actor.MovementDeadline = unchecked(movementInterval + (int)milliseconds);
        int animationInterval = CombatActorMaintenance.ModifyActionInterval(actor, info.Motions[mode].FrameInterval);
        actor.FrameInterval = animationInterval;
        actor.AnimationDeadline = unchecked(animationInterval + (int)milliseconds);
    }

    public void BeginAttack(int index, uint milliseconds)
    {
        int count = Math.Max(info.BasicAttackCount, info.VariationEnd);
        if ((uint)index >= (uint)count)
        {
            throw new InvalidDataException("Combat attack exceeds actual INF records.");
        }

        var attack = info.Attacks[index];
        int selected = attack.Animation;
        ValidateSequence(selected);
        sequence = selected;
        boundFrame = 0;
        displayedSprite = null;
        frameOverride = null;
        actor.AttackIndex = unchecked((short)(unchecked((short)index)));
        actor.FrameIndex = 0;
        actor.Motion = (int)CombatMotionKind.Attack;
        actor.ActionFlags = attack.ResultFlags;
        actor.FrameInterval = CombatActorMaintenance.ModifyActionInterval(actor, attack.FrameInterval);
        actor.AnimationDeadline = 0;
        actor.ActionSpriteHeight = CurrentSprite.Height;
        int chain = attack.ChainWindow;
        actor.ChainMode = unchecked((byte)chain);
        actor.ChainDeadline = chain == 0 ? 0 : unchecked((int)milliseconds + chain);
    }

    private void BindMotion(int mode)
    {
        if ((uint)mode >= (uint)info.Motions.Count)
        {
            throw new InvalidDataException("Combat motion is outside actual INF records.");
        }

        int selected = info.Motions[mode].Animation;
        ValidateSequence(selected);
        sequence = selected;
        boundFrame = 0;
        displayedSprite = null;
        frameOverride = null;
        actor.FrameIndex = 0;
    }

    //404ee0 resolves pointers AFTER AI. A later061b0 D5 increment alone does not rebind the current frame.
    public void RebindFromFrameCounter()
    {
        int jump = actor.JumpStage;
        if (jump is 16 or 17 && actor.Motion == (int)CombatMotionKind.Idle)
        {
            BindAuxiliaryFrame();
            return;
        }

        int index = FrameIndex;
        if ((uint)sequence >= (uint)assets.Animation.Sequences.Length || (uint)index >= (uint)assets.Animation.Sequences[sequence].Frames.Length)
        {
            throw new InvalidDataException("Combat frame counter has no actual sequence frame.");
        }

        int sprite = assets.Animation.Sequences[sequence].Frames[index].SpriteFrameIndex;
        if ((uint)sprite >= (uint)assets.Sprites.Frames.Length)
        {
            throw new InvalidDataException("Combat frame counter refers to an absent sprite.");
        }

        boundFrame = index;
        displayedSprite = null;
        frameOverride = null;
    }

    public int AuxiliaryFrameCount => assets.Animation.Sequences[auxiliarySequence].Frames.Length;

    public void BindAuxiliaryFrame()
    {
        int index = actor.JumpFrameIndex;
        if ((uint)auxiliarySequence >= (uint)assets.Animation.Sequences.Length
|| (uint)index >= (uint)assets.Animation.Sequences[auxiliarySequence].Frames.Length)
        {
            throw new InvalidDataException("Combat auxiliary jump frame exceeds actual sequence.");
        }

        int sprite = assets.Animation.Sequences[auxiliarySequence].Frames[index].SpriteFrameIndex;
        if ((uint)sprite >= (uint)assets.Sprites.Frames.Length)
        {
            throw new InvalidDataException("Combat jump frame refers to absent sprite.");
        }

        displayedSprite = assets.Sprites.Frames[sprite];
    }

    public void BindJumpSequence(int jumpIndex, bool main)
    {
        if ((uint)jumpIndex >= (uint)info.Jumps.Count)
        {
            throw new InvalidDataException("Combat jump exceeds actual INF records.");
        }

        int selected = info.Jumps[jumpIndex].Animation;
        ValidateSequence(selected);
        auxiliarySequence = selected;
        actor.JumpFrameIndex = 0;
        if (main)
        {
            sequence = selected;
            boundFrame = 0;
            displayedSprite = null;
            frameOverride = null;
            actor.FrameIndex = 0;
        }
        else
        {
            frameOverride = assets.Animation.Sequences[selected].Frames[0];
            BindAuxiliaryFrame();
        }
    }

    private void ValidateSequence(int selected)
    {
        if ((uint)selected >= (uint)assets.Animation.Sequences.Length || assets.Animation.Sequences[selected].Frames.Length == 0
|| (uint)assets.Animation.Sequences[selected].Frames[0].SpriteFrameIndex >= (uint)assets.Sprites.Frames.Length)
        {
            throw new InvalidDataException("Combat action has an absent actual frame.");
        }
    }

}
