using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Managed ANI bindings for408b40. No native pointers are stored in actor images.</summary>
public sealed class CombatAnimationCursor(byte[] actor, CombatInfoCharacter info, CombatAnimationAssets assets)
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
            ValidateActor();
            return BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(0xd5, 2));
        }
    }
    public AnimationFrame CurrentFrame
    {
        get
        {
            if (frameOverride is not null) return frameOverride;
            if ((uint)sequence >= (uint)assets.Animation.Sequences.Length) throw new InvalidOperationException("Actor has no actual bound animation sequence.");
            AnimationFrame[] frames = assets.Animation.Sequences[sequence].Frames;
            int index = boundFrame;
            if ((uint)index >= (uint)frames.Length) throw new InvalidDataException("Combat animation cursor exceeds its actual sequence.");
            return frames[index];
        }
    }
    public SpriteFrame CurrentSprite
    {
        get
        {
            if (displayedSprite is not null) return displayedSprite;
            int index = CurrentFrame.SpriteFrameIndex;
            if ((uint)index >= (uint)assets.Sprites.Frames.Length) throw new InvalidDataException("Combat ANI refers to an absent sprite frame.");
            return assets.Sprites.Frames[index];
        }
    }

    //409070 compares sequence pointers, then changes deadlines/frame binding only on a new sequence.
    public bool IsMotionSequence(int mode) => sequence == info.Read(mode * 0x18);
    public int SequenceFrameCount => assets.Animation.Sequences[sequence].Frames.Length;
    public void BindManualMotion(int mode, uint milliseconds)
    {
        BindMotion(mode);
        WS(0xd5, 0);
        W(0xc5, info.Read(mode * 0x18 + 0x10));
        W(0xc9, unchecked(info.Read(mode * 0x18 + 0x10) + (int)milliseconds));
        int interval = CombatActorMaintenance.ModifyActionInterval(actor, info.Read(mode * 0x18 + 0xc));
        W(0xc1, interval); W(0xbd, unchecked(interval + (int)milliseconds));
    }
    //4073a0: direct sequence command uses this actor's actual animation group.
    public void BeginSequenceCommand(int selected, int interval, uint milliseconds)
    {
        ValidateActor(); ValidateSequence(selected); sequence = selected; boundFrame = 0; displayedSprite = null; frameOverride = null;
        W(0xb1, 6); WS(0xd5, 0); W(0xad, 0x140); W(0x84, CurrentSprite.Height); actor[0xa8] = 0;
        int adjusted = CombatActorMaintenance.ModifyActionInterval(actor, interval);
        W(0xc1, adjusted); W(0xbd, unchecked(adjusted + (int)milliseconds));
    }
    //405260 binds reaction frame0 without changing the live D5 counter or deadline.
    public void BindReactionSequence(bool rebuildAnchor = true, bool bindLiveCounter = false)
    {
        ValidateActor(); int reaction = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(0xf6, 4));
        if ((uint)reaction >= (uint)info.Read(0xb24)) throw new InvalidDataException("Combat reaction exceeds actual INF records.");
        int selected = info.Read(0x678 + reaction * 0x10); ValidateSequence(selected);
        sequence = selected; boundFrame = 0; displayedSprite = null; frameOverride = null;
        if(bindLiveCounter)
        {
            int frame=FrameIndex;
            if((uint)frame>=(uint)assets.Animation.Sequences[selected].Frames.Length) throw new InvalidDataException("Reaction frame exceeds actual sequence.");
            boundFrame=frame;
        }
        if (!rebuildAnchor) return;
        int x = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(0x7c, 4));
        if (unchecked((sbyte)actor[0xfa]) == -1) x = unchecked(x + CurrentSprite.Width);
        W(0x70, x);
    }
    public void BindDisplayedSpriteIndex(int index)
    {
        if((uint)index>=(uint)assets.Sprites.Frames.Length) throw new InvalidDataException("Combat displayed sprite index exceeds actual target sprite bank.");
        displayedSprite=assets.Sprites.Frames[index];
    }
    //4063e0 changes2c5 while leaving the bound2d5/2d1 pointers intact until projection.
    public void SelectGuardSequence()
    {
        int at=0x150+info.Read(0xb1c)*0x2c;
        AnimationFrame oldFrame=CurrentFrame; SpriteFrame oldSprite=CurrentSprite;
        int selected=info.Read(at); ValidateSequence(selected); sequence=selected;
        frameOverride=oldFrame; displayedSprite=oldSprite; WS(0xdb,0); WS(0xd5,0);
    }
    public void BeginVariation(int index, uint milliseconds)
    {
        if(index<info.Read(0xb1c) || index>=info.Read(0xb20)) throw new InvalidDataException("Combat variation exceeds actual INF V records.");
        int at=0x150+index*0x2c,selected=info.Read(at); ValidateSequence(selected);
        sequence=selected; boundFrame=0; frameOverride=null; displayedSprite=null;
        W(0xb1,6); WS(0xdb,0); WS(0xd5,0); W(0xad,info.Read(at+8)); W(0x84,CurrentSprite.Height);
        int interval=CombatActorMaintenance.ModifyActionInterval(actor,info.Read(at+24));
        W(0xc1,interval); W(0xbd,unchecked(interval+(int)milliseconds));
    }

    public void BeginMotion(int mode, uint milliseconds)
    {
        ValidateActor();
        if ((uint)mode >= (uint)info.Read(0xb18)) throw new InvalidDataException("Combat motion is outside actual INF motion records.");
        int selected = info.Read(mode * 0x18);
        if ((uint)selected >= (uint)assets.Animation.Sequences.Length || assets.Animation.Sequences[selected].Frames.Length == 0)
            throw new InvalidDataException("Combat motion refers to an absent ANI sequence.");
        int sprite = assets.Animation.Sequences[selected].Frames[0].SpriteFrameIndex;
        if ((uint)sprite >= (uint)assets.Sprites.Frames.Length) throw new InvalidDataException("Combat motion refers to an absent sprite frame.");
        int interval = CombatActorMaintenance.ModifyActionInterval(actor, info.Read(mode * 0x18 + 0xc));
        sequence = selected; boundFrame=0; displayedSprite=null; frameOverride=null;
        W(0xb1, mode); BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(0xd5, 2), 0);
        W(0xad, 0x40); W(0x84, assets.Sprites.Frames[sprite].Height);
        W(0xc1, interval); W(0xbd, unchecked(interval + (int)milliseconds));
        // Native2c5/2d5/2d1 sequence/frame/sprite pointers are represented by this managed binding.
    }
    //411860 intentionally does not update84 or the existing animation deadlineBD.
    public void BeginAutomaticIdle(int mode)
    {
        BindMotion(mode);
        W(0xb1, mode); W(0xad, 0x40);
        W(0xc1, CombatActorMaintenance.ModifyActionInterval(actor, info.Read(mode * 0x18 + 0xc)));
    }
    //40d7b0 called after automatic movement chooses its actual INF motion record.
    public void BeginAutomaticMovement(int mode, int dx, int dy, uint milliseconds)
    {
        int xStep=info.Read(mode*0x18+4), yStep=info.Read(mode*0x18+8);
        if(xStep==0 || yStep==0) throw new InvalidDataException("Combat INF movement divisor is zero.");
        int nx=dx/xStep, ny=dy/yStep;
        BindMotion(mode);
        W(0xb1,mode); W(0xad,1);
        W(0x9c,dx<0?unchecked(-xStep):xStep); W(0xa0,dy<0?unchecked(-yStep):yStep);
        WS(0xde,unchecked((short)((nx^(nx>>31))-(nx>>31)))); WS(0xe0,unchecked((short)((ny^(ny>>31))-(ny>>31))));
        int movementInterval=info.Read(mode*0x18+0x10);
        W(0xc5,movementInterval); W(0xc9,unchecked(movementInterval+(int)milliseconds));
        int animationInterval=CombatActorMaintenance.ModifyActionInterval(actor,info.Read(mode*0x18+0xc));
        W(0xc1,animationInterval); W(0xbd,unchecked(animationInterval+(int)milliseconds));
    }
    public void BeginAttack(int index,uint milliseconds)
    {
        ValidateActor();
        int count=Math.Max(info.Read(0xb10),info.Read(0xb20));
        if((uint)index>=(uint)count) throw new InvalidDataException("Combat attack exceeds actual INF records.");
        int at=0x150+index*0x2c, selected=info.Read(at);
        ValidateSequence(selected);
        sequence=selected; boundFrame=0; displayedSprite=null; frameOverride=null; WS(0xdb,unchecked((short)index)); WS(0xd5,0);
        W(0xb1,6); W(0xad,info.Read(at+8));
        W(0xc1,CombatActorMaintenance.ModifyActionInterval(actor,info.Read(at+24))); W(0xbd,0);
        W(0x84,CurrentSprite.Height); int chain=info.Read(at+12); actor[0xa8]=unchecked((byte)chain);
        W(0xcd,chain==0?0:unchecked((int)milliseconds+chain));
    }
    private void BindMotion(int mode)
    {
        ValidateActor();
        if((uint)mode>=(uint)info.Read(0xb18)) throw new InvalidDataException("Combat motion is outside actual INF records.");
        int selected=info.Read(mode*0x18);
        ValidateSequence(selected);
        sequence=selected; boundFrame=0; displayedSprite=null; frameOverride=null; WS(0xd5,0);
    }
    //404ee0 resolves pointers AFTER AI. A later061b0 D5 increment alone does not rebind the current frame.
    public void RebindFromFrameCounter()
    {
        int jump=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(0xb9,4));
        if(jump is 16 or 17 && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(0xb1,4))==0)
        {
            BindAuxiliaryFrame(); return;
        }
        int index=FrameIndex;
        if((uint)sequence>=(uint)assets.Animation.Sequences.Length || (uint)index>=(uint)assets.Animation.Sequences[sequence].Frames.Length)
            throw new InvalidDataException("Combat frame counter has no actual sequence frame.");
        int sprite=assets.Animation.Sequences[sequence].Frames[index].SpriteFrameIndex;
        if((uint)sprite>=(uint)assets.Sprites.Frames.Length) throw new InvalidDataException("Combat frame counter refers to an absent sprite.");
        boundFrame=index; displayedSprite=null; frameOverride=null;
    }

    public int AuxiliaryFrameCount => assets.Animation.Sequences[auxiliarySequence].Frames.Length;
    public void BindAuxiliaryFrame()
    {
        int index=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(0xd9,2));
        if((uint)auxiliarySequence>=(uint)assets.Animation.Sequences.Length || (uint)index>=(uint)assets.Animation.Sequences[auxiliarySequence].Frames.Length)
            throw new InvalidDataException("Combat auxiliary jump frame exceeds actual sequence.");
        int sprite=assets.Animation.Sequences[auxiliarySequence].Frames[index].SpriteFrameIndex;
        if((uint)sprite>=(uint)assets.Sprites.Frames.Length) throw new InvalidDataException("Combat jump frame refers to absent sprite.");
        displayedSprite=assets.Sprites.Frames[sprite];
    }
    public void BindJumpSequence(int jumpIndex, bool main)
    {
        if((uint)jumpIndex>=(uint)info.Read(0xb14)) throw new InvalidDataException("Combat jump exceeds actual INF records.");
        int selected=info.Read((jumpIndex+7)*0x18); ValidateSequence(selected);
        auxiliarySequence=selected; WS(0xd9,0);
        if(main) { sequence=selected; boundFrame=0; displayedSprite=null; frameOverride=null; WS(0xd5,0); }
        else { frameOverride=assets.Animation.Sequences[selected].Frames[0]; BindAuxiliaryFrame(); }
    }
    private void ValidateSequence(int selected)
    {
        if((uint)selected>=(uint)assets.Animation.Sequences.Length || assets.Animation.Sequences[selected].Frames.Length==0 ||
            (uint)assets.Animation.Sequences[selected].Frames[0].SpriteFrameIndex>=(uint)assets.Sprites.Frames.Length)
            throw new InvalidDataException("Combat action has an absent actual frame.");
    }
    private void ValidateActor()
    { if (actor.Length != CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Incomplete combat animation actor record."); }
    private void W(int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
    private void WS(int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset,2),value);
}
