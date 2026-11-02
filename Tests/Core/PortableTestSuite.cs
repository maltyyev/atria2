namespace Tests.Core;

/// <summary>Runs asset-backed tests that do not depend on a native host or system font rasterizer.</summary>
internal static class PortableTestSuite
{
    /// <summary>Runs portable resources, persistence, input, audio, scripting and gameplay checks.</summary>
    internal static void Run(string resourceRoot, string musicRoot)
    {
        Console.WriteLine(Tests.Core.Resources.StateModelTests.Run());
        Console.WriteLine(Tests.Core.Resources.GameDefinitionTests.Run());
        Console.WriteLine(Tests.Core.Resources.ResourceContractTests.Run(resourceRoot, musicRoot));
        foreach (string result in Tests.Core.Script.ScriptTests.Run(resourceRoot))
        {
            Console.WriteLine(result);
        }

        Console.WriteLine(Tests.Core.State.StateTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Persistence.PersistenceTests.Run());
        Console.WriteLine(Tests.Core.Persistence.AutosaveTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Persistence.SaveRecoveryTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Audio.MusicTests.Run());
        Console.WriteLine(Tests.Core.Audio.SoundEffectTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Audio.CombatSoundTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Audio.PcmChunkTests.Run());
        Console.WriteLine(Tests.Core.Platform.ClockTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Settings.SettingsTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.World.WorldTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Town.TownTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.ActionMenu.ActionMenuTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Exploration.ExplorationTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Exploration.RuinsAccessTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Progression.ProgressionTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Interiors.InteriorTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Interiors.GeographerTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Interiors.GathaGuildTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Status.Feedback035Tests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.PartyAirCombatTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.PartySpellTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatFrameSoundTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatBalanceTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.State.HeroRosterTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatSlowDurationTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatEntryTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatSpecialRasterTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatSchedulingTests.Run());
        Console.WriteLine(Tests.Core.Combat.CombatGlobalUiTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatRandomEncounterTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatDebugInputTests.Run());
        Console.WriteLine(Tests.Core.Combat.CombatSessionBridgeTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatAttributeDomainTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatInfoModelTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatExecutionHostTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatEncounterDiagnostic.Run(resourceRoot, false));
        Console.WriteLine(Tests.Core.Combat.CombatTransitionTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatOutcomeTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.ProjectileReactionTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Combat.CombatParticleTests.Run(resourceRoot));
        Console.WriteLine(Tests.Core.Dungeon.DungeonTests.Run(resourceRoot));
    }
}
