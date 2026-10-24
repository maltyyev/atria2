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
    private readonly List<ControlRow> rows = [];
    private enum Page
    {
        Root, Player, Bindings, Moves
    }
    private Page page;
    private string title = "Настройки", notice = "";
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
        Action<ControlProfile, NativeSettingsState> apply
    )
    {
        this.input = input;
        this.legacy = legacy;
        this.catalog = catalog;
        panel = new(tiles, font);
        this.apply = apply;
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
                notice = "Назначение отменено";
                input.Clear();
                return;
            }

            if (input.Take(14))
            {
                capture[column] = default;
                capture = null;
                notice = "Назначение удалено";
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
            notice = swapped ? "Конфликт: назначения обменены" : "Назначено: " + b;
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
            notice = "Не удалось сохранить настройки. Проверьте доступ к папке и свободное место.";
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
        title = "Настройки";
        Add("Музыка", Audio.Music.ToString(), adjust: d => Audio.Set((byte)Math.Clamp(Audio.Music + d, 0, 4), Audio.Effects, Audio.ThirdOption));
        Add(
            "Звуковые эффекты",
            Audio.Effects.ToString(),
            adjust: d => Audio.Set(Audio.Music, (byte)Math.Clamp(Audio.Effects + d, 0, 4), Audio.ThirdOption)
        );
        Add(
            "Количество игроков",
            Audio.PlayerCount.ToString(),
            adjust: d => Audio.Set(Audio.Music, Audio.Effects, (byte)Math.Clamp(Audio.PlayerCount + d, 1, 2))
        );
        for (int i = 0; i < 2; i++)
        {
            int j = i;
            Add("Управление игрока " + (j + 1), "Открыть", () =>
            {
                player = j;
                Go(Page.Player);
            });
        }

        Add("Подсказки", Draft.Hints ? "Вкл" : "Выкл", () => Draft.Hints = !Draft.Hints);
        Add("Мёртвая зона стиков", Draft.DeadZone.ToString(), adjust: d => Draft.DeadZone = Math.Clamp(Draft.DeadZone + d * 1000, 2000, 20000));
        Add(
            "Порог триггеров",
            Draft.TriggerThreshold.ToString(),
            adjust: d => Draft.TriggerThreshold = Math.Clamp(Draft.TriggerThreshold + d * 10, 10, 240)
        );
        Add("Вибрация", Draft.Vibration ? "Вкл" : "Выкл", () => Draft.Vibration = !Draft.Vibration);
        Add("Автосохранений", Draft.AutosaveLimit.ToString(), adjust: d => Draft.AutosaveLimit = Math.Clamp(Draft.AutosaveLimit + d, 1, 5));
        Add("Применить и вернуться", "", ApplyChanges);
        Add("Сбросить управление", "Modern", () =>
        {
            int limit = Draft.AutosaveLimit;
            Draft = ControlProfile.Defaults(legacy);
            Draft.AutosaveLimit = limit;
            notice = "Сброшено. Нажмите Применить для сохранения.";
        });
        Add("Отменить изменения", "", () => Closed = true);
    }

    private void BuildPlayerRows()
    {
        var p = Draft.Players[player];

        title = "Управление: игрок " + (player + 1);
        Add("Клавиатура и мышь", p.Keyboard ? "Вкл" : "Выкл", () => p.Keyboard = !p.Keyboard);
        Add("Контроллер", p.PadSlot < 0 ? "Нет" : "Xbox " + (p.PadSlot + 1) + (input.Connected(p.PadSlot) ? " подключён" : " не подключён"), adjust: d =>
        {
            int next = Math.Clamp(p.PadSlot + d, -1, 3);
            if (next >= 0 && Draft.Players[1 - player].PadSlot == next)
            {
                Draft.Players[1 - player].PadSlot = -1;
            }

            p.PadSlot = next;
        });
        Add("Профиль", p.Preset, () =>
        {
            int slot = p.PadSlot;
            bool keyboard = p.Keyboard;
            Draft.ResetPlayer(player, !p.Classic, legacy);
            Draft.Players[player].PadSlot = slot;
            Draft.Players[player].Keyboard = keyboard;
            notice = "Профиль сброшен; доступно переназначение.";
        });
        Add("Кнопки и действия", "Открыть", () => Go(Page.Bindings));
        Add("Герой для приёмов", MoveCatalog.Names[party], adjust: d => party = (party + d + 6) % 6);
        Add("Назначить отдельные приёмы", "Открыть", () => Go(Page.Moves));
        Add("Назад", "", () => Go(Page.Root));
    }

    private void BuildBindingsRows()
    {
        var p = Draft.Players[player];

        title = $"Игрок {player + 1}: " + (column == 0 ? "основная кнопка" : column == 1 ? "дополнительная кнопка" : "Xbox");
        for (int i = 0; i < (int)GameAction.Count; i++)
        {
            int a = i;
            var row = p.Bindings[i];
            Add(ControlNames.Action((GameAction)i), (GameAction)i is >= GameAction.Quick1 and <= GameAction.Quick4 ? InputRouter.QuickBindingHint(row[column]) : row[column].ToString(), () =>
            {
                capture = row;
                captureId = null;
                notice = "Нажмите кнопку; Esc: отмена; Backspace: удалить";
                input.Clear();
            }, d => column = (column + d + 3) % 3);
        }

    }

    private void BuildMovesRows()
    {
        var p = Draft.Players[player];

        title = MoveCatalog.Names[party] + ": " + (column == 0 ? "основная" : column == 1 ? "дополнительная" : "Xbox");
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
                notice = "Назначить: кнопка; Esc: отмена; Backspace: удалить";
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
            "Влево / вправо: значение или столбец назначения",
            capture is null ? notice : "Нажмите кнопку. Esc: отмена; Backspace: удалить"
        );
    }
}
