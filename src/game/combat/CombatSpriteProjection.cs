using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>404ee0 coordinate stage after actual input/AI and frame selection; no guessed animation progression.</summary>
public static class CombatSpriteProjection
{
    public static void Update(Span<byte> actor,int type,AnimationFrame frame,SpriteFrame sprite)
    {
        Validate(actor);
        if(frame.Words.Length!=28) throw new ArgumentException("Incomplete actual ANI frame.");
        unchecked
        {
            int mode=I(actor,0xb1), jump=I(actor,0xb9), y=I(actor,0x78);
            if(jump is 16 or 17)
            {
                if(y<I(actor,0x80)+sprite.Height) { W(actor,0x74,y-sprite.Height); W(actor,0x80,y-sprite.Height); }
                return;
            }
            int projectedY;
            switch(mode)
            {
                case 0: case 1: case 2: case 3:
                    if(S(actor,0xd5)==0)
                    { projectedY=frame.OffsetY-sprite.Height+y; W(actor,0x90,projectedY); W(actor,0x94,y); }
                    else projectedY=frame.OffsetY-I(actor,0x94)+I(actor,0x90)+y;
                    break;
                case 11: case 12: case 14: projectedY=y-sprite.Metadata1; break;
                default:
                    projectedY=type==19 || (I(actor,0xad)&0x100)!=0 ? frame.OffsetY-I(actor,0x84)+y : y-sprite.Height;
                    break;
            }
            W(actor,0x80,projectedY);
            int x=I(actor,0x70), projectedX;
            if(I(actor,0xad)==0x940)
            {
                projectedX=I(actor,0xe6)<0?frame.OffsetX+x:x-frame.OffsetX;
                if(unchecked((sbyte)actor[0xfa])==-1) projectedX-=sprite.Width;
            }
            else projectedX=actor[0xfa]==1?frame.OffsetX+x:x-sprite.Width-frame.OffsetX;
            W(actor,0x7c,projectedX);
            if((type==19 || type==45) && (mode<8 || mode>13)) W(actor,0x80,projectedY-(type==19?80:110));
        }
    }
    //407e10 performs this after bounds/animation handling, rather than inside404ee0.
    public static void UpdateTrackingX(Span<byte> actor,SpriteFrame sprite)
    { Validate(actor); W(actor,0x98,unchecked(sprite.Metadata2+I(actor,0x7c))); }
    private static void Validate(ReadOnlySpan<byte> actor)
    { if(actor.Length!=CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Incomplete native sprite projection actor."); }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static short S(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
}
