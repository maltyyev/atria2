using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Game.ActionMenu;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Tests.ActionMenu;

public static class ActionMenuTests
{
    public static string Run(string root)
    {
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        var menu=new ActionMenuController(); var commands=new List<ActionMenuCommand>();
        Check(menu.Depth==-1 && menu.AdvanceLogicalFrame() is null,"closed initial state");
        menu.OpenTown(true,messages);
        var first=menu.AdvanceLogicalFrame()!;
        Check(first.X==208 && first.Y==128 && first.HorizontalTiles==5 && first.VerticalTiles==5 && first.Labels.Length==4,"town0 layout");
        Check(first.Labels[0].Span.SequenceEqual(messages[271].Span) && first.Labels[3].Span.SequenceEqual(messages[274].Span),"town label slots");
        menu.Input(new(Previous:true,Next:true,Cancel:true,Accept:true),commands.Add);
        Check(menu.Selection==3 && commands.SequenceEqual([new(ActionMenuEffect.Sound,1)]),"previous wins all other keys");
        Check(menu.AdvanceLogicalFrame()!.Flash==2 && menu.AdvanceLogicalFrame()!.Flash==1 && menu.AdvanceLogicalFrame()!.Flash==0,"two drawn flash states");
        menu.Input(new(Next:true),commands.Add); Check(menu.Selection==0,"next wraps");
        menu.OpenTown(false,messages);
        Check(menu.Depth==1,"nested menu stack");
        var empty=menu.AdvanceLogicalFrame()!;
        Check(empty.X==208 && empty.Y==160 && empty.Type==66 && empty.Labels.Length==3 && empty.Labels[0].Span.SequenceEqual(messages[272].Span),"empty town layout and labels");
        commands.Clear(); menu.Input(new(Cancel:true,Accept:true),commands.Add);
        Check(menu.Depth==0 && commands.SequenceEqual([new(ActionMenuEffect.Sound,4)]),"cancel wins accept and pops only top");
        for(int choice=0;choice<4;choice++)
        {
            commands.Clear(); menu.Input(new(Accept:true),commands.Add);
            Check(commands.SequenceEqual([new(ActionMenuEffect.Sound,3),new(ActionMenuEffect.InvokeTownChoice,choice,0)]),"regular dispatch "+choice);
            menu.Input(new(Next:true),_=>{});
        }
        menu.CloseAll(); Check(menu.Depth==-1 && !menu.RenderEnabled,"native close-all state");
        menu.OpenTown(false,messages);
        for(int choice=0;choice<3;choice++)
        {
            commands.Clear(); menu.Input(new(Accept:true),commands.Add);
            Check(commands[^1]==new ActionMenuCommand(ActionMenuEffect.InvokeTownChoice,choice,66),"empty dispatch "+choice);
            menu.Input(new(Next:true),_=>{});
        }
        menu.SetRenderEnabled(false); Check(menu.AdvanceLogicalFrame() is null,"render gate suppresses draw and clock");
        menu.CloseAll(); menu.Open(10,20,5,4,0,messages);
        Check(menu.AdvanceLogicalFrame()!.VerticalTiles==5,"even vertical count becomes odd");
        menu.OpenTown(true,messages); menu.OpenTown(true,messages); menu.OpenTown(true,messages);
        bool capacityRejected=false; try { menu.OpenTown(true,messages); } catch(InvalidOperationException) { capacityRejected=true; }
        Check(capacityRejected && menu.Depth==3,"bounded four-record stack");
        bool unknownRejected=false; try { menu.Open(0,0,5,5,99,messages); } catch(NotSupportedException) { unknownRejected=true; }
        Check(unknownRejected,"unrecovered menu handler fails explicitly");

        foreach(var sample in new[]{(Mode:410,Type:3,First:286,Count:2),(Mode:420,Type:5,First:288,Count:2),(Mode:430,Type:6,First:290,Count:4)})
        {
            menu.CloseAll();menu.OpenInterior(sample.Mode,messages);var frame=menu.AdvanceLogicalFrame()!;
            Check(frame.Type==sample.Type&&frame.Labels.Length==sample.Count&&frame.Labels[0].Span.SequenceEqual(messages[sample.First].Span),"native interior labels/type");
            commands.Clear();menu.Input(new(Accept:true),commands.Add);
            Check(commands[^1]==new ActionMenuCommand(ActionMenuEffect.InvokeChoice,0,sample.Type),"interior explicit choice dispatch");
            commands.Clear();menu.Input(new(Cancel:true),commands.Add);
            Check(menu.Depth==0&&commands[^1].Kind==ActionMenuEffect.LeaveInterior,"interior cancel requires pre_out rather than generic pop");
        }
        menu.CloseAll();menu.OpenTalk(new ReadOnlyMemory<byte>[] {"one"u8.ToArray(),"two"u8.ToArray()},true,430);
        var talk=menu.AdvanceLogicalFrame()!;
        Check(talk.Type==8&&talk.HorizontalTiles==7&&talk.VerticalTiles==3&&talk.X==176&&talk.Y==160,"native custom talk geometry");
        commands.Clear();menu.Input(new(Accept:true),commands.Add);Check(commands[^1]==new ActionMenuCommand(ActionMenuEffect.InvokeChoice,0,8),"talk actual choice dispatch");
        commands.Clear();menu.Input(new(Cancel:true),commands.Add);Check(commands[^1].Kind==ActionMenuEffect.ReturnInteriorAction&&menu.Depth==0,"talk cancel requests original mode action menu");
        using(var amf=BinderArchive.Open(Path.Combine(root,"AT2AMF.000")))
        {
            byte[] mapBytes=amf.ReadMember(amf.FindResource("ground1.amf"u8));
            var map=WorldMapContext.Parse(mapBytes);
            var state=new GameState {WorldX=4,WorldY=4};
            void Set(int offset,int value){BinaryPrimitives.WriteInt32LittleEndian(mapBytes.AsSpan(AmfMap.HeaderSize+(4*10+4)*328+offset,4),value);map=WorldMapContext.Parse(mapBytes);}
            Set(8,0);Set(24,0);Set(112,0);Set(120,0);
            menu.CloseAll();menu.OpenWorld(map,state,_=>true,messages);
            var world=menu.AdvanceLogicalFrame()!;
            Check(world.Type==1&&world.VerticalTiles==3&&world.Labels.Length==3&&world.Labels[2].Span.SequenceEqual(messages[277].Span),"world base three labels and native blocked guard");
            Set(8,1);menu.CloseAll();menu.OpenWorld(map,state,_=>true,messages);
            world=menu.AdvanceLogicalFrame()!;
            Check(world.VerticalTiles==5&&world.Labels.Length==4&&world.Labels[3].Span.SequenceEqual(messages[278].Span),"world blocked-scene row and odd geometry");
            Set(24,1);menu.CloseAll();menu.OpenWorld(map,state,_=>true,messages);world=menu.AdvanceLogicalFrame()!;
            Check(world.Labels.Length==5&&world.Labels[3].Span.SequenceEqual(messages[279].Span)&&world.Labels[4].IsEmpty,"native simultaneous predicates overwrite slot3 and increment count twice");
            menu.CloseAll();menu.OpenWorldMessage(77,messages,map,state,_=>true);
            Check(menu.AdvanceLogicalFrame()!.VerticalTiles==5,"world message layout also applies both native predicates");
            menu.CloseAll();menu.OpenWorldReveal(messages);commands.Clear();menu.Input(new(Accept:true),commands.Add);
            Check(commands[^1]==new ActionMenuCommand(ActionMenuEffect.InvokeChoice,0,2),"reveal cursor menu dispatch");
        }
        foreach(var item in new[]{(Type:77,First:296,Count:2),(Type:99,First:294,Count:2),(Type:88,First:360,Count:1)})
        {
            menu.CloseAll();menu.OpenWorldMessage(item.Type,messages);var world=menu.AdvanceLogicalFrame()!;
            Check(world.HorizontalTiles==9&&world.Labels.Length==item.Count&&world.Labels[0].Span.SequenceEqual(messages[item.First].Span),"world message labels");
            commands.Clear();menu.Input(new(Accept:true),commands.Add);
            Check(item.Type==88?menu.Depth==-1&&commands.Count==1:commands[^1]==new ActionMenuCommand(ActionMenuEffect.InvokeChoice,0,item.Type),"native world message accept handler");
        }
        foreach(bool hasSwitch in new[]{false,true})
        {
            menu.CloseAll();menu.OpenDungeon(hasSwitch,messages);var dungeon=menu.AdvanceLogicalFrame()!;
            Check(dungeon.Type==9&&dungeon.HorizontalTiles==5&&dungeon.VerticalTiles==3&&dungeon.Labels.Length==(hasSwitch?3:2)&&dungeon.Labels[0].Span.SequenceEqual(messages[283].Span),"native dungeon type9 labels and fixed geometry");
            commands.Clear();menu.SetSelection(hasSwitch?2:1);menu.Input(new(Accept:true),commands.Add);
            Check(commands[^1]==new ActionMenuCommand(ActionMenuEffect.InvokeChoice,hasSwitch?2:1,9),"dungeon handler receives recovered selection");
        }
        byte[] glyphs=new byte[4096]; for(int row=0;row<16;row++) glyphs[65*16+row]=0x80;
        var testFont=BitmapFont.DecodeEng(glyphs,true);
        var blankTiles=new SpriteFile(false,Enumerable.Range(0,12).Select(_=>new SpriteFrame(0,0,0,[],[],1,1,[])).ToArray());
        var raster=new ActionMenuRenderer(blankTiles,testFont);
        ushort[] target=new ushort[640*480];
        var four=new ActionMenuFrame(208,128,5,5,0,0,0,Enumerable.Repeat<ReadOnlyMemory<byte>>("A"u8.ToArray(),4).ToArray());
        Array.Fill(target,(ushort)0x7bef); raster.Draw(four,target);
        Check(target[180*640+253]==NativeUiPalette.Selected[0] && target[195*640+253]==NativeUiPalette.Selected[26],"even row count start and gradient");
        Check(target[179*640+252]==0 && target[212*640+253]==0x7bef,"selected outline and inactive gray");
        raster.Draw(four with {Flash=1},target); Check(target[195*640+253]==0xffe0,"yellow flash entire glyph");
        var three=four with {Y=160,VerticalTiles=3,Type=66,Labels=four.Labels[..3],Flash=2};
        Array.Fill(target,(ushort)0x7bef); raster.Draw(three,target);
        Check(target[198*640+253]==0xffff && target[213*640+253]==0xffff,"odd row count start and white flash");
        using var archive=BinderArchive.Open(Path.Combine(root,"AT2SPR.000"));
        var etc=SpriteFile.Decode(archive.ReadMember(archive.FindExact("ETC"u8)));
        var font=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"eng1.fnt")),true);
        var renderer=new ActionMenuRenderer(etc,font);
        foreach(bool hasObjects in new[]{true,false})
        {
            menu.CloseAll(); menu.OpenTown(hasObjects,messages);
            for(int i=0;i<(hasObjects?4:3);i++)
            { Array.Fill(target,(ushort)0x7bef); renderer.Draw(menu.AdvanceLogicalFrame()!,target); menu.Input(new(Next:true),_=>{}); }
        }
        return "Action-menu checks passed (town0/66, input priorities, nested stack, exact labels/dispatch, flash, native raster coordinates, real ETC/font rendering).";
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception("Action menu: "+message); }
}




