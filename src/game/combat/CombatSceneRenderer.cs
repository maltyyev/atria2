using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>40a820 actor/particle depth queue and actual world/menu raster; HUD is a separate stage.</summary>
public sealed class CombatSceneRenderer(CombatSceneRuntime runtime,CombatBattleAssets assets,CombatCameraBounds camera,CombatCameraShake shake)
{
    private readonly DrawItem[] queue=new DrawItem[80];
    private readonly CombatBackgroundLayer primaryBackground=new(assets.BackgroundWidth,assets.BackgroundHeight,assets.Background);
    private int count,detail,shakeX;
    private readonly record struct DrawItem(int X,int Y,int Depth,int Code,SpriteFrame? Sprite,int Divisor,ushort Color);
    public void PrepareActors(int visualDetail,bool advanceActors=true)
    {
        count=0;detail=visualDetail;shakeX=shake.AdvanceVisual();
        for(int actorIndex=0;actorIndex<runtime.Actors.Count;actorIndex++)
        {
            CombatActorImage image=runtime.Actors[actorIndex];byte[] a=image.Fields;if(I(a,0x60)!=0 || I(a,0x224)==0) continue;
            if(image.Animation is null || image.Binding is null) throw new InvalidOperationException("Visible combat actor lacks actual bindings.");
            int code=unchecked((sbyte)a[0xfa]);if(I(a,0xb1)==4) code=-code;
            int divisor=0;ushort color=unchecked((ushort)I(a,0x294));
            if(advanceActors)
            {
                if(I(a,0x23c)>0)
                {
                    if((a[0x23c]&15)==15) W(a,0x290,I(a,0x290)==0?1:0);
                    if(I(a,0x290)!=0 && color!=0) code=unchecked((sbyte)a[0xfa])*6;
                    W(a,0x23c,I(a,0x23c)-1);if(I(a,0x23c)==0) W(a,0x240,0);
                }
                if(I(a,0x1ec)==0)
                {
                    if(I(a,0x28c)!=0) W(a,0x28c,I(a,0x28c)-1);
                    divisor=Math.Max(1,5-(I(a,0x28c)>>3));code=unchecked((sbyte)a[0xfa])*7;
                }
                if(I(a,0x2c0)!=0) code=unchecked((sbyte)a[0xfa])*5;
                if(image.Binding.Info.Type>45) { code*=6;color=0x4208; }
            }
            if(image.Binding.Info.Type is 13 or 25 or 26)
            { code=unchecked((sbyte)a[0xfa]);if(I(a,0xb1)==4) code=-code;code*=5; }
            if(unchecked((sbyte)a[0xa6])>99 && actorIndex<runtime.Scene.PartyStartIndex) code*=12;
            if(I(a,0x240)>10) code*=5;
            Add(new(I(a,0x7c),I(a,0x80),I(a,0x78),code,image.Animation.CurrentSprite,divisor,color));
        }
    }
    public void AppendParticles(ReadOnlySpan<CombatParticleDraw> particles)
    {
        foreach(var p in particles) Add(new(p.X,p.Y,p.Depth,p.Code,p.Sprite,0,0));
        for(int i=0;i<count-1;i++) for(int j=i+1;j<count;j++) if(queue[j].Depth<queue[i].Depth) (queue[i],queue[j])=(queue[j],queue[i]);
    }
    public void DrawWorldAndMenu(ushort[] target)
    {
        if(target.Length!=640*480) throw new ArgumentException("Combat target must be640x480.");
        primaryBackground.Draw(target,camera.Primary,shakeX);
        if(assets.Secondary is not null)
            assets.Secondary.Draw(target,camera.Secondary??throw new InvalidOperationException("Second battle bitmap requires its native scroll layer."));
        assets.Menu.CopyTo(target,400*640);
        RasterClip clip=new(0,0,639,400);
        for(int i=0;i<count;i++)
        {
            DrawItem item=queue[i];if(item.Y<0) continue;
            if(item.Code is 999 or -999) { CombatSpecialRaster.DrawCrosshair(target,640,clip);continue; }
            SpriteFrame sprite=item.Sprite??throw new NotSupportedException("Combat special null-sprite draw command requires its dedicated raster.");
            int code=Math.Abs(item.Code);bool mirrored=item.Code<0;
            switch(code)
            {
                case 1:
                    if(detail<2) Shadow(item,sprite,target,mirrored,clip);
                    SpriteRaster.Draw565(sprite,target,640,480,640,item.X,item.Y,detail==0?SpriteBlend.HalfType3:SpriteBlend.Copy,mirrored,clip);break;
                case 6:
                    if(detail!=2) Shadow(item,sprite,target,mirrored,clip);
                    CombatSpriteEffects.DrawTint(sprite,target,640,item.X,item.Y,item.Color,mirrored,clip);break;
                case 5:CombatSpecialRaster.DrawHalfAll(sprite,target,640,item.X,item.Y,mirrored,clip);break;
                case 7:CombatSpriteEffects.DrawFade(sprite,target,640,item.X,item.Y,item.Divisor,mirrored,clip);break;
                case 8:
                case 10:
                    SpriteRaster.Draw565(sprite,target,640,480,640,item.X,item.Y,SpriteBlend.Copy,mirrored,clip);
                    if(code==8 && item.Depth>=0 && item.Depth<400)
                    {
                        int left=Math.Clamp(item.X,0,639),right=Math.Clamp(unchecked(item.X+sprite.Width),0,639);
                        if(right>left) target.AsSpan(item.Depth*640+left,right-left).Clear();
                    }
                    break;
                case 11:CombatSpriteEffects.DrawAlpha(sprite,target,640,item.X,item.Y,mirrored,clip);break;
                case 12:CombatSpriteEffects.DrawPacketColor(sprite,target,640,item.X,item.Y,0x7bef,mirrored,clip);break;
                default:
                    if(detail<2) Shadow(item,sprite,target,mirrored,clip);
                    SpriteRaster.Draw565(sprite,target,640,480,640,item.X,item.Y,detail==0?SpriteBlend.HalfType3:SpriteBlend.Copy,mirrored,clip);break;
            }
        }
    }
    private void Add(DrawItem item) { queue[count]=item;count++;if(count>79) count=0; }
    private static void Shadow(DrawItem item,SpriteFrame sprite,ushort[] target,bool mirrored,RasterClip clip)
        =>CombatShadowRaster.Draw(sprite,target,640,480,640,item.X,item.Depth-sprite.Height-10,mirrored,clip);
    private static int I(byte[] a,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(offset,4));
    private static void W(byte[] a,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(offset,4),value);
}
