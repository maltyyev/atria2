using Atria.Core.Game.Progression;
using Atria.Core.Contracts.Input;
using Atria.Core.Game.Settings;
using Atria.Core.Persistence.Settings;
using Atria.Core.Presentation.Settings;
using Atria.Core.Presentation.Sprites;
using Atria.Core.Resources;
using Atria.Core.Resources.Codecs;
using Atria.Core.Resources.Localization;
using Atria.Core.Runtime.Input;

namespace Atria.Core.Presentation.Controls;

public sealed record ControlSettingsMenuRow(string Id, string Label, string Value, bool CanActivate, bool CanAdjust);

public sealed record ControlSettingsMenuView(
    string Scene,
    string Title,
    IReadOnlyList<ControlSettingsMenuRow> Rows,
    int Selection,
    int Scroll,
    string Notice,
    bool Capturing,
    string? CaptureRowId,
    bool Closed
);

public sealed class ControlSettingsMenu
{
    private readonly InputRouter input;
    private readonly ControlBindings legacy;
    private readonly MoveCatalog catalog;
    private readonly ControlPanel panel;
    private readonly Action<ControlProfile, NativeSettingsState> apply;
    private readonly Action<string>? changeLanguage;
    private readonly List<ControlRow> rows = [];
    private ControlSettingsMenuView? cachedView;
    private enum Page
    {
        Root, Player, Bindings, Moves
    }
    private Page page;
    private string title = GameText.Get("ui.settings.title"), notice = "";
    private int player, column, party;
    private InputBinding[]? capture;
    private string? captureId;
    private string? captureRowId;
    private string? pendingAutoScrollRowId;
    public ControlProfile Draft
    {
        get; private set;
    }
    public NativeSettingsState Audio
    {
        get;
    }
    public bool Closed
    {
        get; private set;
    }

    public string? AutoScrollRowId => pendingAutoScrollRowId;
    public void ClearAutoScrollRowId() => pendingAutoScrollRowId = null;

    public bool Back()
    {
        if (capture is not null) return CancelCapture();
        if (Closed) return false;
        if (page == Page.Root)
        {
            Closed = true;
            input.Clear();
        }
        else
        {
            Go(page is Page.Bindings or Page.Moves ? Page.Player : Page.Root);
        }

        return true;
    }

    public ControlSettingsMenu(
        InputRouter input,
        ControlBindings legacy,
        MoveCatalog catalog,
        SpriteFile tiles,
        BitmapFont font,
        NativeSettingsState audio,
        Action<ControlProfile, NativeSettingsState> apply,
        Action<string>? changeLanguage = null
    )
    {
        this.input = input;
        this.legacy = legacy;
        this.catalog = catalog;
        panel = new(tiles, font);
        this.apply = apply;
        this.changeLanguage = changeLanguage;
        Draft = input.Profile.Clone();
        Audio = NativeSettingsState.Parse(audio.Encode());
    }

    private void Go(Page value)
    {
        page = value;
        panel.Reset();
        notice = "";
        input.Clear();
    }

    private void Add(string id, string label, string value, Action? action = null, Action<int>? adjust = null) => rows.Add(new(label, value, action, adjust, Id: id));

