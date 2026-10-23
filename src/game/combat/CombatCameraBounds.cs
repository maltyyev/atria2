using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>401800/401890 scroll counters for a real loaded bitmap layer.</summary>
public sealed class CombatScrollLayer(int width,int height,int horizontalDivisor=1,int verticalDivisor=1)
{
    public int Width { get; }=width;
    public int Height { get; }=height;
    public int X { get; private set; }
    public int Y { get; private set; }
    private int horizontalCounter,verticalCounter;
    public bool MoveX(int delta)=>Move(delta,Width,639,horizontalDivisor,ref horizontalCounter,true);
    public bool MoveY(int delta)=>Move(delta,Height,400,verticalDivisor,ref verticalCounter,false);
    private bool Move(int delta,int extent,int view,int divisor,ref int counter,bool horizontal)
    {
        int position=horizontal?X:Y;
        if(delta<0)
        {
            if(position<1) return false;
            counter=unchecked(counter-1);
            if(counter<1) { position=Math.Max(0,unchecked(position+delta));counter=divisor; }
        }
        else
        {
            if(extent<=unchecked(view+position)) return false;
            counter=unchecked(counter+1);
            if(divisor<=counter) { position=unchecked(position+delta);counter=0;if(extent<unchecked(position+view)) position=extent-view; }
        }
        if(horizontal) X=position;else Y=position;
        return true;
    }
}

/// <summary>4078b0 camera scrolling/actor bounds plus4077f0 world and particle translation.</summary>
public sealed class CombatCameraBounds(CombatSceneRuntime runtime, CombatScrollLayer primary,
    Action<int,int> translateParticles, CombatScrollLayer? secondary=null)
{
    public CombatScrollLayer Primary { get; }=primary;
    public CombatScrollLayer? Secondary { get; }=secondary;
    public void Update(int index)
    {
        CombatActorImage image=runtime.Actors[index];byte[] actor=image.Fields;
        if(image.Animation is null) throw new InvalidOperationException("Camera actor has no actual bound sprite.");
        if(actor[0xa4]==1)
        {
            if(I(actor,0x7c)<150)
            {
                if(Primary.MoveX(-2)) { Translate(2,0);Secondary?.MoveX(-2); }
            }
            else if(I(actor,0x7c)>400 && Primary.MoveX(2)) { Translate(-2,0);Secondary?.MoveX(2); }
            if(I(actor,0x78)<371)
            {
                if(I(actor,0x78)<=269 && Primary.MoveY(-2)) { Translate(0,2);Secondary?.MoveY(-2); }
            }
            else if(Primary.MoveY(2)) { Translate(0,-2);Secondary?.MoveY(2); }
        }
        var sprite=image.Animation.CurrentSprite;int metadata=sprite.Metadata2;
        int left=actor[0xa4]==1?0:-80;
        int right=unchecked((actor[0xa4]==1?Primary.Width:Primary.Width+80)-Primary.X);
        bool forward=actor[0xfa]==1;
        int center=unchecked(I(actor,0x7c)+(forward?metadata:sprite.Width-metadata));
        int offset=forward?-metadata:metadata-sprite.Width;
        if(center<left)
        {
            W(actor,0x7c,unchecked(left+offset));W(actor,0x70,unchecked(left+offset));
            if(I(actor,0xad)==1 && unchecked((sbyte)actor[0xfa])==-1) WS(actor,0xde,0);
        }
        else if(center>right)
        {
            W(actor,0x7c,unchecked(right+offset));W(actor,0x70,unchecked(right+offset));
            if(I(actor,0xad)==1 && actor[0xfa]==1) WS(actor,0xde,0);
            if(unchecked((sbyte)actor[0xfa])==-1) W(actor,0x70,unchecked(I(actor,0x70)+sprite.Width));
        }
        int worldY=unchecked(Primary.Y+I(actor,0x78));
        int bound=worldY>runtime.Scene.Header77?runtime.Scene.Header77:worldY<runtime.Scene.Header73?runtime.Scene.Header73:worldY;
        if(bound!=worldY)
        {
            int y=unchecked(bound-Primary.Y);
            W(actor,0x80,unchecked(I(actor,0x80)+y-I(actor,0x78)));W(actor,0x78,y);WS(actor,0xde,0);WS(actor,0xe0,0);
        }
    }
    private void Translate(int dx,int dy)
    {
        foreach(CombatActorImage image in runtime.Actors)
        {
            byte[] a=image.Fields;if(I(a,0x224)==0) continue;
            W(a,0x70,unchecked(I(a,0x70)+dx));W(a,0x7c,unchecked(I(a,0x7c)+dx));
            W(a,0x74,unchecked(I(a,0x74)+dy));W(a,0x78,unchecked(I(a,0x78)+dy));W(a,0x80,unchecked(I(a,0x80)+dy));
        }
        translateParticles(dx,dy);
    }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
    private static void WS(byte[] actor,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset,2),value);
}
