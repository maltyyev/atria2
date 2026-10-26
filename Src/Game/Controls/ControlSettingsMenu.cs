using Atria.Game.Settings;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Controls;

public sealed class ControlSettingsMenu
{
    private readonly InputRouter input;
    private readonly ControlBindings legacy;
    private readonly MoveCatalog catalog;
    private readonly ControlPanel panel;
    private readonly Action<ControlProfile, NativeSettingsState> apply;
    private readonly Action<string>? changeLanguage;
    private readonly List<ControlRow> rows = [];
    private enum Page
    {
        Root, Player, Bindings, Moves
    }
    private Page page;
    private string title = GameText.Get("ui.settings.title"), notice = "";
    private int player, column, party;
    private InputBinding[]? capture;
    private string? captureId;
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

    private void Add(string label, string value, Action? action = null, Action<int>? adjust = null) => rows.Add(new(label, value, action, adjust));
    public void Update()
    {
        if (capture is not null)
        {
            if (input.Take(1))
            {
                capture = null;
                notice = GameText.Get("ui.settings.cancelled");
                input.Clear();
                return;
            }

            if (input.Take(14))
            {
                capture[column] = default;
                capture = null;
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
            notice = swapped ? GameText.Get("ui.settings.swapped") : GameText.Format("ui.settings.assigned", ("binding", b));
            input.Clear();
            return;
        }

        BuildRows();
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
        if (changeLanguage is not null)
        {
            Add(GameText.Get("ui.settings.language"), GameText.Get("ui.languages." + GameText.Current.Language),
                ToggleLanguage, _ => ToggleLanguage());
        }
        Add(GameText.Get("ui.settings.music"), Audio.Music.ToString(), adjust: d => Audio.Set((byte)Math.Clamp(Audio.Music + d, 0, 4), Audio.Effects, Audio.ThirdOption));
        Add(
            GameText.Get("ui.settings.effects"),
            Audio.Effects.ToString(),
            adjust: d => Audio.Set(Audio.Music, (byte)Math.Clamp(Audio.Effects + d, 0, 4), Audio.ThirdOption)
        );
        Add(
            GameText.Get("ui.settings.players"),
            Audio.PlayerCount.ToString(),
            adjust: d => Audio.Set(Audio.Music, Audio.Effects, (byte)Math.Clamp(Audio.PlayerCount + d, 1, 2))
        );
        for (int i = 0; i < 2; i++)
        {
            int j = i;
            Add(GameText.Format("ui.settings.playerControls", ("player", j + 1)), GameText.Get("ui.common.open"), () =>
            {
                player = j;
                Go(Page.Player);
            });
        }

        Add(GameText.Get("ui.settings.hints"), Draft.Hints ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => Draft.Hints = !Draft.Hints);
        Add(GameText.Get("ui.settings.deadZone"), Draft.DeadZone.ToString(), adjust: d => Draft.DeadZone = Math.Clamp(Draft.DeadZone + d * 1000, 2000, 20000));
        Add(
            GameText.Get("ui.settings.triggerThreshold"),
            Draft.TriggerThreshold.ToString(),
            adjust: d => Draft.TriggerThreshold = Math.Clamp(Draft.TriggerThreshold + d * 10, 10, 240)
        );
        Add(GameText.Get("ui.settings.vibration"), Draft.Vibration ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => Draft.Vibration = !Draft.Vibration);
        Add(GameText.Get("ui.settings.autosaveCount"), Draft.AutosaveLimit.ToString(), adjust: d => Draft.AutosaveLimit = Math.Clamp(Draft.AutosaveLimit + d, 1, 5));
        Add(GameText.Get("ui.settings.apply"), "", ApplyChanges);
        Add(GameText.Get("ui.settings.reset"), GameText.Get("ui.profiles.Modern"), () =>
        {
            int limit = Draft.AutosaveLimit;
            Draft = ControlProfile.Defaults(legacy);
            Draft.AutosaveLimit = limit;
            notice = GameText.Get("ui.settings.resetNotice");
        });
        Add(GameText.Get("ui.settings.cancel"), "", () => Closed = true);
    }

    private void ToggleLanguage()
    {
        try
        {
            string next = GameText.Current.Language == LocalizationCatalog.BaseLanguage
                ? LocalizationCatalog.BelarusianLanguage : LocalizationCatalog.BaseLanguage;
            changeLanguage!(next);
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
        Add(GameText.Get("ui.settings.keyboardMouse"), p.Keyboard ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => p.Keyboard = !p.Keyboard);
        Add(GameText.Get("ui.settings.controller"), p.PadSlot < 0 ? GameText.Get("ui.common.none") : GameText.Format(input.Connected(p.PadSlot) ? "ui.settings.controllerConnected" : "ui.settings.controllerDisconnected", ("slot", p.PadSlot + 1)), adjust: d =>
        {
            int next = Math.Clamp(p.PadSlot + d, -1, 3);
            if (next >= 0 && Draft.Players[1 - player].PadSlot == next)
            {
                Draft.Players[1 - player].PadSlot = -1;
            }

            p.PadSlot = next;
        });
        Add(GameText.Get("ui.settings.profile"), GameText.Get("ui.profiles." + (p.Preset is "Modern" or "Classic" ? p.Preset : "Custom")), () =>
        {
            int slot = p.PadSlot;
            bool keyboard = p.Keyboard;
            Draft.ResetPlayer(player, !p.Classic, legacy);
            Draft.Players[player].PadSlot = slot;
            Draft.Players[player].Keyboard = keyboard;
            notice = GameText.Get("ui.settings.profileReset");
        });
        Add(GameText.Get("ui.settings.bindings"), GameText.Get("ui.common.open"), () => Go(Page.Bindings));
        Add(GameText.Get("ui.settings.moveHero"), MoveCatalog.Name(party), adjust: d => party = (party + d + 6) % 6);
        Add(GameText.Get("ui.settings.moveBindings"), GameText.Get("ui.common.open"), () => Go(Page.Moves));
        Add(GameText.Get("ui.controls.back"), "", () => Go(Page.Root));
    }

    private void BuildBindingsRows()
    {
        var p = Draft.Players[player];

        title = GameText.Format("ui.settings.bindingTitle", ("player", player + 1), ("column", column == 0 ? GameText.Get("ui.settings.primaryButton") : column == 1 ? GameText.Get("ui.settings.secondaryButton") : GameText.Get("ui.settings.xbox")));
        for (int i = 0; i < (int)GameAction.Count; i++)
        {
            int a = i;
            var row = p.Bindings[i];
            Add(ControlNames.Action((GameAction)i), (GameAction)i is >= GameAction.Quick1 and <= GameAction.Quick4 ? InputRouter.QuickBindingHint(row[column]) : row[column].ToString(), () =>
            {
                capture = row;
                captureId = null;
                notice = GameText.Get("ui.settings.captureHint");
                input.Clear();
            }, d => column = (column + d + 3) % 3);
        }

    }

    private void BuildMovesRows()
    {
        var p = Draft.Players[player];

        title = GameText.Format("ui.settings.moveBindingTitle", ("character", MoveCatalog.Name(party)), ("column", column == 0 ? GameText.Get("ui.settings.primary") : column == 1 ? GameText.Get("ui.settings.secondary") : GameText.Get("ui.settings.xbox")));
        foreach (var move in catalog.All(party))
        {
            string key = party + "/" + move.Id;
            if (!p.Direct.TryGetValue(key, out var row))
            {
                row = new InputBinding[3];
                p.Direct.Add(key, row);
            }

            Add(move.Name, row[column].ToString(), () =>
            {
                capture = row;
                captureId = key;
                notice = GameText.Get("ui.settings.assignHint");
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
            GameText.Get("ui.settings.columnHint"),
            capture is null ? notice : GameText.Get("ui.settings.capturePrompt")
        );
    }
}
