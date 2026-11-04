using Atria.Core.Contracts.Host;
using Atria.Core.Contracts.Input;
using Atria.Core.Game;
using Atria.Core.Game.Navigation;
using Atria.Core.Persistence;
using Atria.Core.Persistence.Profiles;
using Atria.Core.Presentation.Controls;
using Atria.Core.Presentation.Profiles;
using Atria.Core.Resources;
using Atria.Core.Resources.Localization;
using Atria.Core.Runtime.Input;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    private readonly MobileMenuCommandGuard mobileMenuCommandGuard = new();
    private MobileMenuSnapshot? cachedMobileMenu;
    private object? cachedMobileMenuSource;
    private string? cachedMobileMenuKey;
    private MobileTextRequest? cachedMobileTextRequest;
    private ProfileNameEditor? mobileProfileEditor;
    private bool mobileProfilesOpen, mobileCreatingProfile;
    private PlayerProfile? mobileCreatedProfileAwaitingSwitch;
    private string? mobileProfileTargetId, mobileProfileRequestId, mobileProfileNotice;
    private string? mobileProfileAutoScrollRowId;
    private int mobileProfileSelection;
    private long mobileProfileRequestSequence;
    private SaveSlotInfo[]? mobilePersistenceCatalogSource;
    private IReadOnlyList<PlayerProfile>? mobileProfileCatalogSource;
    private long mobilePersistenceCatalogGeneration, mobileProfileCatalogGeneration;
    private int mobileLastPersistenceSelection = -1;
    private bool mobileNativeMenuTouched;

    internal static string VerifyMobileMenuIntegration(
        string resourceRoot,
        string musicRoot,
        string fixture,
        GameSessionProbeHostFactory createHost)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(resourceRoot, "..")), "audit", "mobile-menu-session", Guid.NewGuid().ToString("N"));
        LocalizationCatalog previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization")));
        try
        {
            Directory.CreateDirectory(Path.Combine(output, "saves"));
            File.Copy(fixture, Path.Combine(output, "saves", "atria000.sav"));
            using var probeHost = createHost(resourceRoot, musicRoot, output, 60, true);
            GameSession session = probeHost.Session;
            IMobileUiChannel channel = probeHost.Window as IMobileUiChannel
                ?? throw new InvalidOperationException("Mobile menu probe requires a host with IMobileUiChannel.");
            try
            {
                session.PollMobileUi();
                MobileUiSnapshot snapshot = CopyMobileSnapshot(channel);
                Check(snapshot.Scene == "main" && snapshot.ShowProfilesButton,
                    "the main menu publishes a localized profile action when profile changes are allowed");
                ulong idleRevision = snapshot.Revision;
                session.PollMobileUi();
                Check(CopyMobileSnapshot(channel).Revision == idleRevision,
                    "an unchanged mobile snapshot keeps its revision stable");

                session.OpenScreen(UiScreen.Load);
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot russianLoad = RequireMenu(snapshot, "load");
                Check(GameText.Current.Language == LocalizationCatalog.BaseLanguage
                    && russianLoad.Title == GameText.Get("messages.260")
                    && snapshot.Labels.Back == GameText.Get("ui.controls.back"),
                    "Russian Load title and Back label come from the active catalog");
                Check(session.CloseScreen(), "the Russian Load probe returns to Main Menu");
                session.PollMobileUi();
                session.OpenScreen(UiScreen.Save);
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot russianSave = RequireMenu(snapshot, "save");
                Check(russianSave.Title == GameText.Get("messages.362"),
                    "Russian Save title comes from the active catalog");
                Check(session.CloseScreen(), "the Russian Save probe returns to Main Menu");
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);

                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.OpenProfiles));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot profileMenu = RequireMenu(snapshot, "profiles");
                Check(snapshot.TextRequest is null,
                    "OpenProfiles enters the Core profile list without opening a text keyboard");
                int originalProfileCount = session.playerProfiles.Profiles.Count;
                string originalProfileName = session.playerProfiles.Active.Name;
                MobileMenuRow create = profileMenu.Rows.Single(row => row.Id == "profiles.create");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Activate, create.Id));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                _ = RequireMenu(snapshot, "profileName");
                MobileTextRequest request = snapshot.TextRequest
                    ?? throw new InvalidOperationException("Mobile profile editor omitted its text request.");
                Check(!string.IsNullOrEmpty(request.Id),
                    "the profile name keyboard appears only after activating Create");
                string requestId = request.Id;
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.SubmitText, requestId, Text: "   "));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot editorMenu = RequireMenu(snapshot, "profileName");
                MobileTextRequest currentRequest = snapshot.TextRequest
                    ?? throw new InvalidOperationException("Invalid profile text lost its request.");
                Check(currentRequest.Id == requestId && currentRequest.InitialValue == "   "
                    && !string.IsNullOrEmpty(editorMenu.Notice)
                    && session.playerProfiles.Profiles.Count == originalProfileCount,
                    "invalid full-text profile input remains visible and does not create a profile");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.SubmitText, requestId, InvalidText: true));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                editorMenu = RequireMenu(snapshot, "profileName");
                currentRequest = snapshot.TextRequest
                    ?? throw new InvalidOperationException("Rejected native text lost its request.");
                Check(currentRequest.Id == requestId && currentRequest.InitialValue == "   "
                    && !string.IsNullOrEmpty(editorMenu.Notice)
                    && session.playerProfiles.Profiles.Count == originalProfileCount,
                    "an oversized or malformed native text payload preserves the editor and creates no profile");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.CancelText, requestId));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                _ = RequireMenu(snapshot, "profiles");
                Check(snapshot.TextRequest is null
                    && session.playerProfiles.Profiles.Count == originalProfileCount
                    && session.playerProfiles.Active.Name == originalProfileName,
                    "Cancel leaves profile data untouched and returns to the list");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Back));
                session.PollMobileUi();

                int originalMusic = session.settings.Music;
                session.OpenScreen(UiScreen.Settings);
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot settingsMenu = RequireMenu(snapshot, "settings.root");
                MobileMenuRow language = settingsMenu.Rows.Single(row => row.Id == "settings.root.language");
                Check(GameText.Current.Language == LocalizationCatalog.BaseLanguage
                    && language.Value == GameText.Get("ui.languages." + LocalizationCatalog.BaseLanguage),
                    "Settings starts with the persisted Russian locale");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Activate, language.Id));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                settingsMenu = RequireMenu(snapshot, "settings.root");
                Check(GameText.Current.Language == LocalizationCatalog.BelarusianLanguage
                    && settingsMenu.Rows.Single(row => row.Id == language.Id).Value == GameText.Get("ui.languages." + LocalizationCatalog.BelarusianLanguage)
                    && snapshot.Labels.Back == "Вярнуцца",
                    "a live mobile Settings switch to Belarusian refreshes labels immediately");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Activate, language.Id));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                settingsMenu = RequireMenu(snapshot, "settings.root");
                Check(GameText.Current.Language == LocalizationCatalog.BaseLanguage
                    && settingsMenu.Rows.Single(row => row.Id == language.Id).Value == GameText.Get("ui.languages." + LocalizationCatalog.BaseLanguage)
                    && snapshot.Labels.Back == GameText.Get("ui.controls.back"),
                    "a live mobile Settings switch back to Russian refreshes labels immediately");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Activate, language.Id));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                settingsMenu = RequireMenu(snapshot, "settings.root");
                Check(GameText.Current.Language == LocalizationCatalog.BelarusianLanguage
                    && snapshot.Labels.Back == "Вярнуцца",
                    "the mobile Settings locale cycle remains stable when returning to Belarusian");
                MobileMenuRow music = settingsMenu.Rows.Single(row => row.Id == "settings.root.music");
                int step = music.Value == "4" ? -1 : 1;
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Adjust, music.Id, step));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                settingsMenu = RequireMenu(snapshot, "settings.root");
                Check(settingsMenu.Rows.Single(row => row.Id == music.Id).Value != music.Value,
                    "native Settings changes the existing draft setting through its typed row");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Back));
                session.PollMobileUi();
                session.HandleSettingsInput();
                Check(session.state.Mode == GameMode.MainMenu && session.settings.Music == originalMusic,
                    "Back returns to Main Menu without applying the draft setting");

                session.OpenScreen(UiScreen.Load);
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot loadMenu = RequireMenu(snapshot, "load");
                Check(loadMenu.Rows.Count > 0
                    && GameText.Current.Language == LocalizationCatalog.BelarusianLanguage
                    && loadMenu.Title == GameText.Get("messages.260")
                    && snapshot.Labels.Back == "Вярнуцца",
                    "Belarusian Load title and Back label use the active catalog");
                MobileMenuRow slot = loadMenu.Rows.First(row => row.Kind == MobileMenuRowKind.Slot);
                SaveSlotInfo firstSlot = session.persistenceSlots[0];
                string firstSlotName = firstSlot.Location?.Text ?? GameText.Decode(firstSlot.Metadata.PrimaryName.Span);
                Check(firstSlotName.Length > 0 && slot.Label == firstSlotName.Trim().ToUpperInvariant(),
                    "native Load labels preserve the legacy uppercase save-location format");
                string savePath = session.PersistenceStore.GetSlotPath(0);
                byte[] saveBefore = File.ReadAllBytes(savePath);
                ulong oldContext = snapshot.ContextId;
                channel.SubmitUiCommand(new(oldContext, MobileUiAction.Activate, slot.Id));
                channel.SubmitUiCommand(new(oldContext, MobileUiAction.Activate, slot.Id));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuModal loadModal = snapshot.Menu?.Modal
                    ?? throw new InvalidOperationException("Belarusian Load omitted its confirmation modal.");
                Check(loadModal.Kind == MobileMenuModalKind.Confirmation
                    && loadModal.Title == GameText.Get("messages.263")
                    && loadModal.ConfirmLabel == GameText.Get("messages.264")
                    && loadModal.CancelLabel == GameText.Get("messages.265"),
                    "the Belarusian Load confirmation title and choices come from stable catalog keys");
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                Check(snapshot.Menu?.Modal?.Kind == MobileMenuModalKind.Confirmation
                    && !session.persistenceMenu.PendingOperation,
                    "a queued second tap from the old context cannot confirm the Load");
                channel.SubmitUiCommand(new(oldContext, MobileUiAction.ConfirmYes));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                Check(snapshot.Menu?.Modal?.Kind == MobileMenuModalKind.Confirmation
                    && !session.persistenceMenu.PendingOperation,
                    "a stale ConfirmYes cannot begin a Load");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.ConfirmNo));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                Check(snapshot.Menu?.Modal is null && session.state.Mode == GameMode.Load
                    && !session.persistenceMenu.PendingOperation && File.ReadAllBytes(savePath).AsSpan().SequenceEqual(saveBefore),
                    "current ConfirmNo dismisses the modal without loading or changing save data");
                idleRevision = snapshot.Revision;
                session.PollMobileUi();
                Check(CopyMobileSnapshot(channel).Revision == idleRevision,
                    "the idle Load panel keeps its revision stable");

                Check(session.CloseScreen(), "the Load probe returns to Main Menu");
                session.PollMobileUi();
                session.OpenScreen(UiScreen.Save);
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuSnapshot saveMenu = RequireMenu(snapshot, "save");
                Check(saveMenu.Title == GameText.Get("messages.362")
                    && GameText.Current.Language == LocalizationCatalog.BelarusianLanguage,
                    "Belarusian Save title comes from the active catalog");
                MobileMenuRow newSlot = saveMenu.Rows.Single(row => row.Id == "slot.new");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.Activate, newSlot.Id));
                session.PollMobileUi();
                snapshot = CopyMobileSnapshot(channel);
                MobileMenuModal saveModal = snapshot.Menu?.Modal
                    ?? throw new InvalidOperationException("Belarusian Save omitted its confirmation modal.");
                Check(saveModal.Kind == MobileMenuModalKind.Confirmation
                    && saveModal.Title == GameText.Get("messages.312")
                    && saveModal.ConfirmLabel == GameText.Get("messages.313")
                    && saveModal.CancelLabel == GameText.Get("messages.314"),
                    "the Belarusian Save confirmation title and choices come from stable catalog keys");
                channel.SubmitUiCommand(new(snapshot.ContextId, MobileUiAction.ConfirmNo));
                session.PollMobileUi();
                Check(CopyMobileSnapshot(channel).Menu?.Modal is null && !session.persistenceMenu.PendingOperation,
                    "current ConfirmNo dismisses the Save modal without writing a save");

                return "Mobile menu session PASS: profile validation/rejection/cancel, live RU/BE settings labels, localized Save/Load titles and confirmations, stale Load activation/confirmation, and stable idle revisions. Evidence: " + output;
            }
            finally
            {
                try { session.DisposeProgression(); }
                finally
                {
                    try { session.DisposeInputRouter(); }
                    finally { session.StopAudio(); }
                }
            }
        }
        finally
        {
            GameText.Use(previous);
        }

        static MobileUiSnapshot CopyMobileSnapshot(IMobileUiChannel channel)
        {
            int required = channel.CopyUiSnapshot(Span<byte>.Empty);
            byte[] json = new byte[required];
            if (channel.CopyUiSnapshot(json) != required)
            {
                throw new InvalidOperationException("Mobile UI snapshot changed while the probe copied it.");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return JsonSerializer.Deserialize<MobileUiSnapshot>(json, options)
                ?? throw new InvalidDataException("Mobile UI snapshot JSON was empty.");
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Mobile menu session: " + message);
        }

        static MobileMenuSnapshot RequireMenu(MobileUiSnapshot snapshot, string scene)
        {
            if (snapshot.Menu is not { } menu || menu.Scene != scene)
            {
                throw new InvalidOperationException($"Mobile menu session: expected menu scene {scene}.");
            }

            return menu;
        }
    }

    /// <summary>Builds the Core-owned description used by native Settings, Save/Load, and Profiles panels.</summary>
    private MobileMenuSnapshot? BuildMobileMenu()
    {
        if (mobileProfilesOpen)
        {
            if (mobileProfileEditor is not null)
            {
                var request = BuildMobileTextRequest();
                string key = $"profileName|{mobileProfileRequestId}|{mobileProfileEditor.Text}|{mobileProfileEditor.Error}|{mobileProfileNotice}|{MobileFooterHint()}";
                if (cachedMobileMenu is not null && cachedMobileMenuKey == key)
                {
                    return cachedMobileMenu;
                }

                cachedMobileMenuKey = key;
                cachedMobileMenuSource = mobileProfileEditor;
                return cachedMobileMenu = new(
                    "profileName",
                    request!.Title,
                    Array.Empty<MobileMenuRow>(),
                    mobileProfileNotice ?? mobileProfileEditor.Error,
                    FooterHint: MobileFooterHint());
            }

            return BuildMobileProfilesMenu();
        }

        ControlSettingsMenu? settingsMenu = mobileSettingsMenu
            ?? (state.Mode == GameMode.Settings ? modernSettings : null);
        if (settingsMenu is not null && !settingsMenu.Closed
            && (ReferenceEquals(settingsMenu, mobileSettingsMenu) || state.Mode == GameMode.Settings))
        {
            return BuildMobileSettingsMenu(settingsMenu);
        }

        if (state.Mode is GameMode.Save or GameMode.Load)
        {
            return BuildMobilePersistenceMenu();
        }

        return null;
    }

    /// <summary>Provides native name entry only for an active Core profile editor.</summary>
    private MobileTextRequest? BuildMobileTextRequest()
    {
        if (!mobileProfilesOpen || mobileProfileEditor is null || mobileProfileRequestId is null)
        {
            cachedMobileTextRequest = null;
            return null;
        }

        var request = new MobileTextRequest(
            mobileProfileRequestId,
            GameText.Get("ui.profiles.nameTitle"),
            mobileProfileEditor.Text,
            "",
            GameText.Get("ui.profiles.accept"),
            GameText.Get("ui.profiles.cancel"));
        if (cachedMobileTextRequest == request)
        {
            return cachedMobileTextRequest;
        }

        return cachedMobileTextRequest = request;
    }

    private MobileMenuSnapshot BuildMobileSettingsMenu(ControlSettingsMenu menu)
    {
        ControlSettingsMenuView view = menu.GetView();
        string footer = MobileFooterHint();
        string? autoScrollRowId = menu.AutoScrollRowId;
        if (autoScrollRowId is not null && controls.CurrentInputSource.Kind is InputSourceKind.Keyboard or InputSourceKind.Gamepad)
        {
            mobileNativeMenuTouched = false;
        }
        if (mobileNativeMenuTouched || controls.CurrentInputSource.Kind is not (InputSourceKind.Keyboard or InputSourceKind.Gamepad))
        {
            autoScrollRowId = null;
        }
        string key = $"settings|{view.Scene}|{view.Title}|{view.Selection}|{view.Scroll}|{view.Notice}|{view.Capturing}|{view.CaptureRowId}|{footer}|{autoScrollRowId}";
        if (ReferenceEquals(cachedMobileMenuSource, view) && cachedMobileMenuKey == key)
        {
            return cachedMobileMenu!;
        }

        var rows = new MobileMenuRow[view.Rows.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = view.Rows[i];
            MobileMenuRowKind kind = row.Id.StartsWith("settings.binding.", StringComparison.Ordinal)
                || row.Id.StartsWith("settings.move.", StringComparison.Ordinal)
                ? MobileMenuRowKind.Binding
                : row.CanAdjust ? MobileMenuRowKind.Selector : MobileMenuRowKind.Action;
            rows[i] = new(row.Id, row.Label, row.Value, kind, row.CanActivate || row.CanAdjust,
                i == view.Selection);
        }

        cachedMobileMenuSource = view;
        cachedMobileMenuKey = key;
        return cachedMobileMenu = new(view.Scene, view.Title, Array.AsReadOnly(rows),
            view.Notice, FooterHint: footer, AutoScrollRowId: autoScrollRowId,
            Capturing: view.Capturing, CaptureRowId: view.CaptureRowId);
    }

    private MobileMenuSnapshot BuildMobilePersistenceMenu()
    {
        bool busy = persistenceMenu.Countdown > 0 || persistenceMenu.PendingOperation;
        int selected = persistenceMenu.Selection;
        string? selectedId = (uint)selected < (uint)persistenceSlots.Length ? SaveSlotId(persistenceSlots[selected]) : null;
        if (!ReferenceEquals(mobilePersistenceCatalogSource, persistenceSlots))
        {
            mobilePersistenceCatalogSource = persistenceSlots;
            mobilePersistenceCatalogGeneration = mobilePersistenceCatalogGeneration == long.MaxValue
                ? 1 : mobilePersistenceCatalogGeneration + 1;
            mobileLastPersistenceSelection = -1;
        }

        if (selected != mobileLastPersistenceSelection)
        {
            if (mobileLastPersistenceSelection >= 0
                && controls.CurrentInputSource.Kind is InputSourceKind.Keyboard or InputSourceKind.Gamepad)
            {
                mobileNativeMenuTouched = false;
            }

            mobileLastPersistenceSelection = selected;
        }

        string? autoScrollRowId = !mobileNativeMenuTouched
            && controls.CurrentInputSource.Kind is InputSourceKind.Keyboard or InputSourceKind.Gamepad
            ? selectedId : null;
        MobileMenuModal? modal = null;
        string scene;
        if (persistenceMenu.Failure is { } failure)
        {
            scene = $"{(persistenceMenu.Kind == SaveLoadMenuKind.Save ? "save" : "load")}.notice.failure";
            modal = new(MobileMenuModalKind.Notice,
                GameText.Get("ui.persistence.errorTitle"),
                GameText.Format("ui.persistence.failed", ("message", failure)),
                GameText.Get("ui.controls.back"), "");
        }
        else if (persistenceMenu.EmptyNotice)
        {
            scene = $"{(persistenceMenu.Kind == SaveLoadMenuKind.Save ? "save" : "load")}.notice.empty";
            modal = new(MobileMenuModalKind.Notice, MobileLegacyMessage(267), "",
                GameText.Get("ui.controls.back"), "");
        }
        else if (persistenceMenu.Confirmation && selectedId is not null)
        {
            scene = $"{(persistenceMenu.Kind == SaveLoadMenuKind.Save ? "save" : "load")}.confirm.{selectedId}";
            int first = persistenceMenu.Kind == SaveLoadMenuKind.Save ? 312 : 263;
            string details = selected >= 0 && selected < persistenceSlots.Length
                ? PersistenceRowDetails(persistenceSlots[selected])
                : "";
            modal = new(MobileMenuModalKind.Confirmation,
                MobileLegacyMessage(first),
                details,
                MobileLegacyMessage(first + 1),
                MobileLegacyMessage(first + 2),
                persistenceMenu.ConfirmationChoice == 0);
        }
        else
        {
            scene = persistenceMenu.Kind == SaveLoadMenuKind.Save ? "save" : "load";
        }

        string footer = MobileFooterHint();
        string contextKey = "catalog." + mobilePersistenceCatalogGeneration.ToString();
        string key = $"{scene}|{selected}|{busy}|{persistenceMenu.ConfirmationChoice}|{persistenceMenu.EmptyNotice}|{persistenceMenu.Failure}|{footer}|{GameText.Current.Language}|{contextKey}|{autoScrollRowId}";
        if (ReferenceEquals(cachedMobileMenuSource, persistenceSlots) && cachedMobileMenuKey == key)
        {
            return cachedMobileMenu!;
        }

        var rows = new MobileMenuRow[persistenceSlots.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var slot = persistenceSlots[i];
            string label = slot.IsNew ? GameText.Get("ui.saves.new").Trim().ToUpperInvariant() : PersistenceSlotLabel(slot);
            string value = PersistenceRowDetails(slot);
            bool enabled = !busy && !persistenceMenu.Confirmation && !persistenceMenu.EmptyNotice && persistenceMenu.Failure is null;
            MobileMenuBadgeKind badge = slot.IsNew
                ? MobileMenuBadgeKind.None
                : slot.Automatic ? MobileMenuBadgeKind.Automatic : MobileMenuBadgeKind.Manual;
            string? badgeLabel = badge switch
            {
                MobileMenuBadgeKind.Manual => GameText.Get("ui.saves.manual"),
                MobileMenuBadgeKind.Automatic => GameText.Get("ui.saves.automatic"),
                _ => null
            };
            rows[i] = new(SaveSlotId(slot), label, value, MobileMenuRowKind.Slot, enabled, i == selected,
                Badge: badge, BadgeLabel: badgeLabel);
        }

        string title = MobileLegacyMessage(persistenceMenu.Kind == SaveLoadMenuKind.Save ? 362 : 260);
        cachedMobileMenuSource = persistenceSlots;
        cachedMobileMenuKey = key;
        return cachedMobileMenu = new(scene, title, Array.AsReadOnly(rows), Modal: modal, FooterHint: footer,
            AutoScrollRowId: autoScrollRowId, ContextKey: contextKey);
    }

    private MobileMenuSnapshot BuildMobileProfilesMenu()
    {
        IReadOnlyList<PlayerProfile> profiles = playerProfiles.Profiles;
        if (!ReferenceEquals(mobileProfileCatalogSource, profiles))
        {
            mobileProfileCatalogSource = profiles;
            mobileProfileCatalogGeneration = mobileProfileCatalogGeneration == long.MaxValue
                ? 1 : mobileProfileCatalogGeneration + 1;
        }

        if (mobileProfileSelection < 0 || mobileProfileSelection > profiles.Count)
        {
            mobileProfileSelection = Math.Clamp(mobileProfileSelection, 0, profiles.Count);
        }

        string footer = MobileFooterHint();
        string activeId = playerProfiles.ActiveId;
        string? autoScroll = mobileProfileAutoScrollRowId;
        if (autoScroll is not null && controls.CurrentInputSource.Kind is InputSourceKind.Keyboard or InputSourceKind.Gamepad)
        {
            mobileNativeMenuTouched = false;
        }
        if (mobileNativeMenuTouched || controls.CurrentInputSource.Kind is not (InputSourceKind.Keyboard or InputSourceKind.Gamepad))
        {
            autoScroll = null;
        }

        string contextKey = "profiles." + mobileProfileCatalogGeneration.ToString();
        string key = $"profiles|{activeId}|{mobileProfileSelection}|{mobileProfileNotice}|{footer}|{autoScroll}|{GameText.Current.Language}|{contextKey}";
        if (ReferenceEquals(cachedMobileMenuSource, profiles) && cachedMobileMenuKey == key)
        {
            return cachedMobileMenu!;
        }

        var rows = new MobileMenuRow[profiles.Count + 1];
        rows[0] = new("profiles.create", GameText.Get("ui.profiles.create"), "", MobileMenuRowKind.Action,
            CanChangeProfile, mobileProfileSelection == 0);
        for (int i = 0; i < profiles.Count; i++)
        {
            PlayerProfile profile = profiles[i];
            bool active = profile.Id == activeId;
            var rename = new MobileMenuOption("profile.rename." + profile.Id, GameText.Get("ui.profiles.rename"), false);
            rows[i + 1] = new("profile." + profile.Id, profile.Name,
                active ? GameText.Get("ui.profiles.current") : "", MobileMenuRowKind.Profile,
                CanChangeProfile, i + 1 == mobileProfileSelection, Array.AsReadOnly([rename]));
        }

        cachedMobileMenuSource = profiles;
        cachedMobileMenuKey = key;
        return cachedMobileMenu = new("profiles", GameText.Get("ui.profiles.title"), Array.AsReadOnly(rows),
            mobileProfileNotice, FooterHint: footer, AutoScrollRowId: autoScroll, ContextKey: contextKey);
    }

    private string MobileFooterHint() => controls.CurrentInputSource.Kind == InputSourceKind.Touch
        ? GameText.Get("ui.mobile.menuTouchHelp")
        : GameText.Format("ui.controls.acceptBackDynamic",
            ("confirm", controls.Hint(0, GameAction.Confirm)),
            ("back", controls.Hint(0, GameAction.Back)));

    private string PersistenceSlotLabel(SaveSlotInfo slot)
    {
        if (slot.Status is SaveSlotStatus.Invalid or SaveSlotStatus.Unreadable)
        {
            return GameText.Get("ui.saves.readFailed").Trim().ToUpperInvariant();
        }

        if (slot.Metadata.Present == 0)
        {
            return MobileLegacyMessage(361).Trim().ToUpperInvariant();
        }

        return (slot.Location?.Text ?? GameText.Decode(slot.Metadata.PrimaryName.Span)).Trim().ToUpperInvariant();
    }

    private string PersistenceRowDetails(SaveSlotInfo slot)
    {
        if (slot.IsNew || slot.Metadata.Present == 0)
        {
            return "";
        }

        var date = slot.Date;
        string dateText = date.Year > 0 ? $"{date.Year:0000}.{date.Month:00}.{date.Day:00}" : "";
        string levelLabel = MobileLegacyMessage(310);
        string level = slot.Metadata.ActorZeroLevel > 0 ? $"{levelLabel} {slot.Metadata.ActorZeroLevel}" : "";
        return string.Join(" · ", new[] { dateText, level }.Where(value => value.Length > 0));
    }

    private static string SaveSlotId(SaveSlotInfo slot) => slot.IsNew
        ? "slot.new"
        : slot.Automatic ? "slot.auto." + (-slot.Index).ToString() : "slot.manual." + slot.Index.ToString();

    private static string MobileLegacyMessage(int index) =>
        GameText.Get("messages." + index.ToString("D3", CultureInfo.InvariantCulture));

    /// <summary>Dispatches typed native menu actions through the existing Core controllers.</summary>
    private bool DispatchMobileMenu(MobileUiCommand command)
    {
        if (window is not IMobileUiChannel channel || channel.UiContextId != command.ContextId
            || !mobileMenuCommandGuard.Allows(command.ContextId))
        {
            return false;
        }

        MobileMenuSnapshot? beforeMenu = BuildMobileMenu();
        MobileTextRequest? beforeText = BuildMobileTextRequest();
        string beforeIdentity = MobileCommandIdentity(beforeMenu, beforeText);
        bool applied;
        if (mobileProfilesOpen)
        {
            applied = DispatchMobileProfiles(command);
        }
        else if (command.Action == MobileUiAction.OpenProfiles && CanChangeProfile
            && state.Mode == GameMode.MainMenu)
        {
            OpenMobileProfiles();
            applied = true;
        }
        else
        {
            ControlSettingsMenu? settingsMenu = mobileSettingsMenu
                ?? (state.Mode == GameMode.Settings ? modernSettings : null);
            if (settingsMenu is not null && !settingsMenu.Closed
                && (ReferenceEquals(settingsMenu, mobileSettingsMenu) || state.Mode == GameMode.Settings))
            {
                applied = DispatchMobileSettings(settingsMenu, command);
            }
            else if (state.Mode is GameMode.Save or GameMode.Load)
            {
                applied = DispatchMobilePersistence(command);
            }
            else
            {
                applied = false;
            }
        }

        if (applied)
        {
            mobileNativeMenuTouched = true;
            mobileProfileAutoScrollRowId = null;
            ControlSettingsMenu? activeSettings = mobileSettingsMenu
                ?? (state.Mode == GameMode.Settings ? modernSettings : null);
            activeSettings?.ClearAutoScrollRowId();
            MobileMenuSnapshot? afterMenu = BuildMobileMenu();
            MobileTextRequest? afterText = BuildMobileTextRequest();
            string afterIdentity = MobileCommandIdentity(afterMenu, afterText);
            if (!StringComparer.Ordinal.Equals(beforeIdentity, afterIdentity))
            {
                mobileMenuCommandGuard.Invalidate(command.ContextId);
            }
        }

        return applied;
    }

    private bool DispatchMobileSettings(ControlSettingsMenu menu, MobileUiCommand command)
    {
        if (menu.Closed) return false;
        string? target = command.TargetId;
        bool applied = command.Action switch
        {
            MobileUiAction.Select when target is not null => menu.SelectRow(target),
            MobileUiAction.Activate when target is not null => menu.ActivateRow(target),
            MobileUiAction.Adjust when target is not null && (command.Value is -1 or 1) => menu.AdjustRow(target, command.Value),
            MobileUiAction.Back => menu.Back(),
            _ => false
        };
        if (applied) menu.ClearAutoScrollRowId();
        return applied;
    }

    private bool DispatchMobilePersistence(MobileUiCommand command)
    {
        if (persistenceMenu.PendingOperation || persistenceMenu.Countdown > 0)
        {
            return false;
        }

        bool hasNotice = persistenceMenu.Failure is not null || persistenceMenu.EmptyNotice;
        bool activeConfirmation = persistenceMenu.Confirmation;
        if (command.Action == MobileUiAction.DismissNotice)
        {
            if (persistenceMenu.Failure is not null)
            {
                persistenceMenu.AcknowledgeFailure();
                return true;
            }

            if (persistenceMenu.EmptyNotice)
            {
                ApplyPersistenceInput(new(Accept: true));
                return true;
            }

            return false;
        }

        if (command.Action == MobileUiAction.ConfirmYes)
        {
            if (!activeConfirmation || hasNotice) return false;
            persistenceMenu.PointerConfirm(true);
            ApplyPersistenceInput(new(Accept: true));
            return true;
        }

        if (command.Action == MobileUiAction.ConfirmNo)
        {
            if (!activeConfirmation || hasNotice) return false;
            persistenceMenu.PointerConfirm(false);
            ApplyPersistenceInput(new(Cancel: true));
            return true;
        }

        if (activeConfirmation || hasNotice)
        {
            if (command.Action != MobileUiAction.Back) return false;
            if (persistenceMenu.Failure is not null)
            {
                persistenceMenu.AcknowledgeFailure();
            }
            else
            {
                ApplyPersistenceInput(new(Cancel: true));
            }

            return true;
        }

        if (command.Action == MobileUiAction.Select)
        {
            if (command.TargetId is null) return false;
            int index = FindPersistenceSlot(command.TargetId);
            bool selected = persistenceMenu.Select(index);
            if (selected) mobileLastPersistenceSelection = index;
            return selected;
        }

        if (command.Action == MobileUiAction.Activate)
        {
            if (command.TargetId is null) return false;
            int index = FindPersistenceSlot(command.TargetId);
            if (!persistenceMenu.Select(index)) return false;
            mobileLastPersistenceSelection = index;
            if (persistenceMenu.Kind == SaveLoadMenuKind.Load
                && (uint)index < (uint)persistenceSlots.Length
                && persistenceSlots[index].Status is SaveSlotStatus.Invalid or SaveSlotStatus.Unreadable)
            {
                Sound(3);
                persistenceMenu.ReportFailure(persistenceSlots[index].Failure ?? GameText.Get("ui.saves.readFailed"));
                return true;
            }

            ApplyPersistenceInput(new(Accept: true));
            return true;
        }

        if (command.Action == MobileUiAction.Back)
        {
            ApplyPersistenceInput(new(Cancel: true));
            return true;
        }

        return false;
    }

    private bool DispatchMobileProfiles(MobileUiCommand command)
    {
        if (!CanChangeProfile)
        {
            CloseMobileProfiles();
            return false;
        }

        if (mobileProfileEditor is not null)
        {
            if (command.Action == MobileUiAction.Back || command.Action == MobileUiAction.CancelText)
            {
                if (command.Action == MobileUiAction.CancelText && command.TargetId != mobileProfileRequestId) return false;
                CancelMobileProfileName();
                return true;
            }

            if (command.Action != MobileUiAction.SubmitText || command.TargetId != mobileProfileRequestId) return false;
            if (command.InvalidText)
            {
                mobileProfileEditor.Reject(GameText.Get("ui.profiles.invalidName"));
                mobileProfileNotice = null;
                return true;
            }

            SubmitMobileProfileName(command.Text ?? "");
            return true;
        }

        if (command.Action == MobileUiAction.Back)
        {
            CloseMobileProfiles();
            return true;
        }

        if (command.Action == MobileUiAction.Select)
        {
            if (command.TargetId is null) return false;
            int index = FindMobileProfileRow(command.TargetId);
            if (index < 0) return false;
            mobileProfileSelection = index;
            mobileProfileAutoScrollRowId = null;
            return true;
        }

        if (command.Action != MobileUiAction.Activate || command.TargetId is null) return false;
        if (command.TargetId == "profiles.create")
        {
            StartMobileProfileNameEditor(null);
            return true;
        }

        const string renamePrefix = "profile.rename.";
        if (command.TargetId.StartsWith(renamePrefix, StringComparison.Ordinal))
        {
            string id = command.TargetId[renamePrefix.Length..];
            PlayerProfile? profile = playerProfiles.Profiles.FirstOrDefault(value => value.Id == id);
            if (profile is null) return false;
            mobileProfileSelection = playerProfiles.Profiles.ToList().FindIndex(value => value.Id == id) + 1;
            StartMobileProfileNameEditor(profile.Id);
            return true;
        }

        if (command.TargetId.StartsWith("profile.", StringComparison.Ordinal))
        {
            string id = command.TargetId["profile.".Length..];
            PlayerProfile? profile = playerProfiles.Profiles.FirstOrDefault(value => value.Id == id);
            if (profile is null) return false;
            try
            {
                SwitchPlayerProfile(profile);
                if (mobileCreatedProfileAwaitingSwitch?.Id == profile.Id)
                {
                    mobileCreatedProfileAwaitingSwitch = null;
                }
                CloseMobileProfiles();
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                mobileProfileNotice = GameText.Get("ui.profiles.failed");
                Console.WriteLine("Player profile operation failed: " + error);
                return true;
            }
        }

        return false;
    }

    private void ApplyPersistenceInput(SaveLoadMenuInput input)
    {
        persistenceMenu.Input(input, persistenceMetadata, HandlePersistence);
        persistenceSelection = persistenceMenu.Selection;
    }

    private int FindPersistenceSlot(string targetId)
    {
        for (int i = 0; i < persistenceSlots.Length; i++)
        {
            if (SaveSlotId(persistenceSlots[i]) == targetId) return i;
        }

        return -1;
    }

    private int FindMobileProfileRow(string targetId)
    {
        if (targetId == "profiles.create") return 0;
        if (!targetId.StartsWith("profile.", StringComparison.Ordinal)) return -1;
        string id = targetId["profile.".Length..];
        for (int i = 0; i < playerProfiles.Profiles.Count; i++)
        {
            if (playerProfiles.Profiles[i].Id == id) return i + 1;
        }

        return -1;
    }

    private void OpenMobileProfiles()
    {
        if (!CanChangeProfile) return;
        mobileProfilesOpen = true;
        mobileProfileEditor = null;
        mobileProfileTargetId = mobileProfileRequestId = null;
        mobileProfileNotice = null;
        mobileProfileSelection = playerProfiles.Profiles.ToList().FindIndex(profile => profile.Id == playerProfiles.ActiveId) + 1;
        controls.Clear();
    }

    private void CloseMobileProfiles()
    {
        mobileProfilesOpen = false;
        mobileProfileEditor = null;
        mobileProfileTargetId = mobileProfileRequestId = mobileProfileNotice = null;
        mobileProfileSelection = 0;
        controls.Clear();
    }

    private void StartMobileProfileNameEditor(string? profileId)
    {
        if (!CanChangeProfile) return;
        PlayerProfile? profile = profileId is null ? null : playerProfiles.Profiles.FirstOrDefault(value => value.Id == profileId);
        if (profileId is not null && profile is null) return;
        string name = profile?.Name ?? playerProfiles.NextName(DefaultProfileName);
        mobileCreatingProfile = profile is null;
        mobileProfileTargetId = profile?.Id;
        mobileProfileEditor = new(name);
        mobileProfileRequestId = "profile.name." + (++mobileProfileRequestSequence).ToString();
        mobileProfileNotice = null;
        cachedMobileTextRequest = null;
        _ = window.TakeTextInput();
    }

    private void CancelMobileProfileName()
    {
        mobileProfileEditor = null;
        mobileCreatingProfile = false;
        mobileProfileTargetId = mobileProfileRequestId = null;
        mobileProfileNotice = mobileCreatedProfileAwaitingSwitch is null ? null : GameText.Get("ui.profiles.failed");
        cachedMobileTextRequest = null;
        _ = window.TakeTextInput();
    }

    private void SubmitMobileProfileName(string text)
    {
        ProfileNameEditor editor = mobileProfileEditor!;
        editor.ReplaceText(text);
        editor.Accept();
        if (!editor.Closed || editor.Result is not { } name) return;

        try
        {
            if (mobileCreatingProfile)
            {
                mobileCreatedProfileAwaitingSwitch = playerProfiles.Create(name);
                mobileCreatingProfile = false;
                mobileProfileTargetId = mobileCreatedProfileAwaitingSwitch.Id;
            }

            if (mobileCreatedProfileAwaitingSwitch?.Id == mobileProfileTargetId)
            {
                playerProfiles.Rename(mobileProfileTargetId!, name);
                PlayerProfile created = playerProfiles.Profiles.Single(profile => profile.Id == mobileProfileTargetId);
                SwitchPlayerProfile(created);
                CloseMobileProfiles();
            }
            else
            {
                PlayerProfile target = playerProfiles.Profiles.Single(profile => profile.Id == mobileProfileTargetId);
                playerProfiles.Rename(target.Id, name);
                mobileProfileEditor = null;
                mobileProfileTargetId = mobileProfileRequestId = mobileProfileNotice = null;
                cachedMobileTextRequest = null;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            editor.Reject(GameText.Get("ui.profiles.failed"));
            mobileProfileNotice = null;
            Console.WriteLine("Player profile operation failed: " + error);
        }
    }

    private bool HandleMobileProfilesInput()
    {
        if (!mobileProfilesOpen) return false;
        if (!CanChangeProfile)
        {
            CloseMobileProfiles();
            return false;
        }

        if (mobileProfileEditor is not null)
        {
            if (controls.MenuInput.Menu(MenuAction.Back))
            {
                CancelMobileProfileName();
            }
            return true;
        }

        int previousSelection = mobileProfileSelection;
        bool moved = false;
        if (controls.MenuInput.Menu(MenuAction.Up, true))
        {
            mobileProfileSelection = Math.Max(0, mobileProfileSelection - 1);
            moved = true;
        }
        else if (controls.MenuInput.Menu(MenuAction.Down, true))
        {
            mobileProfileSelection = Math.Min(playerProfiles.Profiles.Count, mobileProfileSelection + 1);
            moved = true;
        }

        if (moved && mobileProfileSelection != previousSelection
            && controls.CurrentInputSource.Kind is InputSourceKind.Keyboard or InputSourceKind.Gamepad)
        {
            mobileNativeMenuTouched = false;
            mobileProfileAutoScrollRowId = mobileProfileSelection == 0
                ? "profiles.create"
                : "profile." + playerProfiles.Profiles[mobileProfileSelection - 1].Id;
        }

        if (controls.Take(0x3d) || controls.PadButtonPressed(2))
        {
            if (mobileProfileSelection > 0)
            {
                StartMobileProfileNameEditor(playerProfiles.Profiles[mobileProfileSelection - 1].Id);
            }
        }
        else if (controls.MenuInput.Menu(MenuAction.Confirm))
        {
            var rows = BuildMobileMenu()?.Rows;
            if (rows is not null && (uint)mobileProfileSelection < (uint)rows.Count)
            {
                _ = DispatchMobileProfiles(new(0, MobileUiAction.Activate, rows[mobileProfileSelection].Id));
            }
        }
        else if (controls.MenuInput.Menu(MenuAction.Back))
        {
            CloseMobileProfiles();
        }

        return true;
    }

    private static string MobileCommandIdentity(MobileMenuSnapshot? menu, MobileTextRequest? textRequest)
    {
        if (menu is null && textRequest is null) return "none";
        string rows = menu is null ? "" : string.Join("\u001f", menu.Rows.Select(row => row.Id));
        string modal = menu?.Modal is { } value ? value.Kind.ToString() : "none";
        return $"{menu?.Scene}|{modal}|{textRequest?.Id}|{rows}|{menu?.Capturing}|{menu?.CaptureRowId}";
    }

}
