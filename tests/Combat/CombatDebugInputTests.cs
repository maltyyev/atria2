using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Game.State;

namespace Atria.Tests.Combat;

public static class CombatDebugInputTests
{
    public static string Run()
    {
        var state=new GameState();
        //Distinct bytes reveal accidental writes into neighboring stats, unused party slots or globals.
        for(int i=0;i<state.PartyRecords.Length;i++) state.PartyRecords[i]=unchecked((byte)(i*37+11));
        for(int actor=0;actor<10;actor++)
        {
            state.SetPartyInt(actor,60,actor switch {0 or 3 or 5 or 6 or 9=>1,1=>2,2=>-1,_=>0});
            state.SetPartyInt(actor,192,actor==3?int.MaxValue-4:actor*100);
        }
        byte[] expected=state.PartyRecords.ToArray();var queries=new List<byte>();bool first=false,second=true;
        var input=new CombatScanInput(scan=>{queries.Add(scan);return scan==3?first:scan==9 && second;});
        Check(CombatDebugInput.ApplyHeldShortcut(state,input)==0 && queries.SequenceEqual(new byte[]{3}) && state.PartyRecords.AsSpan().SequenceEqual(expected),
            "First held test short-circuits the second and preserves the entire raw party bank.");
        first=true;second=false;queries.Clear();
        Check(CombatDebugInput.ApplyHeldShortcut(state,input)==0 && queries.SequenceEqual(new byte[]{3,9}) && state.PartyRecords.AsSpan().SequenceEqual(expected),
            "Both original held scans are required in exact query order.");
        second=true;queries.Clear();
        for(int pass=0;pass<2;pass++)
        {
            foreach(int actor in new[]{0,3,5})
            {
                Span<byte> field=expected.AsSpan(actor*GameState.PartyRecordSize+192,4);
                BinaryPrimitives.WriteInt32LittleEndian(field,unchecked(BinaryPrimitives.ReadInt32LittleEndian(field)+10));
            }
            Check(CombatDebugInput.ApplyHeldShortcut(state,input)==3 && state.PartyRecords.AsSpan().SequenceEqual(expected),
                "Each held opportunity adds10 with signed32 wrapping only to strict membership1 in the first six records.");
        }
        Check(state.GetPartyInt(3,192)==unchecked(int.MaxValue+16),"Original ADD preserves int32 overflow rather than a new XP cap.");
        Check(input.Query(9) && !input.Query(9),"Held shortcut does not consume edge-query latches.");
        Check(state.Globals.All(value=>value==0) && state.PendingExperience==0 && state.ExperiencePending==0,
            "Shortcut edits raw party XP only; it does not fabricate allocation or level-up events.");
        return "Combat held debug shortcut raw-layout checks passed.";
    }
    private static void Check(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
