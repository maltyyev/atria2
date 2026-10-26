using Atria.Render;

if (args.Length == 2 && args[1] == "--party-air")
{
    Console.WriteLine(Atria.Tests.Combat.PartyAirCombatTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--party-spells")
{
    Console.WriteLine(Atria.Tests.Combat.PartySpellTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--travel-cost")
{
    Console.WriteLine(Atria.Tests.Exploration.ExplorationTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Status.Feedback035Tests.Run(args[0]));
    Console.WriteLine(Atria.Tests.State.QuestDayCounterTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--experience-layout")
{
    Console.WriteLine(Atria.Tests.Progression.ProgressionTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Progression.ExperienceLayoutTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--language-session")
{
    Console.WriteLine(Atria.Game.GameSession.VerifyLanguageIntegration(args[0], args[1], args[2]));
    return;
}

if (args.Length == 2 && args[1] == "--live-language")
{
    Console.WriteLine(Atria.Tests.Localization.LiveLanguageTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--belarusian-layout")
{
    Console.WriteLine(Atria.Tests.Render.LayoutFeedbackTests.Run(args[0], "be-BY"));
    return;
}

if (args.Length == 2 && args[1] == "--script-arguments")
{
    Console.WriteLine(Atria.Tests.Script.ScriptArgumentAuditTests.Run(args[0]));
    return;
}

if (args.Length is 2 or 3 && args[1] == "--quest-timer")
{
    Console.WriteLine(Atria.Tests.State.QuestDayCounterTests.Run(args[0], args.Length == 3 ? args[2] : null));
    Console.WriteLine(Atria.Tests.State.StateTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--layout-feedback")
{
    Console.WriteLine(Atria.Tests.Render.LayoutFeedbackTests.Run(args[0]));
    Atria.Tests.Interiors.ShopRendererTests.Run(args[0]);
    Atria.Tests.Status.StatusRendererTests.Run(args[0]);
    Console.WriteLine(Atria.Tests.Persistence.PersistenceRuntimeTests.RunMenuRaster(args[0]));
    Console.WriteLine(Atria.Tests.ActionMenu.ActionMenuTests.Run(args[0]));
    Atria.Tests.Exploration.WorldRenderTests.Run(args[0]);
    return;
}


if (args.Length is 2 or 3 && args[1] == "--shop-stock")
{
    Console.WriteLine(Atria.Tests.Interiors.ShopStockTests.Run(args[0], args.Length == 3 ? args[2] : null));
    Console.WriteLine(Atria.Tests.Interiors.InteriorTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--vertical-menu")
{
    Console.WriteLine(Atria.Tests.Menu.MainMenuTests.Run());
    Console.WriteLine(Atria.Tests.Menu.VerticalMenuTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Render.UnicodeTextTests.Run(args[0]));
    return;
}


if (args.Length == 2 && args[1] == "--journal")
{
    Console.WriteLine(Atria.Tests.Status.QuestJournalTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Status.StatusMenuTests.Run());
    Atria.Tests.Status.StatusRendererTests.Run(args[0]);
    Console.WriteLine(Atria.Tests.Localization.LocalizationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--unicode")
{
    Console.WriteLine(Atria.Tests.Render.UnicodeTextTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Localization.LocalizationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--localization")
{
    Console.WriteLine(Atria.Tests.Localization.LocalizationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--refactor")
{
    Console.WriteLine(Atria.Tests.Resources.StateModelTests.Run());
    Console.WriteLine(Atria.Tests.Resources.GameDefinitionTests.Run());
    Console.WriteLine(Atria.Tests.RefactoringTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Combat.CombatSlowDurationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--slow-duration")
{
    Console.WriteLine(Atria.Tests.Combat.CombatSlowDurationTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Combat.CombatTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Combat.CombatParticleTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Combat.CombatSchedulingTests.Run());
    return;
}

if (args.Length == 3 && args[2] == "--feedback-035")
{
    Console.WriteLine(Atria.Tests.Status.Feedback035Tests.Run(args[0], args[1]));
    Console.WriteLine(Atria.Tests.Status.StatusMenuTests.Run());
    Atria.Tests.Status.StatusRendererTests.Run(args[0]);
    Atria.Tests.Interiors.ShopRendererTests.Run(args[0]);
    Console.WriteLine(Atria.Tests.Combat.CombatOutcomeTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Dungeon.DungeonTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Exploration.ExplorationTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Persistence.PersistenceRuntimeTests.Run(args[0]));
    return;
}

if (args.Length == 3 && args[2] == "--ruins")
{
    Console.WriteLine(Atria.Tests.Exploration.RuinsAccessTests.Run(args[0], args[1]));
    Atria.Tests.Exploration.WorldRenderTests.Run(args[0]);
    Console.WriteLine(Atria.Tests.Exploration.ExplorationTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--gatha-guild-session")
{
    Console.WriteLine(Atria.Game.GameSession.VerifyGeographerIntegration(args[0], args[1], args[2], true));
    return;
}

if (args.Length == 2 && args[1] == "--gatha-guild")
{
    Console.WriteLine(Atria.Tests.Interiors.GathaGuildTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Interiors.InteriorTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.ActionMenu.ActionMenuTests.Run(args[0]));
    foreach (string line in Atria.Tests.Script.ScriptTests.Run(args[0]))
    {
        Console.WriteLine(line);
    }

    return;
}

if (args.Length == 4 && args[3] == "--geographer-session")
{
    Console.WriteLine(Atria.Game.GameSession.VerifyGeographerIntegration(args[0], args[1], args[2]));
    return;
}

if (args.Length == 2 && args[1] == "--geographer")
{
    Console.WriteLine(Atria.Tests.Interiors.GeographerTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Interiors.InteriorTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--autosave-session")
{
    Console.WriteLine(Atria.Game.GameSession.VerifyPersistenceIntegration(args[0], args[1], args[2]));
    return;
}

if (args.Length == 2 && args[1] == "--autosave")
{
    Console.WriteLine(Atria.Tests.Persistence.AutosaveTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Persistence.PersistenceRuntimeTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--combat-audio")
{
    Console.WriteLine(Atria.Tests.Combat.CombatEntryTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Audio.CombatSoundTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--dungeon-switch")
{
    Console.WriteLine(Atria.Tests.Dungeon.DungeonTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Dungeon.DungeonSwitchRasterTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--controls")
{
    Console.WriteLine(Atria.Tests.Controls.ControlRedesignTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--reward-policy")
{
    Console.WriteLine(Atria.Tests.Combat.CombatOutcomeTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Combat.CombatSessionBridgeTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Progression.ProgressionTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--feedback-011")
{
    Console.WriteLine(Atria.Tests.Menu.Feedback011Tests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Menu.MainMenuTests.Run());
    Console.WriteLine(Atria.Tests.Menu.VerticalMenuTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.ActionMenu.ActionMenuTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Town.TownTests.Run(args[0]));
    Console.WriteLine(Atria.Tests.Combat.CombatOutcomeTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--save-menu-raster")
{
    Console.WriteLine(Atria.Tests.Persistence.PersistenceRuntimeTests.RunMenuRaster(args[0]));
    return;
}

if (args.Length == 3 && args[2] == "--battle-diagnostic")
{
    Atria.Tests.Combat.CombatEncounterDiagnostic.Run(args[0]);
    return;
}

if (args.Length != 2)
{
    throw new ArgumentException("Expected original-resource and music roots.");
}

Console.WriteLine($"Verification runtime: dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}.");
Viewport v = Viewport.Fit(2560, 1440);
if (v != new Viewport(320, 0, 1920, 1440))
{
    throw new Exception("1440p aspect contract failed.");
}

if (v.TryMapInput(319, 0, out _, out _))
{
    throw new Exception("Black bar incorrectly maps into world.");
}

if (!v.TryMapInput(2239, 1439, out int x, out int y) || x != 639 || y != 479)
{
    throw new Exception("Input edge mapping failed.");
}

Console.WriteLine("Presentation geometry checks passed.");
Console.WriteLine(Atria.Tests.Localization.LocalizationTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Localization.LiveLanguageTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Resources.StateModelTests.Run());
Console.WriteLine(Atria.Tests.Resources.GameDefinitionTests.Run());
Console.WriteLine(Atria.Tests.RefactoringTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Controls.ControlRedesignTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Dungeon.DungeonSwitchRasterTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Resources.ResourceContractTests.Run(args[0], args[1]));
Atria.Tests.Render.RenderTests.Run(args[0]);
foreach (string line in Atria.Tests.Script.ScriptTests.Run(args[0]))
{
    Console.WriteLine(line);
}

Console.WriteLine(Atria.Tests.State.StateTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Menu.MainMenuTests.Run());
Console.WriteLine(Atria.Tests.Menu.VerticalMenuTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Menu.Feedback011Tests.Run(args[0]));
Console.WriteLine(Atria.Tests.Persistence.PersistenceTests.Run());
Console.WriteLine(Atria.Tests.Persistence.PersistenceRuntimeTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Persistence.AutosaveTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Audio.MusicTests.Run());
Console.WriteLine(Atria.Tests.Audio.MusicStreamTests.Run(args[1]));
Console.WriteLine(Atria.Tests.Dialogue.DialogueTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Menu.MenuRasterTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Platform.ClockTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Audio.SoundEffectTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Audio.CombatSoundTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Settings.SettingsTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Credits.CreditsTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Termination.TerminationTests.Run(args[0]));
Console.WriteLine(Atria.Tests.World.WorldTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Town.TownTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Audio.PcmChunkTests.Run());
Console.WriteLine(Atria.Tests.Video.VideoTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Audio.PcmChunkTests.RunMutedProbe());
Console.WriteLine(Atria.Tests.ActionMenu.ActionMenuTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Exploration.ExplorationTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Exploration.RuinsAccessTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Progression.ProgressionTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Progression.ExperienceLayoutTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Interiors.InteriorTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Interiors.GeographerTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Interiors.GathaGuildTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Status.StatusMenuTests.Run());
Console.WriteLine(Atria.Tests.Status.QuestJournalTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Status.Feedback035Tests.Run(args[0]));
Atria.Tests.Status.StatusRendererTests.Run(args[0]);
Console.WriteLine(Atria.Tests.Combat.PartyAirCombatTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.PartySpellTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatSlowDurationTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatEntryTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatSpecialRasterTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatSchedulingTests.Run());
Console.WriteLine(Atria.Tests.Combat.CombatGlobalUiTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatRandomEncounterTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatDebugInputTests.Run());
Console.WriteLine(Atria.Tests.Combat.CombatSessionBridgeTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatAttributeDomainTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatInfoModelTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatExecutionHostTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatEncounterDiagnostic.Run(args[0], false));
Console.WriteLine(Atria.Tests.Combat.CombatTransportTests.Run());
Console.WriteLine(Atria.Tests.Combat.CombatTransitionTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatOutcomeTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Combat.CombatParticleTests.Run(args[0]));
Console.WriteLine(Atria.Tests.Dungeon.DungeonTests.Run(args[0]));
