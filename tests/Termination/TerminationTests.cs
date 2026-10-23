using Atria.Game.Termination;
using Atria.Game.Menu;
using Atria.Resources;
using Atria.Render.Codecs;

namespace Atria.Tests.Termination;

public static class TerminationTests
{
    public static string Run(string root)
    {
        var quit=new QuitController();List<QuitEffect> effects=[];List<QuitKey> queries=[];
        void Tick(params QuitKey[] keys){effects.Clear();queries.Clear();quit.Input(key=>{queries.Add(key);return keys.Contains(key);},effects.Add);}
        Tick(QuitKey.Left,QuitKey.Confirm);Check(effects.Single()==new QuitEffect(QuitEffectKind.Sound,1)&&queries.SequenceEqual(new[]{QuitKey.Right,QuitKey.Left}),"left at yes still sounds and suppresses confirm");
        Tick(QuitKey.Right);Tick(QuitKey.Right,QuitKey.Cancel);Check(effects.Count==0&&queries.Count==1,"right at no suppresses cancel without sound");
        Tick(QuitKey.Confirm);Check(effects[1].Kind==QuitEffectKind.ReturnMenuRetained,"confirm no writes retained menu mode");
        quit.Initialize();Tick(QuitKey.Confirm);Check(effects[1].Kind==QuitEffectKind.RequestWindowClose,"confirm yes requests real close");
        Tick(QuitKey.Cancel,QuitKey.Confirm);Check(!queries.Contains(QuitKey.Confirm)&&effects[0].Value==4,"cancel precedes confirm");
        var menu=new MainMenuController();menu.Enter();menu.AdvanceLogicalFrame();menu.Input(new(Next:true),false,_=>{});
        for(int i=0;i<40;i++)menu.AdvanceLogicalFrame();int reveal=menu.Reveal;var visual=menu.VisualState;
        menu.ReturnFromTermination();Check(menu.Selection==0&&menu.PanelItem==0&&menu.Reveal==reveal&&menu.VisualState==visual&&menu.AdvanceLogicalFrame().LoadResources,"termination resets only menu selection and reload gate");
        var defeat=new DefeatController();for(int i=0;i<30;i++)defeat.AdvanceFrame();Check(defeat.DrawCounter==20,"native defeat counter saturates20");
        bool returned=false;defeat.Input(()=>false,()=>returned=true);Check(!returned,"defeat waits real confirm");defeat.Input(()=>true,()=>returned=true);Check(returned,"defeat real confirm returns");
        var ending=new EndingController();List<EndingEffect> endEffects=[];ending.Enter(1,endEffects.Add);Check(endEffects.Single().Value==0&&ending.TextY==360,"ending initial background and text origin");
        endEffects.Clear();for(int i=0;i<601;i++)ending.AdvanceFrame();ending.Input(endEffects.Add);Check(endEffects.Count==0,"picture threshold is strict row25*24");
        ending.AdvanceFrame();ending.Input(endEffects.Add);Check(endEffects.Single()==new EndingEffect(EndingEffectKind.LoadBackground,16),"next input applies preceding draw's pending image");
        byte[] raw=File.ReadAllBytes(Path.Combine(root,"END.DAT"));var font=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG1.FNT")),true);
        using var bitmaps=BinderArchive.Open(Path.Combine(root,"AT2BMP.000"));
        for(int selection=1;selection<=9;selection++)
        {
            var text=EndingText.Parse(raw,selection);var definition=EndingCatalog.Get(selection);
            List<int> pictures=[];int returns=0;ending.Enter(selection,effect=>pictures.Add(effect.Value));
            for(int tick=0;tick<definition.TotalRows*24+2;tick++)
            {
                ending.Input(effect=>{if(effect.Kind==EndingEffectKind.LoadBackground)pictures.Add(effect.Value);else returns++;});
                if(returns!=0)break;ending.AdvanceFrame();
            }
            Check(returns==1&&pictures.SequenceEqual(definition.Pictures),"all native ending picture paths complete without extra images");
            foreach(int picture in pictures)
            {
                var bitmap=BmpImage.Decode(bitmaps.ReadMember(bitmaps.FindResource(System.Text.Encoding.ASCII.GetBytes($"END{picture:00}"))));
                Check(bitmap.Width==640&&bitmap.Height==480,"actual ending bitmap dimensions");
            }
            ushort[] target=new ushort[640*480];TerminationRenderer.DrawEnding(text,150,font,target);
            Check(target.Take(150*640).All(v=>v==0)&&target.Skip(351*640).All(v=>v==0),"ending actual font respects native vertical clip");
        }
        var gameover=BmpImage.Decode(bitmaps.ReadMember(bitmaps.FindResource("GAMEOVER"u8)));Check(gameover.Width==640&&gameover.Height==480,"actual defeat background");
        using var sprites=BinderArchive.Open(Path.Combine(root,"AT2SPR.000"));
        var etc=SpriteFile.Decode(sprites.ReadMember(sprites.FindResource("ETC"u8)));
        var large=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG.FNT")),false);
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        ushort[] quitPixels=new ushort[640*480];TerminationRenderer.DrawQuit(quit,etc,large,messages,quitPixels);Check(quitPixels.Any(v=>v!=0),"actual quit UI messages306..308 and ETC tiles");
        return "Termination: exact quit priority, retained menu, defeat confirm, all9 actual ending resources/timelines/clipping";
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
