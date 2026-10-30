using System.Text;
using Atria.Game;
using Atria.Game.Navigation;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.World;

internal static class LocationNavigationTests
{
    public static string Run(string root)
    {
        var navigation = new SessionNavigation();
        Check(!navigation.TryClose(out _), "root menu cannot return to an absent game");
        foreach (var location in Enum.GetValues<LocationKind>())
        {
            navigation.RestoreLocation(location);
            foreach (var screen in Enum.GetValues<UiScreen>())
            {
                navigation.Open(UiScreen.MainMenu, 7);
                navigation.Open(screen, 9);
                Check(navigation.WorldMode == (int)location, "world identity independent of screen depth");
                Check(navigation.TryClose(out var parent) && parent.Mode == GameMode.MainMenu && parent.MusicTrack == 9, "nested return");
                Check(navigation.TryClose(out var world) && world.Mode == (int)location && world.MusicTrack == 7, "world return");
                Check(!navigation.TryClose(out _), "world cannot be popped");
            }
        }

        navigation.RestoreLocation(LocationKind.World);
        navigation.SetWorldReveal(true);
        navigation.Open(UiScreen.Status, 3);
        Check(navigation.TryClose(out var reveal) && reveal.Mode == GameMode.WorldEntry && navigation.WorldMode == GameMode.World,
            "reveal is world presentation, not another location");
        navigation.Reset();
        Check(navigation.Location is null && navigation.Mode == GameMode.MainMenu, "termination clears world navigation");

        VerifyRepeatArrivals(root);
        return "Location navigation: six locations, seven screen kinds, nested returns, map reveal, both departure controls and seven original repeat-arrival scenarios passed.";
    }

    private static void VerifyRepeatArrivals(string root)
    {
        foreach (var (name, id, choice) in new[] { ("18FORGOT", 18, 2), ("19REFUGE", 19, 1), ("25VALLEY", 25, 1),
            ("28DEVILS", 28, 2), ("33CASTLE", 33, 1), ("45ROOST", 45, 2), ("49GATE", 49, 1) })
        {
            var state = new GameState();
            state.SetQuestItemFlag(21, 1);
            state.SetQuestItemFlag(33, 1);
            state.Events[242] = state.Events[323] = state.Events[451] = state.Events[291] = 1;
            state.SetMemberStatus(6, 1);
            var effects = new ChoiceEffects(choice);
            byte[] member = Encoding.ASCII.GetBytes(name);
            var program = ScriptProgram.Parse(name, GameResources.Read(root, "SCRIPT.000", member));
            var host = new ScriptStateHost(state, effects);
            var assets = TownSceneAssets.Load(root, member);
            var town = new TownController();
            var commands = new List<TownCommand>();

            void Run(string function) => new ScriptMachine(program, host).Invoke(function);
            void Arrive()
            {
                commands.Clear();
                state.EnterPermission = 0;
                Run("pre_enter");
                Check(state.EnterPermission != 0, name + " admits fixture");
                town.Initialize(assets.Scene, assets.Objects, id, member, default, 1, commands.Add);
                Check(commands.All(c => c.Kind != TownEffectKind.QueueEnterScript), "initialization has no scenario event");
                town.QueueArrival(commands.Add);
                Check(commands.Count(c => c.Kind == TownEffectKind.QueueEnterScript) == 1, "one arrival event");
                effects.Questions = 0;
                Run("enter");
                if (id != 49)
                    Check(effects.Questions == 1, name + " offers unfinished interaction");
            }

            Arrive();
            foreach (bool actionExit in new[] { false, true })
            {
                town.Input(default, false, commands.Add);
                if (actionExit)
                    town.ChooseAction(0, commands.Add);
                else
                {
                    town.Input(new(Cancel: true), false, commands.Add);
                    town.Input(new(Accept: true), false, commands.Add);
                }
                state.EnterPermission = 0;
                Run("pre_out");
                town.CompletePreOut(state.EnterPermission != 0, commands.Add);
                Check(commands.Last().Kind == TownEffectKind.ChangeMode && commands.Last().Value == GameMode.World, "departure completed");
                int search = state.Globals[649];
                Arrive();
                if (id == 49)
                    Check(state.Globals[649] == search + 1, "each physical return advances the next gated search");
            }
        }
    }

    private sealed class ChoiceEffects(int choice) : IScriptEffects
    {
        public int Questions;
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind is ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey)
            {
                Questions++;
                state.Globals[10] = (short)choice;
            }
            else
                Check(effect.Kind is ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey, "unexpected scenario effect " + effect.Kind);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Navigation: " + message);
    }
}
