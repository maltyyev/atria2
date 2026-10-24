namespace Atria.Game.Settings;

public enum SettingsKey
{
    Left,
    Right,
    Up,
    Down,
    Cancel,
    Confirm
}

public enum SettingsEffectKind
{
    Sound,
    ApplyMusicVolume,
    Close,
    ReleaseSprites,
    UiDirty
}

public readonly record struct SettingsEffect(SettingsEffectKind Kind, int Value = 0);
public readonly record struct SettingsSprite(int Frame, int X, int Y, bool Half = false);
/// <summary>Native mode250: 425fd0/426020/426060/426110/4261a0/426210/426300.</summary>
public sealed class SettingsController(NativeSettingsState state)
{
    public int Column
    {
        get; private set;
    }
    public int Music
    {
        get; private set;
    }
    public int Effects
    {
        get; private set;
    }
    public int ThirdOption
    {
        get; private set;
    }
    public int FlashDirection
    {
        get; private set;
    }
    public int FlashCounter
    {
        get; private set;
    }

    public void Enter()
    {
        Column = 0;
        Music = state.Music;
        Effects = state.Effects;
        ThirdOption = state.ThirdOption;
        FlashDirection = FlashCounter = 0;
    }

    public void Input(Func<SettingsKey, bool> query, Action<SettingsEffect> emit)
    {
        if (query(SettingsKey.Left))
        {
            if (Column > 0)
            {
                emit(new(SettingsEffectKind.Sound, 0));
                Column--;
            }

            return;
        }

        if (query(SettingsKey.Right))
        {
            if (Column < 2)
            {
                emit(new(SettingsEffectKind.Sound, 0));
                Column++;
            }

            return;
        }

        bool up = query(SettingsKey.Up), down = !up && query(SettingsKey.Down);
        if (Column < 2)
        {
            int old = Column == 0 ? Music : Effects;
            int next = up && old < 4 ? old + 1 : down && old > 0 ? old - 1 : old;
            if (next != old)
            {
                if (Column == 0)
                {
                    emit(new(SettingsEffectKind.Sound, 5));
                }

                if (Column == 0)
                {
                    Music = next;
                }
                else
                {
                    Effects = next;
                }

                FlashDirection = up ? 1 : 2;
                FlashCounter = 3;
                state.Set((byte)Music, (byte)Effects, state.ThirdOption);
                if (Column == 0)
                {
                    emit(new(SettingsEffectKind.ApplyMusicVolume, Music));
                }
                else
                {
                    emit(new(SettingsEffectKind.Sound, 5));
                }
            }
        }
        else
        {
            int next = up ? 1 : down ? 2 : ThirdOption;
            if (next != ThirdOption)
            {
                emit(new(SettingsEffectKind.Sound, 5));
                ThirdOption = next;
            }
        }

        if (query(SettingsKey.Cancel))
        {
            emit(new(SettingsEffectKind.Sound, 4));
            Close(emit);
            return;
        }

        if (query(SettingsKey.Confirm))
        {
            emit(new(SettingsEffectKind.Sound, 3));
            emit(new(SettingsEffectKind.ReleaseSprites));
            state.Set((byte)Music, (byte)Effects, (byte)ThirdOption);
            Close(emit);
        }
    }

    private static void Close(Action<SettingsEffect> emit)
    {
        emit(new(SettingsEffectKind.Close));
        emit(new(SettingsEffectKind.UiDirty, 1));
    }

    // Exactly one call per logical render opportunity. GPU redraws must reuse the returned commands.
    public SettingsSprite[] AdvanceFrame()
    {
        List<SettingsSprite> result = [new(Column, new[] { 110, 195, 354 }[Column], 354)];
        if (Column < 2 && FlashDirection != 0)
        {
            int x = Column == 0 ? (FlashDirection == 1 ? 110 : 156) : (FlashDirection == 1 ? 196 : 242);
            result.Add(new(FlashDirection == 1 ? 3 : 4, x, 394, FlashCounter < 1));
            if (--FlashCounter < 0)
            {
                FlashCounter = 0;
                FlashDirection = 0;
            }
        }

        int[] ys = [313, 290, 267, 233];
        for (int i = 0; i < Music; i++)
        {
            result.Add(new(9 + i, 132, ys[i]));
        }

        for (int i = 0; i < Effects; i++)
        {
            result.Add(new(13 + i, 204, ys[i]));
        }

        result.Add(new(17, 370, 233));
        if (ThirdOption == 1)
        {
            result.Add(new(7, 440, 354));
        }
        else
        {
            result.Add(new(18, 442, 234));
            result.Add(new(8, 440, 398));
        }

        return result.ToArray();
    }
}
