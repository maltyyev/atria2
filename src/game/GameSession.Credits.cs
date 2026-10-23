using Atria.Game.Credits;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly CreditsController credits=new();
    private CreditsText? creditsText;
    private SpriteFile? creditsSprites;
    private ushort[]? creditsBackground;
    private void EnterCredits()
    {
        creditsText??=CreditsText.Parse(File.ReadAllBytes(Path.Combine(root,"CREDIT.DAT")));
        creditsSprites??=SpriteFile.Decode(Read("AT2SPR.000","CREATOR"u8));
        if(creditsBackground is null)
        {
            var bitmap=BmpImage.Decode(Read("AT2BMP.000","CREATOR"u8));
            if(bitmap.Width!=640||bitmap.Height!=480)throw new InvalidDataException("Invalid credits background.");
            creditsBackground=new ushort[bitmap.Pixels.Length];
            for(int i=0;i<creditsBackground.Length;i++)
            {uint rgb=bitmap.Pixels[i];creditsBackground[i]=(ushort)(((rgb>>8)&0xf800)|((rgb>>5)&0x7e0)|((rgb>>3)&31));}
        }
        musicId=26;credits.Enter();music.Update(musicId);
    }
    private void HandleCreditsInput()=>credits.Input(()=>host.IsHeld(bindings.Confirm),()=>Query(bindings.Cancel),()=>
    {
        // Native frees rows/sprite and restores text clip0..479 before popping. Managed immutable cache is reusable.
        if(!modes.TryPop(out var transition))throw new InvalidOperationException("Credits return stack is empty.");
        if(transition.RestoreFlag)actions.SetRenderEnabled(true);EnterMode(transition.Mode);actions.SetRenderEnabled(true);
    });
    private void ComposeCredits(Span<ushort> target)=>CreditsRenderer.Draw(credits.AdvanceFrame(),
        creditsText??throw new InvalidOperationException("Credits text missing."),
        creditsSprites??throw new InvalidOperationException("Credits sprite missing."),smallFont,
        creditsBackground??throw new InvalidOperationException("Credits background missing."),target);
}
