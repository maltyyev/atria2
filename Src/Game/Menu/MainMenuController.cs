using Atria.Render.Scenes;

namespace Atria.Game.Menu;

public enum MenuEffect
{
    Sound,
    ReleaseMenuSprite,
    ChangeMode,
    TryReturnToPreviousMode,
    InitializeQuitDialog,
    StopAudio,
    PrepareMovieDisplay,
    PlayMovie,
    RestoreMovieDisplay,
    ResetGame,
    SetNewGameGlobals,
    LoadGroundMap,
    InitializeGroundMap,
    CopyInitialGroundLocation
}

public readonly record struct MenuCommand(MenuEffect Effect, int Value = 0, string? Resource = null);
public readonly record struct MenuInput(bool Next = false, bool Previous = false, bool Accept = false, bool Cancel = false);
public enum MenuVisualState
{
    Stable,
    Revealing,
    Closing,
    Modal = 9
}

// A draw snapshot is produced ONCE per native logical render call, then may be presented any number of times.
public sealed record MenuFrame(
    bool LoadResources,
    int Selection,
    int PanelItem,
    int PanelReveal,
    int ConfirmationPanelReveal,
    bool Confirmation,
    bool YesSelected,
    int ConfirmationFlash,
    int[] Flames
);
public sealed partial class MainMenuController
{
    private readonly int[] flames = MainMenuPresentation.InitialFlameFrames.ToArray();
    private bool initialize = true;
    private int revealCounter, flameCounter, flash;
    public int Selection
    {
        get; private set;
    }
    public int PanelItem
    {
        get; private set;
    }
    public int Reveal
    {
        get; private set;
    }
    public MenuVisualState VisualState
    {
        get; private set;
    }
    public bool Confirmation
    {
        get; private set;
    }
    public bool YesSelected
    {
        get; private set;
    }

    public void PointerConfirm(int x, int y)
    {
        int answer = MainMenuLayout.HitTestConfirmation(x * MainMenuLayout.Width / 640, y * MainMenuLayout.Height / 480);
        if (Confirmation && answer > 0)
        {
            YesSelected = answer == 1;
        }
    }

    public void PointerSelect(int item, bool saveDisabled)
    {
        if (Confirmation || item < 0 || item >= 6 || saveDisabled && item == 2 || Selection == item)
        {
            return;
        }

        Selection = PanelItem = item;
        VisualState = MenuVisualState.Stable;
        Reveal = 7;
    }

    // Host supplies the result of native 0x429290 before processing another input.
    public static void CompleteReturnToPreviousMode(bool succeeded, Action<MenuCommand> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if (!succeeded)
        {
            return;
        }

        emit(new(MenuEffect.Sound, 4));
        emit(new(MenuEffect.ReleaseMenuSprite));
    }

    // 0x4231f0 does not reset the reveal cursor/counter or confirmation flash.
    //4210f0/421910 only reset selection/panel and request sprite reload; retain animation state.
    public void ReturnFromTermination()
    {
        Selection = PanelItem = 0;
        initialize = true;
    }

    public void Enter()
    {
        Selection = PanelItem = 0;
        VisualState = MenuVisualState.Stable;
        MainMenuPresentation.InitialFlameFrames.CopyTo(flames);
        flameCounter = 0;
        Confirmation = YesSelected = false;
        initialize = true;
    }

