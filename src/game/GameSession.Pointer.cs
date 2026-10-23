using Atria.Render.Scenes;

namespace Atria.Game;

public sealed partial class GameSession
{
    private int pointerX=-1,pointerY=-1;
    private void UpdatePointerFocus()
    {
        int x=controls.MouseX,y=controls.MouseY;if(x==pointerX&&y==pointerY)return;pointerX=x;pointerY=y;
        if(x<0||dialogueActive||battleActive)return;
        if(actions.Depth>=0&&actions.RenderEnabled){actions.PointerSelect(x,y);return;}
        if(state.Mode==200&&menu.Confirmation)menu.PointerConfirm(x,y);
        else if(state.Mode==200&&!menu.Confirmation)
        {
            for(int i=0;i<6;i++)
            {var sprite=menuSprites.Frames[i];int sx=MainMenuPresentation.SelectionX[i],sy=MainMenuPresentation.SelectionY[i];if(x>=sx&&x<sx+sprite.Width&&y>=sy&&y<sy+sprite.Height){menu.PointerSelect(i,saveDisabled);return;}}
        }
        else if(state.Mode is 220 or 230)persistenceMenu.PointerSelect(x,y);
        else if(state.Mode==400&&!town.InputDeferred&&!town.LeaveConfirmation&&townAssets?.Objects is {} sprites)
        {
            for(int i=SodScenePresentation.SelectableCount(town.Scene)-1;i>=0;i--)
            {
                var obj=town.Scene.GetObject(i);int index=obj.GetInteger(2);if((uint)index>=(uint)sprites.Frames.Length)continue;
                var sprite=sprites.Frames[index];int sx=obj.GetInteger(0),sy=obj.GetInteger(1);
                if(x>=sx&&x<sx+sprite.Width&&y>=sy&&y<sy+sprite.Height){town.PointerSelect(i);return;}
            }
        }
        else if(state.Mode==430&&trade is {} current)current.PointerSelect(x,y);
        else if(state.Mode==500&&!statusRuntime.Menu.DetailActive&&x is >=20 and <148&&y is >=240 and <430)statusRuntime.Menu.PointerPage((y-240)/38);
    }
}
