using System.Globalization;
using System.Text;
using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Dungeon;

public sealed class DungeonModalRenderer(SpriteFile tiles,BitmapFont font,IReadOnlyList<ReadOnlyMemory<byte>> messages)
{
    private static ReadOnlySpan<ushort> ValueStyle=>[0xffff,0xff1f,0xfe9f,0xfe1f,0xfd9f,0xfd1f,0xfc9f,0xfc9f,0xfb9f,0xfb1f,0xfa9f,0xfa1f,0xe19c,0xd19a,0xc198,0xb196,0xa114,0x9112,0x8110,0x710e,0x608c,0x588b,0x508a,0x4889,0x4008,0x3807,0x3006,0x2805,0x2004,0x1803,0x1002,0x0801];
    public void Draw(DungeonController controller,int availableTorches,ReadOnlySpan<byte> dynamicText,Span<ushort> target)
    {
        int modal=controller.Modal;if(modal==0) return;
        UiRaster.TintHalf(target,640,480,640,174,174,292,modal==99?36:100,0x000f);
        UiRaster.DrawPanel(tiles,160,160,8,modal==99?0:2,target);
        switch(modal)
        {
            case 3:
                Line(194,200,184,NativeUiPalette.Normal,target);Line(195,200,210,NativeUiPalette.Normal,target);
                Line(196,200,244,controller.Selection==0?NativeUiPalette.Selected:NativeUiPalette.Normal,target);
                Line(197,340,244,controller.Selection==1?NativeUiPalette.Selected:NativeUiPalette.Normal,target);break;
            case 6:
                if(controller.TreasureKind==1) {Line(203,210,200,NativeUiPalette.Normal,target);Line(204,210,230,NativeUiPalette.Normal,target);Text(dynamicText,298,230,NativeUiPalette.Selected,target);}
                else {Line(205,200,195,NativeUiPalette.Normal,target);Line(206,200,215,NativeUiPalette.Normal,target);Line(207,200,235,NativeUiPalette.Normal,target);Text(dynamicText,264,235,ValueStyle,target);}break;
            case 99:
                if(availableTorches<1) Line(208,220,184,NativeUiPalette.Title,target);
                else {Line(209,220,184,NativeUiPalette.Normal,target);Text(Encoding.ASCII.GetBytes(controller.TorchCost.ToString(CultureInfo.InvariantCulture)),388,184,ValueStyle,target);}break;
            default:throw new InvalidDataException("Unknown native dungeon modal.");
        }
    }
    private void Line(int index,int x,int y,ReadOnlySpan<ushort> style,Span<ushort> target)=>Text(messages[index].Span,x,y,style,target);
    private void Text(ReadOnlySpan<byte> text,int x,int y,ReadOnlySpan<ushort> style,Span<ushort> target)=>UiRaster.DrawStyledText(font,text,style,0,target,640,480,640,x,y);
    public static byte[] BuildDynamicText(DungeonArrivalEffect effect,IReadOnlyList<ReadOnlyMemory<byte>> messages,IReadOnlyList<ReadOnlyMemory<byte>> itemNames)
    {
        byte[] Number(int value)=>Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
        return effect.Kind switch
        {
            DungeonArrivalEffectKind.TreasureMoney=>Join(Number(effect.Value),"GOLD"u8,[]),
            DungeonArrivalEffectKind.TreasureItem=>Join(itemNames[effect.Value].Span,Number(effect.Value2),[]),
            DungeonArrivalEffectKind.TreasureTorch=>Join(messages[193].Span,"TORCH"u8,Number(effect.Value)),
            _=>throw new ArgumentException("Effect has no dungeon dynamic message.")
        };
    }
    private static byte[] Join(ReadOnlySpan<byte> a,ReadOnlySpan<byte> b,ReadOnlySpan<byte> c)
    {byte[] result=new byte[a.Length+b.Length+c.Length];a.CopyTo(result);b.CopyTo(result.AsSpan(a.Length));c.CopyTo(result.AsSpan(a.Length+b.Length));return result;}
}