    public void Input(MenuInput input, bool saveDisabled, Action<MenuCommand> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        void Sound(int id) => emit(new(MenuEffect.Sound, id));
        if (!Confirmation)
        {
            if (input.Next || input.Previous)
            {
                Selection = PanelItem = MainMenuLayout.Navigate(Selection, input.Next ? 1 : -1, saveDisabled);
                Sound(0);
                VisualState = MenuVisualState.Stable;
                Reveal = 7;
                return;
            }

            if (!input.Accept)
            {
                if (input.Cancel)
                {
                    emit(new(MenuEffect.TryReturnToPreviousMode));
                }

                return;
            }

            if (saveDisabled && Selection == MainMenuLayout.SaveItem)
                return;
            PanelItem = Selection;
            VisualState = MenuVisualState.Modal;
            Sound(3);
            if (PanelItem == 0)
            {
                Confirmation = YesSelected = true;
                return;
            }

            if (PanelItem == 3)
            {
                emit(new(MenuEffect.InitializeQuitDialog));
                VisualState = MenuVisualState.Stable;
                emit(new(MenuEffect.ChangeMode, 9999));
                return;
            }

            emit(new(MenuEffect.ReleaseMenuSprite));
            emit(new(MenuEffect.ChangeMode, PanelItem switch
            {
                1 => 230,
                2 => 220,
                4 => 240,
                5 => 250,
                _ => throw new InvalidOperationException()
            }));
            return;
        }

        // Modal priority is previous, next, accept, cancel, unlike ordinary navigation.
        if (input.Previous)
        {
            if (!YesSelected)
            {
                Sound(1);
                YesSelected = true;
                flash = 2;
            }

            return;
        }

        if (input.Next)
        {
            if (YesSelected)
            {
                Sound(1);
                YesSelected = false;
                flash = 2;
            }

            return;
        }

        if (!input.Accept && !input.Cancel)
        {
            return;
        }

        if (input.Accept && YesSelected)
        {
            emit(new(MenuEffect.ReleaseMenuSprite));
            emit(new(MenuEffect.StopAudio));
            emit(new(MenuEffect.PrepareMovieDisplay));
            emit(new(MenuEffect.PlayMovie, Resource: "jamie1.jad"));
            emit(new(MenuEffect.RestoreMovieDisplay));
            Confirmation = YesSelected = false;
            emit(new(MenuEffect.ResetGame));
            emit(new(MenuEffect.SetNewGameGlobals));
            emit(new(MenuEffect.LoadGroundMap, Resource: "ground1.amf"));
            emit(new(MenuEffect.InitializeGroundMap, 0));
            emit(new(MenuEffect.CopyInitialGroundLocation));
            emit(new(MenuEffect.ChangeMode, 400));
            return;
        }

        Sound(4);
        Selection = PanelItem = 0;
        VisualState = MenuVisualState.Stable;
        Confirmation = false;
    }

    // 0x423900. Host schedules calls with the recovered strict >20ms gate, never display refresh.
    public MenuFrame AdvanceLogicalFrame()
    {
        if (initialize)
        {
            initialize = false;
            MainMenuPresentation.InitialFlameFrames.CopyTo(flames);
            flameCounter = 0;
            VisualState = MenuVisualState.Revealing;
            return new(true, Selection, -1, -1, -1, false, YesSelected, flash, []);
        }

        int panel = -1, pose = -1;
        bool early = false;
        switch (VisualState)
        {
            case MenuVisualState.Stable:
                panel = PanelItem;
                pose = Reveal;
                break;
            case MenuVisualState.Revealing:
                if (++revealCounter > 1)
                {
                    if (++Reveal > 7)
                    {
                        Reveal = 7;
                        VisualState = MenuVisualState.Stable;
                    }

                    revealCounter = 1;
                }

                panel = PanelItem;
                pose = Reveal;
                break;
            case MenuVisualState.Closing:
                if (Reveal < 1)
                {
                    VisualState = MenuVisualState.Revealing;
                    PanelItem = Selection;
                    early = true;
                }
                else
                {
                    panel = PanelItem;
                    pose = Reveal - 1;
                    Reveal--;
                }

                break;
        }

        if (!early)
        {
            flameCounter++;
        }

        int[] drawnFlames = (int[])flames.Clone();
        if (flameCounter > 1)
        {
            for (int i = 0; i < flames.Length; i++)
            {
                if (++flames[i] > 10)
                {
                    flames[i] = 0;
                }
            }
        }

        int drawnFlash = flash;
        if (!early && Confirmation && flash != 0)
        {
            flash--;
        }

        if (!early && flameCounter > 1)
        {
            flameCounter = 0;
        }

        return new(false, Selection, panel, pose, !early && Confirmation ? Reveal : -1, !early && Confirmation, YesSelected, drawnFlash, drawnFlames);
    }
}
