using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation;
using Atria.Platforms.Windows.Win32;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation.UI;

UnicodeText.UseRasterizer(new DirectWriteRasterizer());
Tests.Windows.WindowsSessionProbeHost.VerifyLifetimeContract();

if (args.Length == 4 && args[3] == "--expedition-session")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyExpedition(args[0], args[1], args[2], Tests.Windows.Render.UnicodeTextTests.SaveBitmap, Tests.Windows.WindowsSessionProbeHost.Create));
    return;
}

if (args.Length == 2 && args[1] == "--eiren-expedition")
{
    Console.WriteLine(Tests.Core.Combat.EirenExpeditionTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.NecromancerTests.Run(args[0]));
    return;
}


if (args.Length == 3 && args[1] == "--script-only-admission")
{
    Console.WriteLine(Tests.Core.Persistence.ScriptOnlyAdmissionTests.Run(args[0], args[2]));
    return;
}

if (args.Length == 3 && args[1] == "--location-ui")
{
    Console.WriteLine(Tests.Core.World.LocationNavigationTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Persistence.SessionMigrationTests.Run(args[0], args[2]));
    Tests.Core.Persistence.RuntimeSnapshotTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Town.TownTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Menu.Feedback011Tests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.NarrativeReturnTests.Run(args[0]));
    return;
}

if (args.Length is 2 or 3 && args[1] == "--narrative-return")
{
    Console.WriteLine(Tests.Core.Combat.NarrativeReturnTests.Run(args[0], args.Length == 3 ? args[2] : null));
    return;
}

if (args.Length == 4 && args[3] == "--narrative-arrival")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyNarrativeArrival(args[0], args[1], args[2], Tests.Windows.WindowsSessionProbeHost.Create));
    return;
}

if (args.Length is 2 or 3 && args[1] == "--narrative-revision")
{
    Console.WriteLine(Tests.Windows.Combat.NarrativeRevisionTests.Run(args[0], args.Length == 3 ? args[2] : null));
    Console.WriteLine(Tests.Core.Localization.LocalizationTests.Run(args[0]));
    Tests.Core.Persistence.RuntimeSnapshotTests.Run(args[0]);
    return;
}

if (args.Length == 2 && args[1] == "--resource-labels")
{
    Console.WriteLine(Tests.Core.Localization.ResourceLabelTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Localization.LocalizationTests.Run(args[0]));
    Console.WriteLine(Tests.Windows.Localization.LiveLanguageTests.Run(args[0]));
    Tests.Core.Render.SceneRenderTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Interiors.InteriorTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--save-localization")
{
    Tests.Core.Persistence.RuntimeSnapshotTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Render.LayoutFeedbackTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Render.LayoutFeedbackTests.Run(args[0], "be-BY"));
    Tests.Core.Exploration.WorldRenderTests.Run(args[0]);
    return;
}

if (args.Length == 2 && args[1] == "--library-save")
{
    Tests.Core.Persistence.RuntimeSnapshotTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Town.TigerLibraryTests.Run(args[0]));
    foreach (string result in Tests.Core.Script.ScriptTests.Run(args[0]))
    {
        Console.WriteLine(result);
    }
    return;
}

