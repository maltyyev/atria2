using Atria.Game.Credits;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Credits;

public static class CreditsTests
{
    public static string Run(string root)
    {
        var controller=new CreditsController();controller.Enter();
        for(int i=0;i<10;i++)controller.Input(()=>true,()=>false,()=>throw new InvalidOperationException());
        Check(controller.Speed==1,"held confirm strict ten threshold");
        controller.Input(()=>true,()=>false,()=>throw new InvalidOperationException());Check(controller.Speed==2,"eleventh held poll doubles speed");
        for(int i=0;i<5;i++)controller.Input(()=>false,()=>false,()=>throw new InvalidOperationException());
        Check(controller.Speed==1,"fifth released poll halves speed");
        for(int i=0;i<3;i++)controller.Input(()=>true,()=>false,()=>throw new InvalidOperationException());
        controller.Enter();Check(controller.SpeedCounter==3,"entry retains global speed counter");
        var first=controller.AdvanceFrame();Check(first.TextY==400&&first.Pictures[0].Frame==0&&first.Pictures[1].Frame==10,"first native composition");
        for(int i=1;i<721;i++)controller.AdvanceFrame();Check(controller.Picture==1,"picture strictly exceeds720");
        var transition=controller.AdvanceFrame();Check(transition.Pictures.Length==3&&transition.Pictures[0].Frame==0&&transition.Pictures[1] is {Frame:1,Half:true},"single-pass oldcopy/newhalf transition");
        Check(controller.AdvanceFrame().Pictures[0] is {Frame:1,Half:false},"following picture returns fullcopy");
        controller.Enter();for(int i=0;i<9120;i++)controller.AdvanceFrame();Check(!controller.Completed,"completion tested before final scroll update");
        controller.AdvanceFrame();bool closed=false;controller.Input(()=>throw new InvalidOperationException(),()=>throw new InvalidOperationException(),()=>closed=true);
        Check(closed,"completed credits close without polling keys");
        var text=CreditsText.Parse(File.ReadAllBytes(Path.Combine(root,"CREDIT.DAT")));
        using var archive=BinderArchive.Open(Path.Combine(root,"AT2SPR.000"));
        var sprites=SpriteFile.Decode(archive.ReadMember(archive.FindResource("CREATOR"u8)));
        var font=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG1.FNT")),true);
        ushort[] background=new ushort[640*480],target=new ushort[640*480],again=new ushort[640*480];
        controller.Enter();CreditsFrame frame=first;
        for(int i=0;i<80;i++)frame=controller.AdvanceFrame();
        CreditsRenderer.Draw(frame,text,sprites,font,background,target);CreditsRenderer.Draw(frame,text,sprites,font,background,again);
        Check(target.AsSpan().SequenceEqual(again)&&target.Any(x=>x!=0),"real CREATOR sprite/font/credit data bounded deterministic raster");
        return "Credits: real data/raster, held speed thresholds, retained counter, image transition and completion";
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
