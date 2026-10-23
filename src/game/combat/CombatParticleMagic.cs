using System.Buffers.Binary;
using System.Globalization;

namespace Atria.Game.Combat;

public static class CombatParticleMagic
{
    //40d550 binds actual MAGIC%02d.ANI for id-100. Caller owns active-magic lifecycle.
    public static CombatAnimationAssets Load(string root,int id)=>CombatAnimationAssets.Load(root,
        System.Text.Encoding.ASCII.GetBytes("MAGIC"+unchecked(id-100).ToString("D2",CultureInfo.InvariantCulture)));

    //40b730 mass resurrection. Does not invent an animation/input action for the revived actor.
    public static int Resurrect(IReadOnlyList<CombatActorImage> actors,CombatActorImage current,int mode)
    {
        int count=0;byte team=current.Fields[0xa6];int strength=I(current.Fields,0x17c);
        foreach(var actor in actors)
        {
            byte[] fields=actor.Fields;
            if(I(fields,0x170)>=1||actor.Binding is not {} binding||fields[0xa6]!=team)continue;
            int type=binding.Info.Read(0xafc);
            if(type is not (12 or 21 or 22 or 23 or 37)&&mode==1)continue;
            W(fields,0x224,1);count++;W(fields,0x23c,250);W(fields,0x170,Math.Min(I(fields,0x16c),unchecked(strength*50)));
            W(fields,0x294,0);W(fields,0x1ec,3);if(count>4)return count;
        }
        return count;
    }
    private static int I(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    private static void W(byte[] bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset,4),value);
}