if (args.Length == 2 && args[1] == "--frame-sounds")
{
    Console.WriteLine(Tests.Core.Combat.CombatFrameSoundTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--tiger-library")
{
    Console.WriteLine(Tests.Core.Town.TigerLibraryTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Interiors.GeographerTests.Run(args[0]));
    foreach (string result in Tests.Core.Script.ScriptTests.Run(args[0]))
    {
        Console.WriteLine(result);
    }
    return;
}

if (args.Length is 2 or 3 && args[1] == "--selection-assets")
{
    Console.WriteLine(Tests.Windows.Combat.CombatSelectionAssetsTests.Run(args[0], args.Length == 3 ? args[2] : null));
    return;
}

if (args.Length == 2 && args[1] == "--necromancer")
{
    Console.WriteLine(Tests.Core.Combat.NecromancerTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--hero-roster")
{
    Console.WriteLine(Tests.Core.State.HeroRosterTests.Run(args[0]));
    return;
}


if (args.Length == 2 && args[1] == "--combat-balance")
{
    Console.WriteLine(Tests.Core.Combat.CombatBalanceTests.Run(args[0]));
    Console.WriteLine(Tests.Core.State.HeroRosterTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.PartySpellTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--profile-session")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyProfileIntegration(args[0], args[1], args[2], Tests.Windows.WindowsSessionProbeHost.Create));
    return;
}

if (args.Length == 2 && args[1] == "--profiles")
{
    Console.WriteLine(Tests.Windows.Profiles.PlayerProfileTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--projectile-reaction")
{
    Console.WriteLine(Tests.Core.Combat.ProjectileReactionTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatParticleTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--party-air")
{
    Console.WriteLine(Tests.Core.Combat.PartyAirCombatTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--party-spells")
{
    Console.WriteLine(Tests.Core.Combat.PartySpellTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--travel-cost")
{
    Console.WriteLine(Tests.Core.Exploration.ExplorationTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Status.Feedback035Tests.Run(args[0]));
    Console.WriteLine(Tests.Core.State.QuestDayCounterTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--experience-layout")
{
    Console.WriteLine(Tests.Core.Progression.ProgressionTests.Run(args[0]));
    Console.WriteLine(Tests.Windows.Progression.ExperienceLayoutTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--language-session")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyLanguageIntegration(args[0], args[1], args[2], Tests.Windows.WindowsSessionProbeHost.Create));
    return;
}

if (args.Length == 2 && args[1] == "--live-language")
{
    Console.WriteLine(Tests.Windows.Localization.LiveLanguageTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--belarusian-layout")
{
    Console.WriteLine(Tests.Core.Render.LayoutFeedbackTests.Run(args[0], "be-BY"));
    return;
}

if (args.Length == 2 && args[1] == "--script-arguments")
{
    Console.WriteLine(Tests.Core.Script.ScriptArgumentAuditTests.Run(args[0]));
    return;
}

if (args.Length is 2 or 3 && args[1] == "--quest-timer")
{
    Console.WriteLine(Tests.Core.State.QuestDayCounterTests.Run(args[0], args.Length == 3 ? args[2] : null));
    Console.WriteLine(Tests.Core.State.StateTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--layout-feedback")
{
    Console.WriteLine(Tests.Core.Render.LayoutFeedbackTests.Run(args[0]));
    Tests.Core.Interiors.ShopRendererTests.Run(args[0]);
    Tests.Core.Status.StatusRendererTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Persistence.PersistenceRuntimeTests.RunMenuRaster(args[0]));
    Console.WriteLine(Tests.Core.ActionMenu.ActionMenuTests.Run(args[0]));
    Tests.Core.Exploration.WorldRenderTests.Run(args[0]);
    return;
}


if (args.Length is 2 or 3 && args[1] == "--shop-stock")
{
    Console.WriteLine(Tests.Core.Interiors.ShopStockTests.Run(args[0], args.Length == 3 ? args[2] : null));
    Console.WriteLine(Tests.Core.Interiors.InteriorTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--vertical-menu")
{
    Console.WriteLine(Tests.Core.Menu.MainMenuTests.Run());
    Console.WriteLine(Tests.Windows.Menu.VerticalMenuTests.Run(args[0]));
    Console.WriteLine(Tests.Windows.Render.UnicodeTextTests.Run(args[0]));
    return;
}


if (args.Length == 2 && args[1] == "--journal")
{
    Console.WriteLine(Tests.Core.Status.QuestJournalTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Status.StatusMenuTests.Run());
    Tests.Core.Status.StatusRendererTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Localization.LocalizationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--unicode")
{
    Console.WriteLine(Tests.Windows.Render.UnicodeTextTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Localization.LocalizationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--localization")
{
    Console.WriteLine(Tests.Core.Localization.LocalizationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--refactor")
{
    Console.WriteLine(Tests.Core.Resources.StateModelTests.Run());
    Console.WriteLine(Tests.Core.Resources.GameDefinitionTests.Run());
    Console.WriteLine(Tests.Windows.RefactoringTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatSlowDurationTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--slow-duration")
{
    Console.WriteLine(Tests.Core.Combat.CombatSlowDurationTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatParticleTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatSchedulingTests.Run());
    return;
}

if (args.Length == 3 && args[2] == "--feedback-035")
{
    Console.WriteLine(Tests.Core.Status.Feedback035Tests.Run(args[0], args[1]));
    Console.WriteLine(Tests.Core.Status.StatusMenuTests.Run());
    Tests.Core.Status.StatusRendererTests.Run(args[0]);
    Tests.Core.Interiors.ShopRendererTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Combat.CombatOutcomeTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Dungeon.DungeonTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Exploration.ExplorationTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Persistence.PersistenceRuntimeTests.Run(args[0]));
    return;
}

if (args.Length == 3 && args[2] == "--ruins")
{
    Console.WriteLine(Tests.Core.Exploration.RuinsAccessTests.Run(args[0], args[1]));
    Tests.Core.Exploration.WorldRenderTests.Run(args[0]);
    Console.WriteLine(Tests.Core.Exploration.ExplorationTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--gatha-guild-session")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyGeographerIntegration(args[0], args[1], args[2], Tests.Windows.WindowsSessionProbeHost.Create, true));
    return;
}

if (args.Length == 2 && args[1] == "--gatha-guild")
{
    Console.WriteLine(Tests.Core.Interiors.GathaGuildTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Interiors.InteriorTests.Run(args[0]));
    Console.WriteLine(Tests.Core.ActionMenu.ActionMenuTests.Run(args[0]));
    foreach (string line in Tests.Core.Script.ScriptTests.Run(args[0]))
    {
        Console.WriteLine(line);
    }

    return;
}

if (args.Length == 4 && args[3] == "--geographer-session")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyGeographerIntegration(args[0], args[1], args[2], Tests.Windows.WindowsSessionProbeHost.Create));
    return;
}

if (args.Length == 2 && args[1] == "--geographer")
{
    Console.WriteLine(Tests.Core.Interiors.GeographerTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Interiors.InteriorTests.Run(args[0]));
    return;
}

if (args.Length == 4 && args[3] == "--autosave-session")
{
    Console.WriteLine(Atria.Core.Runtime.GameSession.VerifyPersistenceIntegration(args[0], args[1], args[2], Tests.Windows.WindowsSessionProbeHost.Create));
    return;
}

if (args.Length == 2 && args[1] == "--autosave")
{
    Console.WriteLine(Tests.Core.Persistence.SaveRecoveryTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Persistence.AutosaveTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Persistence.PersistenceRuntimeTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--combat-audio")
{
    Console.WriteLine(Tests.Core.Combat.CombatEntryTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Audio.CombatSoundTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--dungeon-switch")
{
    Console.WriteLine(Tests.Core.Dungeon.DungeonTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Dungeon.DungeonSwitchRasterTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--controls")
{
    Console.WriteLine(Tests.Windows.Controls.ControlRedesignTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--reward-policy")
{
    Console.WriteLine(Tests.Core.Combat.CombatOutcomeTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatSessionBridgeTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Progression.ProgressionTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--feedback-011")
{
    Console.WriteLine(Tests.Core.Menu.Feedback011Tests.Run(args[0]));
    Console.WriteLine(Tests.Core.Menu.MainMenuTests.Run());
    Console.WriteLine(Tests.Windows.Menu.VerticalMenuTests.Run(args[0]));
    Console.WriteLine(Tests.Core.ActionMenu.ActionMenuTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Town.TownTests.Run(args[0]));
    Console.WriteLine(Tests.Core.Combat.CombatOutcomeTests.Run(args[0]));
    return;
}

if (args.Length == 2 && args[1] == "--save-menu-raster")
{
    Console.WriteLine(Tests.Core.Persistence.PersistenceRuntimeTests.RunMenuRaster(args[0]));
    return;
}

if (args.Length == 3 && args[2] == "--battle-diagnostic")
{
    Tests.Core.Combat.CombatEncounterDiagnostic.Run(args[0]);
    return;
}

if (args.Length != 2)
{
    throw new ArgumentException("Expected original-resource and music roots.");
}

Console.WriteLine($"Verification runtime: dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}.");
Console.WriteLine(Tests.Windows.Profiles.PlayerProfileTests.Run(args[0]));
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
Console.WriteLine(Tests.Core.Localization.LocalizationTests.Run(args[0]));
Console.WriteLine(Tests.Windows.Localization.LiveLanguageTests.Run(args[0]));
Console.WriteLine(Tests.Core.Localization.ResourceLabelTests.Run(args[0]));
Console.WriteLine(Tests.Core.Resources.StateModelTests.Run());
Console.WriteLine(Tests.Core.Resources.GameDefinitionTests.Run());
Console.WriteLine(Tests.Windows.RefactoringTests.Run(args[0]));
Console.WriteLine(Tests.Windows.Controls.ControlRedesignTests.Run(args[0]));
Console.WriteLine(Tests.Core.Dungeon.DungeonSwitchRasterTests.Run(args[0]));
Console.WriteLine(Tests.Core.Resources.ResourceContractTests.Run(args[0], args[1]));
Tests.Core.Render.RenderTests.Run(args[0]);
foreach (string line in Tests.Core.Script.ScriptTests.Run(args[0]))
{
    Console.WriteLine(line);
}

Console.WriteLine(Tests.Core.State.StateTests.Run(args[0]));
Console.WriteLine(Tests.Core.Menu.MainMenuTests.Run());
Console.WriteLine(Tests.Windows.Menu.VerticalMenuTests.Run(args[0]));
Console.WriteLine(Tests.Core.Menu.Feedback011Tests.Run(args[0]));
Console.WriteLine(Tests.Core.Persistence.PersistenceTests.Run());
Console.WriteLine(Tests.Core.Persistence.PersistenceRuntimeTests.Run(args[0]));
Console.WriteLine(Tests.Core.Persistence.AutosaveTests.Run(args[0]));
Console.WriteLine(Tests.Core.Persistence.SaveRecoveryTests.Run(args[0]));
Console.WriteLine(Tests.Core.Audio.MusicTests.Run());
Console.WriteLine(Tests.Windows.Audio.MusicStreamTests.Run(args[1]));
Console.WriteLine(Tests.Windows.Dialogue.DialogueTests.Run(args[0]));
Console.WriteLine(Tests.Core.Menu.MenuRasterTests.Run(args[0]));
Console.WriteLine(Tests.Core.Platform.ClockTests.Run(args[0]));
Console.WriteLine(Tests.Core.Audio.SoundEffectTests.Run(args[0]));
Console.WriteLine(Tests.Core.Audio.CombatSoundTests.Run(args[0]));
Console.WriteLine(Tests.Core.Settings.SettingsTests.Run(args[0]));
Console.WriteLine(Tests.Core.Credits.CreditsTests.Run(args[0]));
Console.WriteLine(Tests.Core.Termination.TerminationTests.Run(args[0]));
Console.WriteLine(Tests.Core.World.WorldTests.Run(args[0]));
Console.WriteLine(Tests.Core.Town.TownTests.Run(args[0]));
Console.WriteLine(Tests.Core.Audio.PcmChunkTests.Run());
Console.WriteLine(Tests.Windows.Video.VideoTests.Run(args[0]));
Console.WriteLine(Tests.Windows.Audio.PcmChunkProbeTests.RunMutedProbe());
Console.WriteLine(Tests.Core.ActionMenu.ActionMenuTests.Run(args[0]));
Console.WriteLine(Tests.Core.Exploration.ExplorationTests.Run(args[0]));
Console.WriteLine(Tests.Core.Exploration.RuinsAccessTests.Run(args[0]));
Console.WriteLine(Tests.Core.Progression.ProgressionTests.Run(args[0]));
Console.WriteLine(Tests.Windows.Progression.ExperienceLayoutTests.Run(args[0]));
Console.WriteLine(Tests.Core.Interiors.InteriorTests.Run(args[0]));
Console.WriteLine(Tests.Core.Interiors.GeographerTests.Run(args[0]));
Console.WriteLine(Tests.Core.Interiors.GathaGuildTests.Run(args[0]));
Console.WriteLine(Tests.Core.Status.StatusMenuTests.Run());
Console.WriteLine(Tests.Core.Status.QuestJournalTests.Run(args[0]));
Console.WriteLine(Tests.Core.Status.Feedback035Tests.Run(args[0]));
Tests.Core.Status.StatusRendererTests.Run(args[0]);
Console.WriteLine(Tests.Core.Combat.PartyAirCombatTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.PartySpellTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatFrameSoundTests.Run(args[0]));
Console.WriteLine(Tests.Windows.Combat.CombatSelectionAssetsTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatBalanceTests.Run(args[0]));
Console.WriteLine(Tests.Core.State.HeroRosterTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatSlowDurationTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatEntryTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatSpecialRasterTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatSchedulingTests.Run());
Console.WriteLine(Tests.Core.Combat.CombatGlobalUiTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatRandomEncounterTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatDebugInputTests.Run());
Console.WriteLine(Tests.Core.Combat.CombatSessionBridgeTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatAttributeDomainTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatInfoModelTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatExecutionHostTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatEncounterDiagnostic.Run(args[0], false));
Console.WriteLine(Tests.Windows.Combat.CombatTransportTests.Run());
Console.WriteLine(Tests.Core.Combat.CombatTransitionTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatOutcomeTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.ProjectileReactionTests.Run(args[0]));
Console.WriteLine(Tests.Core.Combat.CombatParticleTests.Run(args[0]));
Console.WriteLine(Tests.Core.Dungeon.DungeonTests.Run(args[0]));

Console.WriteLine(Tests.Core.Combat.NecromancerTests.Run(args[0]));
