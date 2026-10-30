using Atria.Game.Navigation;
using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    //458be4 is BSS initially0, survives battles/new-game reset; modern persistence must retain it.
    private int retainedBattleCounter;
    //45a8dc BSS0;45ae60=25 at413750 application initialization. Neither is reset by413930 entry.
    private int retainedBattleVisualDetail = 0;
    private int retainedBattleRenderPeriod = 25;
    /// <summary>Call only after real battle outcome/XP cleanup and ordinary audio/resource restoration.</summary>
    private void CompleteBattleReturn(int actualReturnCode)
    {
        if (actualReturnCode is not (1 or 2))
        {
            throw new InvalidDataException("Battle restoration requires a real terminal outcome.");
        }

        if (state.EndingResetFlag == 0)
        {
            throw new InvalidOperationException("Pending battle gate was cleared before restoration completed.");
        }
        //403ec0 runs after restoration for BOTH terminal outcomes, independent of award eligibility.
        CombatExperienceAward.FinalizeReturnedStats(state, items);
        CombatOutcomeReturn.Apply(state, actualReturnCode, ref retainedBattleCounter, ApplyCombatReturnEffect);
    }

    private void ApplyCombatReturnEffect(CombatOutcomeReturnEffect effect)
    {
        switch (effect.Kind)
        {
            case CombatOutcomeReturnEffectKind.ReloadWorldBackground:
                {
                    var current = map ?? throw new InvalidOperationException("Battle return lost the current AMF identity.");
                    ushort[] pixels = ReadCombatReturnBackground(current.BackgroundReference.Span);
                    if (state.Mode == GameMode.World)
                    {
                        var assets = explorationAssets ?? throw new InvalidOperationException("World battle return lost ground assets.");
                        var controller = exploration ?? throw new InvalidOperationException("World battle return lost movement state.");
                        pixels.CopyTo(assets.Ground565, 0);
                        controller.InputDelay = 2;
                        ExplorationRules.MarkArrival(current, state.WorldX, state.WorldY);
                        (explorationRenderer ?? throw new InvalidOperationException("World battle return lost renderer.")).RebuildBase(current, state);
                    }
                    else if (state.Mode == GameMode.Dungeon)
                    {
                        (dungeonComposer ?? throw new InvalidOperationException("Dungeon battle return lost renderer.")).ReloadBackgroundAfterBattle(pixels);
                    }
                    else
                    {
                        throw new InvalidDataException("World background return routed outside300/330.");
                    }

                    break;
                }

            case CombatOutcomeReturnEffectKind.ReloadTownBackground:
                {
                    var assets = state.Mode == GameMode.Town ? townAssets : state.Mode is GameMode.Interior or GameMode.Inn ? interiorAssets : null;
                    if (assets is null)
                    {
                        throw new InvalidOperationException("Battle return lost the active town/interior background identity.");
                    }

                    ReadCombatReturnBackground(assets.Scene.BackgroundReference.Span).CopyTo(assets.Background565, 0);
                    labelDelay = 2; //428ea0 writes458be8 for modes other than300/330.
                    break;
                }

            case CombatOutcomeReturnEffectKind.ResetMovement46027c:
                exploration?.ClearMovementAfterBattle();
                dungeon?.Motion.ClearMovementAfterBattle();
                break;
            case CombatOutcomeReturnEffectKind.ResetStackAndPushGameOver:
                ShowTermination(GameMode.Defeat);
                break;
            default:
                throw new InvalidDataException("Unknown battle return effect.");
        }
    }

    private ushort[] ReadCombatReturnBackground(ReadOnlySpan<byte> reference)
    {
        var image = BmpImage.Decode(Read("AT2BMP.000", reference));
        if (image.Width != 640 || image.Height != 480)
        {
            throw new InvalidDataException("Battle return background must be640x480.");
        }

        ushort[] pixels = image.ToRgb565();
        return pixels;
    }
}
