using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Render.Scenes;
namespace Atria.Game.Town;

public sealed record TownSnapshot(int TownId,int Selection,int SavedSelection,bool LeaveConfirmation,bool LeaveYes,
    int InputDelay,int MarkerCounter,int MarkerPhase,int MotionX,int MotionY,int LeaveFlash,
    bool PendingPreOut,bool PendingPreEnter,bool ActionPreOut,int[] EffectFrames,int[] EffectCounters,byte[] SceneName,bool ObjectSpritesReleased);
public sealed partial class TownController
{
    public TownSnapshot CaptureRuntime()=>new(TownId,Selection,SavedSelection,LeaveConfirmation,LeaveYes,inputDelay,
        markerCounter,markerPhase,motionX,motionY,leaveFlash,pendingPreOut,pendingPreEnter,actionPreOut,
        (int[])effectFrames.Clone(),(int[])effectCounters.Clone(),loadedSceneName.ToArray(),sprites is null);
    public void RestoreRuntime(TownSnapshot value,SodV2? restoredScene,SpriteFile? restoredSprites)
    {
        ArgumentNullException.ThrowIfNull(value);
        if((uint)value.TownId>=52 || value.Selection<0 || value.SavedSelection<0 || value.EffectFrames is not {Length:5} ||
            value.EffectCounters is not {Length:5} || value.SceneName is null || value.SceneName.Length>4096 ||
            (uint)value.MarkerCounter>1 || (uint)value.MarkerPhase>=9)
            throw new InvalidDataException("Invalid retained town state.");
        if(restoredScene is {Count:>0} && (value.Selection>=SodScenePresentation.SelectableCount(restoredScene) ||
            value.SavedSelection>=SodScenePresentation.SelectableCount(restoredScene) || (!value.ObjectSpritesReleased && restoredSprites is null)))
            throw new InvalidDataException("Retained town selection or sprites do not match scene.");
        foreach(var effect in TownEffectCatalog.Records)
            if(effect.Town==value.TownId && ((uint)value.EffectFrames[effect.Slot]>=(uint)effect.FrameCount ||
                (uint)value.EffectCounters[effect.Slot]>(uint)effect.CounterThreshold))
                throw new InvalidDataException("Retained active town effect cursor is invalid.");
        byte[] member=(byte[])value.SceneName.Clone();
        scene=restoredScene;sprites=value.ObjectSpritesReleased?null:restoredSprites;loadedSceneName=member;
        TownId=value.TownId;Selection=value.Selection;SavedSelection=value.SavedSelection;LeaveConfirmation=value.LeaveConfirmation;
        LeaveYes=value.LeaveYes;inputDelay=value.InputDelay;markerCounter=value.MarkerCounter;markerPhase=value.MarkerPhase;
        motionX=value.MotionX;motionY=value.MotionY;leaveFlash=value.LeaveFlash;pendingPreOut=value.PendingPreOut;
        pendingPreEnter=value.PendingPreEnter;actionPreOut=value.ActionPreOut;
        value.EffectFrames.CopyTo(effectFrames,0);value.EffectCounters.CopyTo(effectCounters,0);
    }
}

