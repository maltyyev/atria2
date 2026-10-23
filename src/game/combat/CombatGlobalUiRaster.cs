using System.Buffers.Text;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Combat;

public sealed record CombatGlobalUiAssets(BitmapFont Font,IReadOnlyList<ReadOnlyMemory<byte>> Labels)
{
    public static CombatGlobalUiAssets Load(string root)=>new(
        BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG1.FNT")),true),
        LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root,"BATTLE.DAT"))));
}

/// <summary>407fe0 text composition. Combat entry43ce90(1) selects ENG1,8x16/advance8.</summary>
public sealed class CombatGlobalUiRaster(CombatGlobalUiAssets assets)
{
    public void Draw(CombatGlobalUiRuntime ui,bool automationEnabled,bool paused,Span<ushort> target)
    {
        if(target.Length!=640*480 || assets.Font.Width!=8 || assets.Font.Height!=16 || assets.Labels.Count<19)
            throw new ArgumentException("Incomplete native combat global UI assets/raster.");
        if(ui.NoticePhase!=0)
        {
            Text(ui.NoticeText.Span,ui.NoticeX+1,ui.NoticeY+1,0x7bef,target);
            Text(ui.NoticeText.Span,ui.NoticeX,ui.NoticeY,0xffff,target);
        }
        if(automationEnabled)
        {
            Text(assets.Labels[13].Span,11,51,0x7bef,target);
            Text(assets.Labels[13].Span,10,50,0xffe0,target);
        }
        if(paused)
        {
            Text(assets.Labels[18].Span,121,201,0x7bef,target);
            Text(assets.Labels[18].Span,120,200,0xffe0,target);
        }
        if(ui.SettingsNoticeVisible)
        {
            Span<byte> text=stackalloc byte[64];int length=FormatSettings(ui.VisualDetail,ui.Schedule.Period,text);
            //The native order/offset is intentionally unusual: dark at250,50 then yellow at251,51.
            Text(text[..length],250,50,0x4208,target);
            Text(text[..length],251,51,0xffe0,target);
        }
    }
    public static int FormatSettings(int detail,int period,Span<byte> text)
    {
        ReadOnlySpan<byte> first="Detail Level "u8,second="  Speed="u8;
        if(text.Length<64) throw new ArgumentException("Settings text scratch must contain64 bytes.");
        first.CopyTo(text);int length=first.Length;
        if(!Utf8Formatter.TryFormat(detail,text[length..],out int written)) throw new InvalidOperationException("Detail text did not fit.");
        length+=written;second.CopyTo(text[length..]);length+=second.Length;
        if(!Utf8Formatter.TryFormat(period,text[length..],out written)) throw new InvalidOperationException("Speed text did not fit.");
        return length+written;
    }
    private void Text(ReadOnlySpan<byte> text,int x,int y,ushort color,Span<ushort> target)
        =>assets.Font.DrawText(text,target,640,480,640,x,y,color);
}
