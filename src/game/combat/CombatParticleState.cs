using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Pointer-free projection of native108byte particle. Field indices retain native integer layout.</summary>
public sealed class CombatParticleState
{
    public int[] Fields {get;}=new int[27];
    public CombatAnimationAssets? Assets {get;internal set;}
    public CombatAnimationAssets? ContainerAssets {get;internal set;} //Native+54 ANI owner can change before bound frame/sprite pointers.
    public int SequenceIndex {get;internal set;}
    public CombatActorImage? Owner {get;internal set;}
    public AnimationFrame CurrentFrame=>Sequence.Frames[Fields[5]];
    public AnimationFrame FirstFrame=>Sequence.Frames[0];
    public AnimationSequence Sequence=>(Assets??throw new InvalidOperationException("Particle animation not bound.")).Animation.Sequences[SequenceIndex];
    public SpriteFrame CurrentSprite
    {
        get
        {
            var sprites=Assets!.Sprites.Frames;int index=CurrentFrame.SpriteFrameIndex;
            if((uint)index>=(uint)sprites.Length)throw new InvalidDataException("Particle ANI references an absent sprite.");return sprites[index];
        }
    }
    public void Clear(){Array.Clear(Fields);Assets=ContainerAssets=null;Owner=null;SequenceIndex=0;}
    public void AdvanceFrame(uint milliseconds,Action<int> playSound)
    {
        int[] data=Fields;int count=Sequence.Frames.Length;
        if(count<2)throw new InvalidDataException("Particle sequence requires native frame1.");
        int frame=unchecked(data[5]+1);if(count-1<=frame)frame=1;data[5]=frame;
        if(count-2<=frame)data[18]=unchecked(data[18]+1);
        playSound(CurrentFrame.Words[7]);var sprite=CurrentSprite;
        data[3]=data[6]==1?unchecked(CurrentFrame.OffsetX+data[0]):unchecked(data[0]-CurrentFrame.OffsetX-sprite.Width);
        data[7]=unchecked(data[8]+(int)milliseconds);data[4]=unchecked(CurrentFrame.OffsetY+data[1]);
    }
}
public readonly record struct CombatParticleDraw(int Slot,int X,int Y,int Depth,int Code,SpriteFrame? Sprite,int NativeKind=1);
public sealed record CombatParticlePass(bool HadActiveParticles,CombatParticleDraw[] Draws);
