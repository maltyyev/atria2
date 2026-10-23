using System.Text.Json;
using Atria.Audio;
using Atria.Game.Menu;
using Atria.Game.Credits;
using Atria.Game.Termination;
using System.Text.Json.Serialization;
using Atria.Game.Dungeon;
using Atria.Game.Dialogue;
using Atria.Game.Exploration;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Script;
using Atria.Game.Interiors;
namespace Atria.Game.Persistence;

public sealed record SessionMapSnapshot(byte[] Member,byte[] Header,byte[] Tiles);
public sealed record SessionLocationSnapshot(int SceneIndex,byte[] SceneMember,byte[] ScriptMember,int AreaIndex);
public sealed record SessionExplorationSnapshot(SessionMapSnapshot Map,byte[] PrimaryName,byte[] SecondaryName,byte[] ScriptMember,
    ExplorationSnapshot? Controller,WorldRendererSnapshot? Renderer,bool MapReload,int TravelSteps,int RevealX,int RevealY);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record SessionDungeonSnapshot(SessionMapSnapshot Map,int Source,byte[] ScriptMember,int ReturnX,int ReturnY,int? CaptureArea,
    DungeonControllerSnapshot Controller,DungeonRasterSnapshot Raster,byte[] DynamicText,byte[] ReturnMap,byte[] ReturnTown,
    byte[] ReturnTownScript,int Padding,int HudY,int TreasureKind,int TreasureAmount,int TreasureItem,bool HudEnabled);
public sealed record SessionRuntimeSnapshot([property:JsonRequired] int SavedMode,[property:JsonRequired] ModeStackSnapshot Modes,[property:JsonRequired] LegacyRandomSnapshot Random,
    [property:JsonRequired] SessionMapSnapshot CurrentMap,[property:JsonRequired] int CurrentMapKind,[property:JsonRequired] DialogueRuntimeSnapshot Dialogue,[property:JsonRequired] MusicControllerSnapshot Music,[property:JsonRequired] int PlayingMusicTrack,[property:JsonRequired] SessionLocationSnapshot? Location,[property:JsonRequired] TownSnapshot Town,[property:JsonRequired] SessionExplorationSnapshot? Exploration,
    [property:JsonRequired] SessionDungeonSnapshot? Dungeon,[property:JsonRequired] byte[] ScriptMember,[property:JsonRequired] byte[] PendingHandler,[property:JsonRequired] byte[] TownName,[property:JsonRequired] byte[] ReturnMap,[property:JsonRequired] byte[] TownFile,
    [property:JsonRequired] int PreviousTown,[property:JsonRequired] int MusicId,[property:JsonRequired] int LabelDelay,[property:JsonRequired] int ObjectActionField,[property:JsonRequired] int EntryTextMode,[property:JsonRequired] int SelectedEquipmentActor,
    [property:JsonRequired] int AwardMusicTrack,[property:JsonRequired] bool SaveDisabled,[property:JsonRequired] bool StatusAlternateJournalHeading,[property:JsonRequired] byte[] Settings,[property:JsonRequired] int FrameThreshold,[property:JsonRequired] byte[] EndingTextPrefix,[property:JsonRequired] MainMenuRetainedSnapshot MainMenu,[property:JsonRequired] CreditsRetainedSnapshot Credits,[property:JsonRequired] DefeatRetainedSnapshot Defeat,[property:JsonRequired] int RetainedBattleCounter,[property:JsonRequired] int RetainedBattleVisualDetail,[property:JsonRequired] int RetainedBattleRenderPeriod)
{
    public bool ExactResume {get;init;}
    public string SaveReason {get;init;}="";
    public ScriptCheckpoint? Script {get;init;}
    public InteriorSnapshot? Interior {get;init;}
    public SessionLocationSnapshot? ScriptReturnLocation {get;init;}
    public uint? NextBattleSeed {get;init;}
}

