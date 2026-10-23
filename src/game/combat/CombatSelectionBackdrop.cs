using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>Frozen40a820(0) encounter frame before party leaders and HUD bars exist.</summary>
public sealed class CombatSelectionBackdrop
{
    private readonly ushort[] pixels=new ushort[640*480];
    public ReadOnlyMemory<ushort> Pixels=>pixels;
    public CombatSelectionBackdrop(string resourceRoot,CombatSceneRuntime runtime)
        :this(runtime,CombatBattleAssets.Load(resourceRoot,runtime.Scene)) { }
    public CombatSelectionBackdrop(CombatSceneRuntime runtime,CombatBattleAssets assets,int visualDetail=0)
    {
        foreach(CombatActorImage actor in runtime.Actors)
        {
            if(I(actor.Fields,0x224)==0 || I(actor.Fields,0x60)!=0) continue;
            if(actor.Animation is null || I(actor.Fields,0xad)!=0x40 || I(actor.Fields,0xb1)!=0 || I(actor.Fields,0x230)!=0)
                throw new InvalidOperationException("Preselection backdrop requires actual bound idle actors before leader binding.");
            CombatSpriteProjection.Update(actor.Fields,actor.Binding!.Info.Type,actor.Animation.CurrentFrame,actor.Animation.CurrentSprite);
        }
        //413930 has reset particles and shake. No actor controller, animation, status or camera update runs here.
        var camera=assets.CreateCamera(runtime,(_,_)=>throw new InvalidOperationException("Frozen battle backdrop cannot scroll particles."));
        var renderer=new CombatSceneRenderer(runtime,assets,camera,new CombatCameraShake());
        renderer.PrepareActors(visualDetail,false);renderer.AppendParticles([]);renderer.DrawWorldAndMenu(pixels);
    }
    public void Draw(ushort[] target)
    {
        if(target.Length!=pixels.Length) throw new ArgumentException("Battle target must be640x480.");
        pixels.CopyTo(target,0);
    }
    private static int I(byte[] a,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(offset,4));
}
