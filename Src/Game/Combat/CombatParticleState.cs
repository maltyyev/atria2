using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Live projectile/effect state. Animation resources supply its movement and lifetime parameters.</summary>
public sealed class CombatParticleState
{
    public int X
    {
        get; set;
    }
    public int Y
    {
        get; set;
    }
    public int Depth
    {
        get; set;
    }
    public int SpriteX
    {
        get; set;
    }
    public int SpriteY
    {
        get; set;
    }
    public int FrameIndex
    {
        get; set;
    }
    public int Facing
    {
        get; set;
    }
    public int NextFrameTime
    {
        get; set;
    }
    public int FrameInterval
    {
        get; set;
    }
    public int HitDelay
    {
        get; set;
    }
    public int VelocityX
    {
        get; set;
    }
    public int VelocityY
    {
        get; set;
    }
    public int AccelerationY
    {
        get; set;
    }
    public int DepthTolerance
    {
        get; set;
    }
    public int ExpiryRule
    {
        get; set;
    }
    public int Kind
    {
        get; set;
    }
    public int CompletedCycles
    {
        get; set;
    }
    public CombatAnimationAssets? Assets
    {
        get; internal set;
    }
    public CombatAnimationAssets? ContainerAssets
    {
        get; internal set;
    } //Native+54 ANI owner can change before bound frame/sprite pointers.
    public int SequenceIndex
    {
        get; internal set;
    }
    public CombatActorImage? Owner
    {
        get; internal set;
    }
    public AnimationFrame CurrentFrame => Sequence.Frames[FrameIndex];
    public AnimationFrame FirstFrame => Sequence.Frames[0];
    public AnimationSequence Sequence => (Assets ?? throw new InvalidOperationException("Particle animation not bound.")).Animation.Sequences[SequenceIndex];

    public SpriteFrame CurrentSprite
    {
        get
        {
            var sprites = Assets!.Sprites.Frames;
            int index = CurrentFrame.SpriteFrameIndex;
            if ((uint)index >= (uint)sprites.Length)
            {
                throw new InvalidDataException("Particle ANI references an absent sprite.");
            }

            return sprites[index];
        }
    }

    public void Clear()
    {
        X = 0;
        Y = 0;
        Depth = 0;
        SpriteX = 0;
        SpriteY = 0;
        FrameIndex = 0;
        Facing = 0;
        NextFrameTime = 0;
        FrameInterval = 0;
        HitDelay = 0;
        VelocityX = 0;
        VelocityY = 0;
        AccelerationY = 0;
        DepthTolerance = 0;
        ExpiryRule = 0;
        Kind = 0;
        CompletedCycles = 0;
        Assets = ContainerAssets = null;
        Owner = null;
        SequenceIndex = 0;
    }

    public void AdvanceFrame(uint milliseconds, Action<int> playSound)
    {
        int count = Sequence.Frames.Length;
        if (count < 2)
        {
            throw new InvalidDataException("Particle sequence requires native frame1.");
        }

        int frame = unchecked(FrameIndex + 1);
        if (count - 1 <= frame)
        {
            frame = 1;
        }

        FrameIndex = frame;
        if (count - 2 <= frame)
        {
            CompletedCycles = unchecked(CompletedCycles + 1);
        }

        playSound(CurrentFrame.Sound);
        var sprite = CurrentSprite;
        SpriteX = Facing == 1 ? unchecked(CurrentFrame.OffsetX + X) : unchecked(X - CurrentFrame.OffsetX - sprite.Width);
        NextFrameTime = unchecked(FrameInterval + (int)milliseconds);
        SpriteY = unchecked(CurrentFrame.OffsetY + Y);
    }
}

public readonly record struct CombatParticleDraw(int Slot, int X, int Y, int Depth, int Code, SpriteFrame? Sprite, int NativeKind = 1);
public sealed record CombatParticlePass(bool HadActiveParticles, CombatParticleDraw[] Draws);
