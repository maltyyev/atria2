using Atria.Core.Contracts.Host;
using Atria.Core.Game;
using Atria.Core.Game.Exploration;
using Atria.Core.Game.Navigation;
using Atria.Core.Presentation.Controls;
using Atria.Core.Presentation.Menu;
using Atria.Core.Resources;
using Atria.Core.Resources.Localization;
using Atria.Core.Runtime.Input;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    /// <summary>Checks that a world-map touch edge survives multiple input pumps before its real scene consumer.</summary>
    internal static string VerifyMobileWorldInputIntegration(string resourceRoot, string musicRoot, GameSessionProbeHostFactory createHost)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(resourceRoot, "..")), "audit", "mobile-world-session", Guid.NewGuid().ToString("N"));
        LocalizationCatalog previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization")));
        GameSessionProbeHost? probeHost = null;
        GameSession? session = null;
        try
        {
            Directory.CreateDirectory(output);
            probeHost = createHost(resourceRoot, musicRoot, output, 60, true);
            session = probeHost.Session;
            IMobileUiChannel channel = probeHost.Window as IMobileUiChannel
                ?? throw new InvalidOperationException("Mobile world input probe requires a host with IMobileUiChannel.");
            session.PublishMobileUiSnapshot(channel);
            ulong lastContext = session.mobileUiSnapshot?.ContextId
                ?? throw new InvalidOperationException("Mobile overlay probe did not publish its initial context.");
            void VerifyScopeSnapshot(string expectedScene, MobileOverlayKind expectedOverlay)
            {
                MobileUiSnapshot snapshot = session.mobileUiSnapshot
                    ?? throw new InvalidOperationException("Mobile overlay probe lost its snapshot.");
                if (snapshot.Scene != expectedScene || snapshot.OverlayKind != expectedOverlay || snapshot.ContextId <= lastContext)
                    throw new InvalidOperationException($"Mobile overlay scope expected a fresh {expectedScene}/{expectedOverlay} context.");
                lastContext = snapshot.ContextId;
            }

            using (session.BeginMobileOverlayScope(MobileOverlayKind.CombatSelection)
                ?? throw new InvalidOperationException("Selection scope was unavailable on a mobile host."))
            {
                VerifyScopeSnapshot("combat.selection", MobileOverlayKind.CombatSelection);
                using (session.BeginMobileOverlayScope(MobileOverlayKind.Navigation)
                    ?? throw new InvalidOperationException("Nested navigation scope was unavailable."))
                {
                    VerifyScopeSnapshot("controls.reference", MobileOverlayKind.Navigation);
                }

                VerifyScopeSnapshot("combat.selection", MobileOverlayKind.CombatSelection);
                try
                {
                    using (session.BeginMobileOverlayScope(MobileOverlayKind.ExperienceAllocation)
                        ?? throw new InvalidOperationException("XP scope was unavailable."))
                    {
                        VerifyScopeSnapshot("combat.experience", MobileOverlayKind.ExperienceAllocation);
                        throw new InvalidOperationException("expected overlay-scope unwind");
                    }
                }
                catch (InvalidOperationException error) when (error.Message == "expected overlay-scope unwind")
                {
                }

                VerifyScopeSnapshot("combat.selection", MobileOverlayKind.CombatSelection);
            }

            VerifyScopeSnapshot("main", MobileOverlayKind.None);
            session.HandleMenu(new(MenuEffect.ResetGame));
            session.HandleMenu(new(MenuEffect.SetNewGameGlobals));
            session.HandleMenu(new(MenuEffect.LoadGroundMap, Resource: "ground1.amf"));
            session.HandleMenu(new(MenuEffect.InitializeGroundMap, 0));
            session.HandleMenu(new(MenuEffect.CopyInitialGroundLocation));
            session.LoadExplorationReturnMap("ground1.amf"u8.ToArray());
            session.CommitLocation(LocationKind.World);
            session.state.ScriptActive = 0;
            session.pendingHandler = [];
            session.state.ChangedChip = 0;
            session.state.DungeonEntryPending = 0;
            session.exploration!.InputDelay = 0;
            session.exploration.RefreshPending = false;
            session.dialogueActive = false;
            session.actions.CloseAll();

            var assets = session.explorationAssets ?? throw new InvalidOperationException("World input probe lost world animation assets.");
            var renderer = session.explorationRenderer ?? throw new InvalidOperationException("World input probe lost the world renderer.");
            WorldDirection? requestedDirection = null;
            foreach (WorldDirection candidate in Enum.GetValues<WorldDirection>())
            {
                var candidateController = new ExplorationController(session.state, session.map!, assets.CharacterAnimation,
                    session.random, renderer.IsSceneReferenceBlocked);
                if (candidateController.BeginTravel(candidate) != TravelStartResult.Blocked)
                {
                    requestedDirection = candidate;
                    break;
                }
            }
            WorldDirection direction = requestedDirection
                ?? throw new InvalidOperationException("World input probe found no traversable or revealable neighbor.");
            MobileTouchButtons touchDirection = direction switch
            {
                WorldDirection.Left => MobileTouchButtons.Left,
                WorldDirection.Right => MobileTouchButtons.Right,
                WorldDirection.Up => MobileTouchButtons.Up,
                WorldDirection.Down => MobileTouchButtons.Down,
                _ => throw new InvalidOperationException("World input probe selected an invalid direction.")
            };

            session.PublishMobileUiSnapshot(channel);
            ulong contextId = session.mobileUiSnapshot?.ContextId
                ?? throw new InvalidOperationException("World input probe did not publish a Core context.");
            channel.SubmitTouchState(new(contextId, 0, 0, touchDirection));
            session.host.Pump();
            session.host.Pump();
            session.HandleExplorationInput();
            if (!session.exploration.Moving && session.state.Mode != GameMode.WorldEntry)
                throw new InvalidOperationException($"World input consumer did not handle touch {direction} after repeated host pumps.");

            return $"Mobile world input PASS: touch {direction} survived two host pumps and reached HandleExplorationInput. Evidence: {output}";
        }
        finally
        {
            try
            {
                try { session?.DisposeProgression(); }
                finally
                {
                    try { session?.DisposeInputRouter(); }
                    finally
                    {
                        try { session?.StopAudio(); }
                        finally { probeHost?.Dispose(); }
                    }
                }
            }
            finally
            {
                GameText.Use(previous);
            }
        }
    }

    private readonly record struct MobileContextKey(
        int Mode,
        string Scene,
        MobileOverlayKind Overlay,
        bool DirectionsAreMenu,
        ulong ModalIdentity,
        int ActionDepth,
        int ActionType,
        bool DialogueActive,
        bool MoviePlaying,
        int StatusPage,
        bool StatusDetail,
        bool TownLeaveConfirmation,
        bool ShopQuantityActive,
        bool ShopBuying,
        string MenuIdentity,
        string? TextRequestId);

    private MobileUiSnapshot? mobileUiSnapshot;
    private MobileContextKey? mobileUiContextKey;
    private string? mobileUiNotice;
    private ulong mobileUiContextId, mobileUiRevision;
    private ulong mobileModalSequence, mobileModalIdentity;
    private MobileOverlayKind? mobileOverlayOverride;
    private ControlSettingsMenu? mobileSettingsMenu;
    private bool mobileMoviePlaying;
    private MobileMenuSnapshot? mobileMenuIdentitySource;
    private string mobileMenuIdentity = "";

    private IDisposable? BeginMobileOverlayScope(MobileOverlayKind? overlay)
    {
        if (window is not IMobileUiChannel channel) return null;
        MobileOverlayKind? previousOverlay = mobileOverlayOverride;
        ulong previousIdentity = mobileModalIdentity;
        mobileOverlayOverride = overlay;
        mobileModalIdentity = checked(++mobileModalSequence);
        try
        {
            PublishMobileUiSnapshot(channel);
            return new MobileOverlayLease(() =>
            {
                mobileOverlayOverride = previousOverlay;
                mobileModalIdentity = previousIdentity;
                PublishMobileUiSnapshot(channel);
            });
        }
        catch
        {
            mobileOverlayOverride = previousOverlay;
            mobileModalIdentity = previousIdentity;
            throw;
        }
    }

    private sealed class MobileOverlayLease(Action close) : IDisposable
    {
        private Action? close = close;
        public void Dispose() => Interlocked.Exchange(ref close, null)?.Invoke();
    }

    private ControlSettingsMenu? SetMobileSettingsMenu(ControlSettingsMenu? menu)
    {
        ControlSettingsMenu? previous = mobileSettingsMenu;
        mobileSettingsMenu = menu;
        return previous;
    }

    private void PollMobileUi()
    {
        if (window is not IMobileUiChannel channel) return;
        PublishMobileUiSnapshot(channel);
        if (!window.Focused || !channel.TryDequeueUiCommand(out var command)) return;
        if (command.ContextId == mobileUiContextId)
            DispatchMobileUiCommand(command);
        PublishMobileUiSnapshot(channel);
    }

    private void DispatchMobileUiCommand(MobileUiCommand command)
    {
        if (mobileUiSnapshot is not { } snapshot || command.ContextId != snapshot.ContextId) return;
        if (command.Action == MobileUiAction.CycleLanguage)
        {
            if (!snapshot.ShowLanguageButton) return;
            try
            {
                ChangeLanguage(GameText.Current.NextLanguage());
                mobileUiNotice = null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                mobileUiNotice = GameText.Get("ui.mobile.languageFailed");
                controls.Notice = mobileUiNotice;
                Console.WriteLine("Mobile language change failed: " + error.Message);
            }
            return;
        }

        DispatchMobileMenu(command);
    }

    private void PublishMobileUiSnapshot(IMobileUiChannel channel)
    {
        MobileMenuSnapshot? menuSnapshot = BuildMobileMenu();
        MobileTextRequest? textRequest = BuildMobileTextRequest();
        MobileOverlayKind overlay = MobileOverlay(menuSnapshot, textRequest);
        bool menuDirections = overlay == MobileOverlayKind.Navigation && (
            mobileOverlayOverride == MobileOverlayKind.Navigation || blockingControls || actions.Depth >= 0 || dialogueActive ||
            state.Mode is not (GameMode.World or GameMode.WorldEntry or GameMode.Dungeon));
        string scene = MobileScene(menuSnapshot, textRequest);
        string menuIdentity = MobileMenuIdentity(menuSnapshot);
        var contextKey = new MobileContextKey(
            state.Mode, scene, overlay, menuDirections, mobileModalIdentity, actions.Depth, actions.ContextType,
            dialogueActive, mobileMoviePlaying, state.Mode == GameMode.Status ? statusRuntime.Menu.Page : -1,
            state.Mode == GameMode.Status && statusRuntime.Menu.DetailActive,
            state.Mode == GameMode.Town && town.LeaveConfirmation,
            state.Mode == GameMode.Shop && trade?.QuantityActive == true,
            state.Mode == GameMode.Shop && trade?.Buying == true,
            menuIdentity,
            textRequest?.Id);
        if (mobileUiContextKey != contextKey)
        {
            mobileUiContextKey = contextKey;
            mobileUiContextId = checked(mobileUiContextId + 1);
        }

        controls?.SetMobileContext(mobileUiContextId, overlay, menuDirections);
        bool showLanguage = GameText.Current.AvailableLanguages.Count > 1
            && !battleActive
            && overlay is not (MobileOverlayKind.CombatSelection or MobileOverlayKind.ExperienceAllocation)
            && !mobileMoviePlaying
            && textRequest is null
            && state.Mode is not (GameMode.MainMenu or GameMode.Settings or GameMode.Save or GameMode.Load)
            && menuSnapshot?.Scene.StartsWith("settings.", StringComparison.Ordinal) != true
            && menuSnapshot?.Scene is not ("save" or "load" or "profiles" or "profileName");
        var candidate = new MobileUiSnapshot(
            mobileUiContextId,
            0,
            scene,
            overlay,
            showLanguage,
            new(
                GameText.Get("ui.controls.up"),
                GameText.Get("ui.controls.down"),
                GameText.Get("ui.controls.left"),
                GameText.Get("ui.controls.right"),
                overlay == MobileOverlayKind.CombatSelection
                    ? GameText.Format("ui.combat.start", ("button", "A"))
                    : overlay == MobileOverlayKind.ExperienceAllocation
                        ? GameText.Get("ui.mobile.experience.cycleMode")
                        : GameText.Get("ui.controls.confirm"),
                overlay == MobileOverlayKind.ExperienceAllocation
                    ? GameText.Get("ui.mobile.experience.finish")
                    : GameText.Get("ui.controls.back"),
                overlay == MobileOverlayKind.CombatSelection
                    ? GameText.Format("ui.combat.toggleHero", ("button", "X"))
                    : GameText.Get("ui.controls.attack"),
                GameText.Get("ui.controls.jump"),
                GameText.Get("ui.mobile.quickModifier"),
                GameText.Get("ui.settings.language"),
                GameText.Get("ui.controls.movementStick"),
                GameText.Format("ui.profiles.mobileButton", ("name", playerProfiles.Active.Name))),
            menuSnapshot,
            textRequest,
            CanChangeProfile && !menu.Confirmation && menuSnapshot is null && state.Mode == GameMode.MainMenu,
            mobileUiNotice);
        if (mobileUiSnapshot is not null &&
            (candidate with { Revision = 0 }) == (mobileUiSnapshot with { Revision = 0 })) return;
        mobileUiRevision = checked(mobileUiRevision + 1);
        mobileUiSnapshot = candidate with { Revision = mobileUiRevision };
        channel.PublishUiSnapshot(mobileUiSnapshot);
    }

    private string MobileScene(MobileMenuSnapshot? menuSnapshot, MobileTextRequest? textRequest)
    {
        if (mobileMoviePlaying) return "movie";
        if (textRequest is not null) return "profileName";
        if (menuSnapshot is not null) return menuSnapshot.Scene;
        if (blockingControls) return "pause";
        if (mobileOverlayOverride == MobileOverlayKind.Navigation) return "controls.reference";
        if (mobileOverlayOverride == MobileOverlayKind.CombatSelection) return "combat.selection";
        if (mobileOverlayOverride == MobileOverlayKind.ExperienceAllocation) return "combat.experience";
        if (battleActive) return "combat";
        return state.Mode switch
        {
            GameMode.MainMenu or GameMode.QuitConfirmation => "main",
            GameMode.World => "world",
            GameMode.WorldEntry => "worldEntry",
            GameMode.Dungeon => "dungeon",
            GameMode.Town => "town",
            GameMode.Status => "status",
            GameMode.Settings => "settings",
            GameMode.Save => "save",
            GameMode.Load => "load",
            GameMode.Interior => "interior",
            GameMode.Inn => "inn",
            GameMode.Shop => "shop",
            GameMode.Credits => "credits",
            GameMode.Defeat => "defeat",
            GameMode.Ending => "ending",
            _ => "game"
        };
    }

    private MobileOverlayKind MobileOverlay(MobileMenuSnapshot? menuSnapshot, MobileTextRequest? textRequest)
    {
        if (mobileMoviePlaying) return MobileOverlayKind.None;
        if (textRequest is not null || menuSnapshot is not null) return MobileOverlayKind.None;
        if (blockingControls) return MobileOverlayKind.Navigation;
        if (dialogueActive) return MobileOverlayKind.Navigation;
        if (mobileOverlayOverride is { } forced) return forced;
        if (battleActive) return MobileOverlayKind.Combat;
        return state.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon or GameMode.Town or GameMode.Interior
            or GameMode.Inn or GameMode.Shop or GameMode.Status or GameMode.Credits or GameMode.Defeat
            ? MobileOverlayKind.Navigation
            : MobileOverlayKind.None;
    }

    private string MobileMenuIdentity(MobileMenuSnapshot? snapshot)
    {
        if (ReferenceEquals(snapshot, mobileMenuIdentitySource)) return mobileMenuIdentity;
        mobileMenuIdentitySource = snapshot;
        if (snapshot is null)
        {
            mobileMenuIdentity = "";
            return mobileMenuIdentity;
        }

        if (snapshot.ContextKey is { } catalogIdentity)
        {
            mobileMenuIdentity = string.Concat(snapshot.Scene, "|", catalogIdentity, "|",
                (int)(snapshot.Modal?.Kind ?? MobileMenuModalKind.None), "|", snapshot.Capturing, "|", snapshot.CaptureRowId);
            return mobileMenuIdentity;
        }

        var identity = new System.Text.StringBuilder(snapshot.Scene);
        identity.Append('|').Append((int)(snapshot.Modal?.Kind ?? MobileMenuModalKind.None));
        identity.Append('|').Append(snapshot.Capturing).Append('|').Append(snapshot.CaptureRowId);
        foreach (var row in snapshot.Rows) identity.Append('|').Append(row.Id);
        mobileMenuIdentity = identity.ToString();
        return mobileMenuIdentity;
    }

}