    public ControlSettingsMenuView GetView()
    {
        BuildRows();
        string scene = page switch
        {
            Page.Root => "settings.root",
            Page.Player => $"settings.player.{player}",
            Page.Bindings => $"settings.bindings.{player}.{column}",
            Page.Moves => $"settings.moves.{player}.{party}.{column}",
            _ => throw new InvalidOperationException("Unknown settings page.")
        };
        bool same = cachedView is { } previous
            && previous.Scene == scene
            && previous.Title == title
            && previous.Selection == panel.Selection
            && previous.Scroll == panel.Scroll
            && previous.Notice == notice
            && previous.Capturing == (capture is not null)
            && previous.CaptureRowId == captureRowId
            && previous.Closed == Closed
            && previous.Rows.Count == rows.Count;
        if (same)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var old = cachedView!.Rows[i];
                var current = rows[i];
                if (old.Id != current.Id || old.Label != current.Label || old.Value != current.Value
                    || old.CanActivate != (current.Activate is not null) || old.CanAdjust != (current.Adjust is not null))
                {
                    same = false;
                    break;
                }
            }
        }

        if (same) return cachedView!;
        var viewRows = rows.Select(row => new ControlSettingsMenuRow(
            row.Id, row.Label, row.Value, row.Activate is not null, row.Adjust is not null)).ToArray();
        return cachedView = new(scene, title, Array.AsReadOnly(viewRows), panel.Selection, panel.Scroll, notice,
            capture is not null, captureRowId, Closed);
    }

    public bool ActivateRow(string id)
    {
        if (Closed || capture is not null) return false;
        BuildRows();
        return panel.ActivateRow(rows, id);
    }

    public bool SelectRow(string id)
    {
        if (Closed || capture is not null) return false;
        BuildRows();
        return panel.SelectRow(rows, id);
    }

    public bool AdjustRow(string id, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (Closed || capture is not null) return false;
        BuildRows();
        return panel.AdjustRow(rows, id, direction);
    }

    public bool CancelCapture()
    {
        if (capture is null) return false;
        capture = null;
        captureId = captureRowId = null;
        notice = GameText.Get("ui.settings.cancelled");
        input.Clear();
        return true;
    }

    public bool RemoveCapture()
    {
        if (capture is null) return false;
        capture[column] = default;
        capture = null;
        captureId = captureRowId = null;
        notice = GameText.Get("ui.settings.removed");
        return true;
    }

    public void Update()
    {
        if (capture is not null)
        {
            if (input.Take(1))
            {
                capture = null;
                captureRowId = null;
                notice = GameText.Get("ui.settings.cancelled");
                input.Clear();
                return;
            }

            if (input.Take(14))
            {
                capture[column] = default;
                capture = null;
                captureRowId = null;
                notice = GameText.Get("ui.settings.removed");
                return;
            }

            var b = input.Capture(player, column == 2 ? InputKind.Pad : InputKind.Key, Draft.Players[player].PadSlot);
            if (b.Kind == InputKind.None)
            {
                return;
            }

            if (column == 2 && b.Kind != InputKind.Pad)
            {
                return;
            }

            if (column != 2 && b.Kind == InputKind.Pad)
            {
                return;
            }
            // Swap conflicts only within the same context. Direct move keys are combat actions.
            var p = Draft.Players[player];
            GameAction selected = captureId is null ? (GameAction)panel.Selection : GameAction.Attack;
            InputBinding previous = capture[column];
            bool swapped = false;
            for (int a = 0; a < (int)GameAction.Count; a++)
            {
                if (ControlProfile.SameContext(selected, (GameAction)a))
                {
                    for (int c = 0; c < 3; c++)
                    {
                        if (!ReferenceEquals(capture, p.Bindings[a]) && p.Bindings[a][c] == b)
                        {
                            p.Bindings[a][c] = previous;
                            swapped = true;
                        }
                    }
                }
            }

            if (!ControlProfile.IsMenu(selected))
            {
                foreach (var row in p.Direct.Values)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        if (!ReferenceEquals(row, capture) && row[c] == b)
                        {
                            row[c] = previous;
                            swapped = true;
                        }
                    }
                }
            }

            capture[column] = b;
            p.Preset = "Custom";
            capture = null;
            captureRowId = null;
            notice = swapped ? GameText.Get("ui.settings.swapped") : GameText.Format("ui.settings.assigned", ("binding", b));
            input.Clear();
            return;
        }

        BuildRows();
        int previousSelection = panel.Selection;
        Page previousPage = page;
        if (panel.Input(input, rows))
        {
            if (page == Page.Root)
            {
                Closed = true;
                input.Clear();
            }
            else
            {
                Go(page is Page.Bindings or Page.Moves ? Page.Player : Page.Root);
            }
        }

        if (page == previousPage && panel.Selection != previousSelection
            && input.CurrentInputSource.Kind is InputSourceKind.Keyboard or InputSourceKind.Gamepad
            && (uint)panel.Selection < (uint)rows.Count)
        {
            pendingAutoScrollRowId = rows[panel.Selection].Id;
        }
    }

    private void ApplyChanges()
    {
        try
        {
            apply(Draft, Audio);
            Closed = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            notice = GameText.Get("ui.settings.saveFailed");
            Console.WriteLine("Settings were not applied: " + error.Message);
        }
    }

    private void BuildRows()
    {
        rows.Clear();
        switch (page)
        {
            case Page.Root:
                BuildRootRows();
                break;
            case Page.Player:
                BuildPlayerRows();
                break;
            case Page.Bindings:
                BuildBindingsRows();
                break;
            case Page.Moves:
                BuildMovesRows();
                break;
        }
    }

    private void BuildRootRows()
    {
        title = GameText.Get("ui.settings.title");
        if (changeLanguage is not null && GameText.Current.AvailableLanguages.Count > 1)
        {
            string languageLabel = GameText.Current.TryGet("ui.languages." + GameText.Current.Language, out var label) ? label : GameText.Current.Language;
            Add("settings.root.language", GameText.Get("ui.settings.language"), languageLabel,
                ToggleLanguage, _ => ToggleLanguage());
        }
        Add("settings.root.music", GameText.Get("ui.settings.music"), Audio.Music.ToString(), adjust: d => Audio.Set((byte)Math.Clamp(Audio.Music + d, 0, 4), Audio.Effects, Audio.ThirdOption));
        Add(
            "settings.root.effects",
            GameText.Get("ui.settings.effects"),
            Audio.Effects.ToString(),
            adjust: d => Audio.Set(Audio.Music, (byte)Math.Clamp(Audio.Effects + d, 0, 4), Audio.ThirdOption)
        );
        Add(
            "settings.root.players",
            GameText.Get("ui.settings.players"),
            Audio.PlayerCount.ToString(),
            adjust: d => Audio.Set(Audio.Music, Audio.Effects, (byte)Math.Clamp(Audio.PlayerCount + d, 1, 2))
        );
        for (int i = 0; i < 2; i++)
        {
            int j = i;
            Add($"settings.root.playerControls.{j}", GameText.Format("ui.settings.playerControls", ("player", j + 1)), GameText.Get("ui.common.open"), () =>
            {
                player = j;
                Go(Page.Player);
            });
        }

        Add("settings.root.hints", GameText.Get("ui.settings.hints"), Draft.Hints ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => Draft.Hints = !Draft.Hints);
        Add("settings.root.deadZone", GameText.Get("ui.settings.deadZone"), Draft.DeadZone.ToString(), adjust: d => Draft.DeadZone = Math.Clamp(Draft.DeadZone + d * 1000, 2000, 20000));
        Add(
            "settings.root.triggerThreshold",
            GameText.Get("ui.settings.triggerThreshold"),
            Draft.TriggerThreshold.ToString(),
            adjust: d => Draft.TriggerThreshold = Math.Clamp(Draft.TriggerThreshold + d * 10, 10, 240)
        );
        Add("settings.root.vibration", GameText.Get("ui.settings.vibration"), Draft.Vibration ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => Draft.Vibration = !Draft.Vibration);
        Add("settings.root.autosaveCount", GameText.Get("ui.settings.autosaveCount"), Draft.AutosaveLimit.ToString(), adjust: d => Draft.AutosaveLimit = Math.Clamp(Draft.AutosaveLimit + d, 1, 5));
        Add("settings.root.apply", GameText.Get("ui.settings.apply"), "", ApplyChanges);
        Add("settings.root.reset", GameText.Get("ui.settings.reset"), GameText.Get("ui.profiles.Modern"), () =>
        {
            int limit = Draft.AutosaveLimit;
            Draft = ControlProfile.Defaults(legacy);
            Draft.AutosaveLimit = limit;
            notice = GameText.Get("ui.settings.resetNotice");
        });
        Add("settings.root.cancel", GameText.Get("ui.settings.cancel"), "", () => Closed = true);
    }

    private void ToggleLanguage()
    {
        try
        {
            changeLanguage!(GameText.Current.NextLanguage());
            notice = GameText.Get("ui.settings.languageImmediate");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            notice = GameText.Get("ui.settings.saveFailed");
            Console.WriteLine("Language was not changed: " + error.Message);
        }
    }

    private void BuildPlayerRows()
    {
        var p = Draft.Players[player];

        title = GameText.Format("ui.settings.playerTitle", ("player", player + 1));
        Add("settings.player.keyboardMouse", GameText.Get("ui.settings.keyboardMouse"), p.Keyboard ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => p.Keyboard = !p.Keyboard);
        Add("settings.player.controller", GameText.Get("ui.settings.controller"), p.PadSlot < 0 ? GameText.Get("ui.common.none") : GameText.Format(input.Connected(p.PadSlot) ? "ui.settings.controllerConnected" : "ui.settings.controllerDisconnected", ("slot", p.PadSlot + 1)), adjust: d =>
        {
            int next = Math.Clamp(p.PadSlot + d, -1, 3);
            if (next >= 0 && Draft.Players[1 - player].PadSlot == next)
            {
                Draft.Players[1 - player].PadSlot = -1;
            }

            p.PadSlot = next;
        });
        Add("settings.player.profile", GameText.Get("ui.settings.profile"), GameText.Get("ui.profiles." + (p.Preset is "Modern" or "Classic" ? p.Preset : "Custom")), () =>
        {
            int slot = p.PadSlot;
            bool keyboard = p.Keyboard;
            Draft.ResetPlayer(player, !p.Classic, legacy);
            Draft.Players[player].PadSlot = slot;
            Draft.Players[player].Keyboard = keyboard;
            notice = GameText.Get("ui.settings.profileReset");
        });
        Add("settings.player.bindings", GameText.Get("ui.settings.bindings"), GameText.Get("ui.common.open"), () => Go(Page.Bindings));
        Add("settings.player.moveHero", GameText.Get("ui.settings.moveHero"), catalog.Heroes[party].Name, adjust: d => party = (party + d + catalog.Heroes.Count) % catalog.Heroes.Count);
        Add("settings.player.moveBindings", GameText.Get("ui.settings.moveBindings"), GameText.Get("ui.common.open"), () => Go(Page.Moves));
        Add("settings.player.back", GameText.Get("ui.controls.back"), "", () => Go(Page.Root));
    }

    private void BuildBindingsRows()
    {
        var p = Draft.Players[player];

        title = GameText.Format("ui.settings.bindingTitle", ("player", player + 1), ("column", column == 0 ? GameText.Get("ui.settings.primaryButton") : column == 1 ? GameText.Get("ui.settings.secondaryButton") : GameText.Get("ui.settings.xbox")));
        for (int i = 0; i < (int)GameAction.Count; i++)
        {
            int a = i;
            var row = p.Bindings[i];
            Add($"settings.binding.{(GameAction)i}", ControlNames.Action((GameAction)i), (GameAction)i is >= GameAction.Quick1 and <= GameAction.Quick4 ? InputRouter.QuickBindingHint(row[column]) : row[column].ToString(), () =>
            {
                capture = row;
                captureId = null;
                captureRowId = $"settings.binding.{(GameAction)a}";
                notice = CaptureHelp("ui.settings.captureHintDynamic");
                input.Clear();
            }, d => column = (column + d + 3) % 3);
        }

    }

    private void BuildMovesRows()
    {
        var p = Draft.Players[player];

        title = GameText.Format("ui.settings.moveBindingTitle", ("character", catalog.Heroes[party].Name), ("column", column == 0 ? GameText.Get("ui.settings.primary") : column == 1 ? GameText.Get("ui.settings.secondary") : GameText.Get("ui.settings.xbox")));
        foreach (var move in catalog.All(party))
        {
            string key = party + "/" + move.Id;
            if (!p.Direct.TryGetValue(key, out var row))
            {
                row = new InputBinding[3];
                p.Direct.Add(key, row);
            }

            Add("settings.move." + key, move.Name, row[column].ToString(), () =>
            {
                capture = row;
                captureId = key;
                captureRowId = "settings.move." + key;
                notice = CaptureHelp("ui.settings.assignHintDynamic");
                input.Clear();
            }, d => column = (column + d + 3) % 3);
        }

    }

    public void Draw(Span<ushort> pixels)
    {
        BuildRows();
        panel.Draw(
            pixels,
            title,
            rows,
            GameText.Format("ui.settings.columnHintDynamic", ("left", input.Hint(0, GameAction.MenuLeft)), ("right", input.Hint(0, GameAction.MenuRight))),
            capture is null ? notice : CaptureHelp("ui.settings.capturePromptDynamic")
        );
    }

    private string CaptureHelp(string key) => GameText.Format(key,
        ("target", GameText.Get(column == 2
            ? player == 0 ? "ui.settings.captureControllerTouch" : "ui.settings.captureController"
            : "ui.settings.captureKeyboardMouse")));
}
