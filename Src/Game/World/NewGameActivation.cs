using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.World;

public sealed record NewGameActivation(GameState State, WorldMapContext Map, WorldAreaStore Areas, ModeStack Modes, WorldSceneReference FirstScene)
{
    /// <summary>425990 state/storage portion. Caller must execute mode400 entry and all resource/script effects.</summary>
    public static NewGameActivation Prepare(ItemCatalog items, IReadOnlyList<ReadOnlyMemory<byte>> messages, ReadOnlyMemory<byte> ground1)
    {
        // Build detached candidates; failure cannot replace an existing running session.
        var map = WorldMapContext.Parse(ground1);
        var state = InitialGameState.Create(items, messages);
        var areas = new WorldAreaStore();
        areas.ResetNames(messages);
        var first = map.SelectScene(state.WorldX, state.WorldY);
        areas.Capture(0, map);
        var modes = new ModeStack();
        modes.StartNewGame();
        state.Mode = 400;
        state.ModeStackDepth = 0;
        return new(state, map, areas, modes, first);
    }
}

public enum ModeEntryOperation
{
    LoadFixedBackground,
    LoadWorldBackground,
    LoadTownBackground,
    ResetMenuFlag,
    ClearWorldSelectionFlag,
    InitializeSaveMenu,
    InitializeLoadMenu,
    InitializeCredits,
    InitializeOptions,
    LoadTownScene,
    InitializeTownObject410,
    InitializeTownObject420,
    InitializeTownObject430,
    InitializeWorldMapMenu,
    InitializeEnding,
    UpdateMusic
}

public readonly record struct ModeEntryStep(ModeEntryOperation Operation, string? Resource = null);
/// <summary>Ordered obligations of428f00. Executing these operations belongs to the session/platform owners.</summary>
public static class NativeModeEntry
{
    public static IReadOnlyList<ModeEntryStep> Steps(int mode)
    {
        var steps = new List<ModeEntryStep>();
        void Add(ModeEntryOperation op, string? name = null) => steps.Add(new(op, name));
        switch (mode)
        {
            case 200:
                Add(ModeEntryOperation.LoadFixedBackground, "menubg.bmp");
                Add(ModeEntryOperation.ResetMenuFlag);
                break;
            case 210:
                Add(ModeEntryOperation.LoadFixedBackground, "newgame.bmp");
                break;
            case 220:
                Add(ModeEntryOperation.LoadFixedBackground, "saveload.bmp");
                Add(ModeEntryOperation.InitializeSaveMenu);
                break;
            case 230:
                Add(ModeEntryOperation.LoadFixedBackground, "saveload.bmp");
                Add(ModeEntryOperation.InitializeLoadMenu);
                break;
            case 240:
                Add(ModeEntryOperation.LoadFixedBackground, "creator.bmp");
                Add(ModeEntryOperation.InitializeCredits);
                break;
            case 250:
                Add(ModeEntryOperation.LoadFixedBackground, "option.bmp");
                Add(ModeEntryOperation.InitializeOptions);
                break;
            case 300:
            case 330:
                Add(ModeEntryOperation.ClearWorldSelectionFlag);
                Add(ModeEntryOperation.LoadWorldBackground);
                break;
            case 400:
                Add(ModeEntryOperation.LoadTownScene);
                Add(ModeEntryOperation.LoadTownBackground);
                break;
            case 410:
                Add(ModeEntryOperation.InitializeTownObject410);
                Add(ModeEntryOperation.LoadTownBackground);
                break;
            case 420:
                Add(ModeEntryOperation.InitializeTownObject420);
                Add(ModeEntryOperation.LoadTownBackground);
                break;
            case 430:
                Add(ModeEntryOperation.InitializeTownObject430);
                Add(ModeEntryOperation.LoadTownBackground);
                break;
            case 500:
                Add(ModeEntryOperation.LoadFixedBackground, "map.bmp");
                Add(ModeEntryOperation.InitializeWorldMapMenu);
                break;
            case 1000:
                Add(ModeEntryOperation.InitializeEnding);
                break;
            case 1100:
                Add(ModeEntryOperation.LoadFixedBackground, "gameover.bmp");
                break;
                // Original default has no entry work beyond its music controller call.
        }

        Add(ModeEntryOperation.UpdateMusic);
        return steps.AsReadOnly();
    }

    /// <summary>Only direct music assignments in428f00; town loader400 has its own independent table.</summary>
    public static int MusicId(int mode, int previousMusic, int areaIndex, Func<int>? nativeRandom = null)
    {
        if (mode == 300)
        {
            if ((uint)areaIndex >= 8)
            {
                throw new InvalidDataException("World music area outside recovered eight areas.");
            }

            return areaIndex + 1;
        }

        if (mode == 330)
        {
            if (nativeRandom is null)
            {
                throw new ArgumentNullException(nameof(nativeRandom));
            }

            int value = nativeRandom();
            if ((uint)value > 32767)
            {
                throw new InvalidDataException("Expected original15-bit rand result.");
            }

            return (value & 1) + 19;
        }

        return mode switch
        {
            240 => 26,
            1000 or 1100 => 27,
            _ => previousMusic
        };
    }
}
