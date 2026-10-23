using Atria.Game.Combat;
using Atria.Render.Codecs;

namespace Atria.Tests.Combat;

public static class CombatTransitionTests
{
    public static string Run(string root)
    {
        BitmapFont large=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG.FNT")),false);
        ushort[] source=Enumerable.Repeat((ushort)0xffff,640*480).ToArray(),target=new ushort[source.Length];
        var raster=new CombatTransitionRaster(source,large,[]);var runtime=new CombatTransitionRuntime();
        raster.Draw(runtime.Frame,target);
        Check(target[24*640+100]==0 && target[25*640+100]==0xffff && target[454*640+100]==0xffff &&
            target[455*640+100]==0 && target[100*640+32]==0 && target[100*640+33]==0xffff &&
            target[100*640+606]==0xffff && target[100*640+607]==0,"First shutter frame uses all four inclusive rectangle boundaries.");
        source.AsSpan().Clear(); //Compositor owns a frozen cached scene, not the changing output buffer.
        int labels=0,whole=0;
        for(int i=0;i<CombatTransitionRuntime.FrameCount;i++)
        {
            CombatTransitionFrame frame=runtime.Frame;
            Check(frame.Index==i && frame.Bottom==455-24*i,"Exact native serial frame order.");
            raster.Draw(frame,target);
            if(frame.DrawLabel) labels++;if(frame.EntireBlack)whole++;
            if(i==8) Check(target.Count(p=>p==0xffff)==62*46,"Last open frame has an exact62x46 visible source window.");
            if(i>=9) Check(target.All(p=>p==0),"Every whole-black frame starts with a fresh source copy then covers it.");
            runtime.AfterPresented();
        }
        Check(runtime.Completed && runtime.PresentedFrames==19 && labels==17 && whole==10,"Nineteen presentations, seventeen label opportunities, ten full closures.");
        try { runtime.AfterPresented();throw new Exception("Transition advanced beyond its real final step."); }
        catch(InvalidOperationException) {}
        var labelled=new CombatTransitionRaster(Enumerable.Repeat((ushort)0xffff,640*480).ToArray(),large,"A\0ignored"u8);
        var closed=new CombatTransitionFrame(9,240,239,320,319,true,true);
        labelled.Draw(closed,target);
        Check(target.Any(p=>p!=0),"Recovered label draws over the closed shutter using actual ENG glyphs.");
        for(int y=0;y<480;y++) for(int x=0;x<640;x++)
            if(x<306 || x>=322 || y<224 || y>=248) Check(target[y*640+x]==0,"One raw-byte label uses native310-4*length origin and16x24 glyph bounds.");
        labelled.Draw(new(17,432,47,576,63,true,false),target);
        Check(target.All(p=>p==0),"Bottom47 suppresses retained label even though the previous closed frame could draw it.");
        return "Combat transition: nineteen exact inclusive shutter steps, source snapshot, strict label cutoff and actual large-font placement passed.";
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
