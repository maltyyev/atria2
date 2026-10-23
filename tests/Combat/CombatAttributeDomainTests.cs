using System.Buffers.Binary;
using Atria.Game.Combat;

namespace Atria.Tests.Combat;

public static class CombatAttributeDomainTests
{
    public static string Run(string root)
    {
        (int resource,int type,int hp,int attack,int interval)[] fixtures=[(47,45,300,36,250),(48,46,180,20,500),(49,47,300,24,400),(50,48,300,24,400),(51,49,300,24,400)];
        foreach(var f in fixtures)
        {
            CombatInfoCharacter info=CombatInfoDefinition.Load(root,System.Text.Encoding.ASCII.GetBytes(f.resource+".inf")).Characters[0];
            Check(info.Type==f.type,"Actual numeric INF uses its declared T type, not the SOD numeric identifier as table index.");
            byte[] actor=new byte[CombatSceneDefinition.RuntimeRecordSize],shared=info.Fields.ToArray();
            W(actor,0x355,99);W(actor,0x35d,123);int award=0,samples=0;
            CombatEnemyAttributes.Initialize(actor,shared,0,2,()=>{samples++;return 32767;},ref award);
            Check(samples==1 && I(actor,0x170)==f.hp && I(actor,0x16c)==f.hp && I(actor,0x1f4)==f.attack && I(actor,0x180)==f.interval && I(actor,0x1ec)==1,
                "Previously rejected shipped INF type projects actual PE attribute row and retains native RNG consumption.");
            Check(I(actor,0x355)==99 && I(actor,0x35d)==123,"Types45..49 do not acquire zeroed boss-cycle fields from the38..44 switch.");
            Check(award==f.hp/4+(f.type switch {46 or 48=>100,47 or 49=>200,_=>0}),"Actual high-type base award uses its own row31.");
        }
        byte[] invalid=new byte[CombatSceneDefinition.RuntimeRecordSize],invalidInfo=new byte[CombatInfoCharacter.RuntimeSize];
        W(invalidInfo,0xafc,52);W(invalid,0x170,10000);int untouchedAward=81;
        CombatEnemyAttributes.Initialize(invalid,invalidInfo,99,2,()=>throw new Exception("Early native return must not consume RNG."),ref untouchedAward);
        Check(untouchedAward==81 && I(invalid,0x170)==10000,"Native high-HP early return precedes table/domain/region access.");
        W(invalid,0x170,9999);byte[] before=invalid.ToArray();bool rejected=false;
        try { CombatEnemyAttributes.Initialize(invalid,invalidInfo,0,2,()=>0,ref untouchedAward); }
        catch(NotSupportedException) { rejected=true; }
        Check(rejected && before.AsSpan().SequenceEqual(invalid) && untouchedAward==81,"First ASCII row52 is rejected before any actor mutation or invented balance.");
        return "Combat shipped INF/table domain checks passed.";
    }
    private static int I(byte[] a,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(offset,4));
    private static void W(byte[] a,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(offset,4),value);
    private static void Check(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
