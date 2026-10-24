using System.Runtime.InteropServices;
using Atria.Game.Menu;
using Atria.Game.State;
using Atria.Game.Termination;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly QuitController quit = new();
    private readonly DefeatController defeat = new();
    private readonly EndingController ending = new();
    private BitmapFont? terminationLargeFont;
    private ushort[]? defeatBackground, endingBackground;
    private EndingText? endingText;
    private void InitializeQuitDialog() => quit.Initialize();
    private void EnterQuitConfirmation()
    { /*42a850 already ran before429260; no extra native entry state.*/
    }

    private void HandleQuitInput() => quit.Input(key => Query(key switch
    {
        QuitKey.Right => bindings.Next,
        QuitKey.Left => bindings.Previous,
        QuitKey.Cancel => bindings.Cancel,
        QuitKey.Confirm => bindings.Confirm,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    }), effect =>
    {
        switch (effect.Kind)
        {
            case QuitEffectKind.Sound:
                Sound(effect.Value);
                break;
            case QuitEffectKind.ReturnMenuRetained:
                state.Mode = GameMode.MainMenu;
                break; //No stack pop/entry reset in42aaa0.
            case QuitEffectKind.RequestWindowClose:
                musicTransport.Stop(); //435760/435740 stop and close native CD stream before WM_CLOSE.
                if (TerminationNative.PostMessageW(window.Handle, 0x10, 0, 0) == 0)
                {
                    throw new IOException("Cannot post game window close.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect));
        }
    });
    private void ComposeQuit(Span<ushort> target)
    {
        MainMenuComposer.Draw(menu.AdvanceLogicalFrame(), menuBackground, menuSprites, fireAnimation, fireSprites, target, menuModal.Draw);
        terminationLargeFont ??= BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false);
        TerminationRenderer.DrawQuit(quit, etc, terminationLargeFont, displayMessages, target);
    }

    private ushort[] LoadTerminationBackground(ReadOnlySpan<byte> name)
    {
        var bitmap = BmpImage.Decode(Read("AT2BMP.000", name));
        if (bitmap.Width != 640 || bitmap.Height != 480)
        {
            throw new InvalidDataException("Invalid termination background.");
        }

        ushort[] pixels = bitmap.ToRgb565();
        return pixels;
    }

    private void EnterDefeat()
    {
        musicId = 27;
        defeatBackground ??= LoadTerminationBackground("GAMEOVER"u8);
        music.Update(musicId);
    }

    private void HandleDefeatInput() => defeat.Input(() => Query(bindings.Confirm), () =>
    {
        //421910->403630. Native core subset retains uninitialized/opaque bytes by design.
        InitialGameState.ResetRecoveredCore(state, items, messages);
        areas.ResetNames(messages);
        ResetShopStocks();
        ResetExplorationSession();
        actions.CloseAll();
        previousTown = 0;
        saveDisabled = true;
        ReturnTerminationMenu();
    });
    private void ComposeDefeat(Span<ushort> target)
    {
        (defeatBackground ?? throw new InvalidOperationException("Game-over background missing.")).CopyTo(target);
        defeat.AdvanceFrame();
    }

    private void EnterEnding()
    {
        endingText = EndingText.Parse(File.ReadAllBytes(Path.Combine(root, "END.DAT")), state.EndingSelection);
        musicId = 27;
        ending.Enter(state.EndingSelection, HandleEndingEffect);
        //4211d0 writes all16000 raw bytes at464020; darkness indices118..120 alias its first three DWORDs.
        endingText[0][..12].CopyTo(dungeonEndingTextPrefix);
        gate.SetThresholdMilliseconds(25);
        music.Update(musicId);
    }

    private void HandleEndingInput() => ending.Input(HandleEndingEffect);
    private void HandleEndingEffect(EndingEffect effect)
    {
        switch (effect.Kind)
        {
            case EndingEffectKind.LoadBackground:
                endingBackground = LoadTerminationBackground(System.Text.Encoding.ASCII.GetBytes($"END{effect.Value:00}"));
                break;
            case EndingEffectKind.ReturnMainMenu:
                gate.SetThresholdMilliseconds(20);
                saveDisabled = true;
                ReturnTerminationMenu();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect));
        }
    }

    private void ComposeEnding(Span<ushort> target)
    {
        (endingBackground ?? throw new InvalidOperationException("Ending background missing.")).CopyTo(target);
        TerminationRenderer.DrawEnding(endingText ?? throw new InvalidOperationException("Ending text missing."), ending.AdvanceFrame(), smallFont, target);
    }

    private void ReturnTerminationMenu()
    {
        menuSpriteActive = true;
        menu.ReturnFromTermination();
        modes.ResetDepth();
        var transition = modes.Push(GameMode.MainMenu);
        state.Mode = GameMode.MainMenu;
        state.ModeStackDepth = transition.Depth;
        //Native200 entry preserves musicId27; no invented restart of title track1.
        music.Update(musicId);
    }

    private static class TerminationNative
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern int PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    }
}
