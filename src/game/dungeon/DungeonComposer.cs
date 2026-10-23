using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Dungeon;

/// <summary>420bc0 single logical compose pass. GPU presentations reuse its output without calling it again.</summary>
public sealed class DungeonComposer
{
    private readonly GameState state;
    private readonly DungeonController controller;
    private readonly DungeonMap map;
    private readonly DungeonSceneAssets assets;
    private readonly DungeonModalRenderer modals;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages,itemNames;
    private byte[] dynamicText;
    public DungeonSceneRenderer Raster {get;}
    public ReadOnlyMemory<byte> DynamicText=>dynamicText;
    /// <summary>428ea0/41eae0 reloads the ground without replacing retained motion, visibility or raster clocks.</summary>
    public void ReloadBackgroundAfterBattle(ReadOnlySpan<ushort> pixels)
    {
        if(pixels.Length!=640*480)throw new ArgumentException("Dungeon background requires640x480 pixels.",nameof(pixels));
        pixels.CopyTo(assets.Base.Ground565);
        Raster.RebuildObjects(map,controller.Visibility,state.DungeonFog!=0);
    }
    public DungeonComposer(GameState state,DungeonController controller,DungeonMap map,DungeonSceneAssets assets,
        BitmapFont font,IReadOnlyList<ReadOnlyMemory<byte>> messages,IReadOnlyList<ReadOnlyMemory<byte>> itemNames,ReadOnlyMemory<byte> retainedDynamicText=default)
    {
        this.state=state;this.controller=controller;this.map=map;this.assets=assets;this.messages=messages;this.itemNames=itemNames;
        Raster=new(assets.Base,assets.ObjectMarkers);modals=new(assets.Base.Etc,font,messages);dynamicText=retainedDynamicText.ToArray();
    }
    public void ApplyRasterCommand(DungeonCommand command)
    {
        if(command.Kind==DungeonCommandKind.RebuildObjects) Raster.RebuildObjects(map,controller.Visibility,state.DungeonFog!=0);
        else if(command.Kind==DungeonCommandKind.RefreshVisibility) Raster.RefreshVisibility(map,controller.Visibility,state.DungeonFog!=0);
        else throw new ArgumentException("Command is not a dungeon cached raster update.");
    }
    public IReadOnlyList<DungeonCommand> Compose(Span<ushort> target,Func<uint> scheduledClock,
        Func<int,int> readNativeVisibility,int hudY,bool hudEnabled,Action<DungeonCommand>? observeCommand=null)
    {
        Raster.CopyStaticBackdrop(target);Raster.DrawMarkers(map,controller.Visibility,state.DungeonFog!=0,target);
        if(state.DungeonFogDirty==0)
        {
            Raster.DrawTorches(controller.Visibility,assets.TorchAnimation,assets.TorchSprites,hudY,hudEnabled,target);
            if(state.DungeonFog==1) Raster.DrawDarkness(state.WorldX,state.WorldY,readNativeVisibility,target);
        }
        if(state.DungeonFogDirty!=0) controller.Motion.ApplyVisibilityRefreshFrameIncrement();
        state.DungeonFogDirty=0;
        var step=controller.Advance(scheduledClock);Raster.DrawCharacter(step.Motion.Pose,target);
        foreach(var command in step.Commands)
            if(command.Arrival is {} effect && effect.Kind is DungeonArrivalEffectKind.TreasureMoney or DungeonArrivalEffectKind.TreasureItem or DungeonArrivalEffectKind.TreasureTorch)
                dynamicText=DungeonModalRenderer.BuildDynamicText(effect,messages,itemNames);
        if(observeCommand is not null) foreach(var command in step.Commands) observeCommand(command);
        if(!controller.ExitPending&&state.DungeonFog==1&&state.DungeonFogDirty==0)
            Raster.DrawDarkness(state.WorldX,state.WorldY,readNativeVisibility,target);
        modals.Draw(controller,state.ResourceCounterFc9a,dynamicText,target);
        return step.Commands;
    }
}