/// <summary>Own native context schema. Derived raster caches and immutable resources are rebuilt, never serialized.</summary>
public static class SessionRuntimeCodec
{
    public const string Schema="atria.session";
    public const int Version=1;
    public static NativeRuntimeContext Encode(SessionRuntimeSnapshot value)
    {
        Validate(value);byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value,SessionRuntimeJson.Default.SessionRuntimeSnapshot);
        return new(Schema,Version,bytes);
    }
    public static SessionRuntimeSnapshot Decode(NativeRuntimeContext context)
    {
        if(context.Schema!=Schema || context.Version!=Version)throw new InvalidDataException("Session runtime schema/version is unsupported.");
        try
        {
            var result=JsonSerializer.Deserialize(context.Payload.Span,SessionRuntimeJson.Default.SessionRuntimeSnapshot)
                ??throw new InvalidDataException("Session runtime context is absent.");
            // Source-generated record construction supplies null for an absent init property.
            if(result.SaveReason is null)
            {using var json=JsonDocument.Parse(context.Payload);if(!json.RootElement.TryGetProperty("SaveReason",out _))result=result with {SaveReason=""};}
            Validate(result);return result;
        }
        catch(JsonException error){throw new InvalidDataException("Malformed bounded session runtime context.",error);}
    }
    private static void Bytes(byte[]? bytes,int max=4096)
    {if(bytes is null || bytes.Length>max)throw new InvalidDataException("Session byte bank is absent or exceeds its bound.");}
    private static void Map(SessionMapSnapshot? value)
    {
        if(value is null || value.Header is not {Length:313} || value.Tiles is not {Length:32800})throw new InvalidDataException("Session map banks have invalid size.");
        Bytes(value.Member,260);if(value.Member.Length==0 || value.Member.Contains((byte)0))throw new InvalidDataException("Session map identity is invalid.");
    }
    public static void Validate(SessionRuntimeSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.SavedMode is not (300 or 400 or 330 or 410 or 420 or 430))throw new NotSupportedException("Session snapshot activation for this underlying mode is not recovered.");
        if(value.SaveReason is null||value.SaveReason.Length>100)throw new InvalidDataException("Invalid save reason.");
        if(value.SavedMode is 410 or 420 or 430 && (value.Interior is null||value.Interior.Mode!=value.SavedMode||!value.ExactResume||value.Location is null))throw new InvalidDataException("Interior resume context absent or inconsistent.");
        if(value.Script is {} script)
        {
            if(!value.ExactResume||string.IsNullOrEmpty(script.Member)||script.Member.Length>260||string.IsNullOrEmpty(script.Function)||script.Function.Length>128||script.SourceHash is not {Length:64}||script.Continuation is <0 or >5||script.Calls is null||script.Calls.Length>100000)throw new InvalidDataException("Invalid script continuation.");
            foreach(var call in script.Calls)
            {if(call is null||call.Function is null||call.Function.Length>128||call.Offset<0||call.Arguments is null||call.Arguments.Length>60)throw new InvalidDataException("Invalid scenario call record.");foreach(var arg in call.Arguments){if(arg is null)throw new InvalidDataException("Missing scenario argument.");Bytes(arg.Bytes,4096);}}
        }
        if(value.FrameThreshold is <15 or >45)throw new InvalidDataException("Session frame threshold is outside recovered speed controls.");
        if((uint)value.RetainedBattleVisualDetail>3 || value.RetainedBattleRenderPeriod<25 || value.RetainedBattleRenderPeriod>225 || (value.RetainedBattleRenderPeriod-25)%20!=0)throw new InvalidDataException("Retained battle detail or render period is outside native controls.");
        if((uint)value.RetainedBattleCounter>1)throw new InvalidDataException("Retained battle counter is outside its native0/1 state.");
        if((uint)value.CurrentMapKind>2)throw new InvalidDataException("Current map alias kind is invalid.");
        Map(value.CurrentMap);if(value.Modes is null || value.Modes.Slots is not {Length:ModeStack.SafeCapacity} || value.Modes.Depth is < -1 or >=ModeStack.SafeCapacity)
            throw new InvalidDataException("Session mode bank is invalid.");
        if((uint)value.SelectedEquipmentActor>=Atria.Game.State.GameState.PartyCount || value.Town is null)throw new InvalidDataException("Session actor/town state is invalid.");
        new TownController().RestoreRuntime(value.Town,null,null);
        DialogueRuntime.ValidateRuntime(value.Dialogue);
        MusicController.ValidateRuntime(value.Music);
        MainMenuController.ValidateRetained(value.MainMenu);CreditsController.ValidateRetained(value.Credits);DefeatController.ValidateRetained(value.Defeat);
        if((uint)value.PlayingMusicTrack>27 || value.MusicId>27)throw new InvalidDataException("Session music identity is invalid.");
        Bytes(value.ScriptMember);Bytes(value.PendingHandler);Bytes(value.TownName);Bytes(value.ReturnMap);Bytes(value.TownFile);Bytes(value.Settings,4);Bytes(value.EndingTextPrefix,12);
        if(value.EndingTextPrefix.Length!=12)throw new InvalidDataException("Ending text alias prefix must contain12 bytes.");
        if(value.Settings.Length!=4)throw new InvalidDataException("Session settings snapshot is required.");
        _=Atria.Game.Settings.NativeSettingsState.Parse(value.Settings);
        if(value.Location is {} location){Bytes(location.SceneMember);Bytes(location.ScriptMember);if((uint)location.AreaIndex>=8 || (uint)location.SceneIndex>=52)throw new InvalidDataException("Session location is invalid.");}
        if(value.ScriptReturnLocation is {} prior){Bytes(prior.SceneMember);Bytes(prior.ScriptMember);if((uint)prior.AreaIndex>=8||(uint)prior.SceneIndex>=52)throw new InvalidDataException("Scenario return location is invalid.");}
        if(value.Exploration is {} world)
        {
            Map(world.Map);Bytes(world.PrimaryName);Bytes(world.SecondaryName);Bytes(world.ScriptMember);
            if(world.Controller is not null && world.Renderer is null)throw new InvalidDataException("World controller requires its retained renderer bank.");
        }
        if(value.Dungeon is {} dungeon)
        {
            if((uint)dungeon.ReturnX>=10 || (uint)dungeon.ReturnY>=10 || (dungeon.CaptureArea is {} area && (uint)area>=8))
                throw new InvalidDataException("Retained dungeon return coordinate/area is invalid.");
            Map(dungeon.Map);Bytes(dungeon.ScriptMember);Bytes(dungeon.DynamicText);Bytes(dungeon.ReturnMap);Bytes(dungeon.ReturnTown);Bytes(dungeon.ReturnTownScript);
            if((uint)dungeon.Source>2 || dungeon.Controller is null)throw new InvalidDataException("Retained dungeon context is invalid.");
        }
        if(value.SavedMode==400 && value.Location is null)throw new InvalidDataException("Town activation requires its scene identity.");
        if(value.SavedMode==300 && value.Exploration is null)throw new InvalidDataException("World activation requires its entry identity.");
        if(value.SavedMode==330 && value.Dungeon is null)throw new InvalidDataException("Dungeon activation requires its entry identity.");
    }
}
[JsonSourceGenerationOptions(GenerationMode=JsonSourceGenerationMode.Metadata,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SessionRuntimeSnapshot))]
internal partial class SessionRuntimeJson:JsonSerializerContext { }
