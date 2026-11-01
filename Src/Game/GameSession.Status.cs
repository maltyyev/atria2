using Atria.Game.Navigation;
using Atria.Game.Status;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly StatusMenuRuntime statusRuntime = new();
    private readonly StatusRenderClock statusClock = new();
    private StatusMenuRenderer? statusRenderer;
    private QuestJournalCatalog? statusJournal;
    private byte[] statusEventDescriptions = [];
    private ushort[]? statusBackground;
    private bool statusAlternateJournalHeading = false; //46fcd8: startup0; later option mutation not recovered.
    private void EnterStatus()
    {
        var bitmap = BmpImage.Decode(Read("AT2BMP.000", "map.bmp"u8));
        if (bitmap.Width != 640 || bitmap.Height != 480)
        {
            throw new InvalidDataException("Status map.bmp must be640x480.");
        }

        var background = bitmap.ToRgb565();
        var item = SpriteFile.Decode(Read("AT2SPR.000", "item.spr"u8));
        var portraits = SpriteFile.Decode(Read("AT2SPR.000", "charinfo.spr"u8));
        var additional = SpriteFile.Decode(File.ReadAllBytes(Path.Combine(root, "..", "interface", "EirenFaces.spr")));
        portraits = portraits with
        {
            Frames = [.. portraits.Frames, additional.Frames[3]]
        };
        var renderer = new StatusMenuRenderer(
            item,
            etc,
            portraits,
            smallFont,
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng.fnt")), false),
            items,
            displayMessages,
            File.ReadAllBytes(Path.Combine(root, "CHARINTR.DAT"))
        );
        var journal = QuestJournalCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "DANGEL.DAT")));
        byte[] eventDescriptions = File.ReadAllBytes(Path.Combine(root, "EITEM.DAT"));
        statusBackground = background;
        statusRenderer = renderer;
        statusJournal = journal;
        statusEventDescriptions = eventDescriptions;
        statusClock.Reset();
        statusRuntime.Enter(HandleStatus);
    }

    private void HandleStatus(StatusMenuCommand command)
    {
        switch (command.Kind)
        {
            case StatusMenuEffect.Sound:
                Sound(command.Value);
                break;
            case StatusMenuEffect.SelectActor:
                selectedEquipmentActor = command.Value;
                break;
            case StatusMenuEffect.ReleaseSprites:
                statusRenderer = null;
                break;
            case StatusMenuEffect.PopMode:
                CloseScreen();
                break;
            case StatusMenuEffect.SetRestoreFlag:
                // Native473154 requests background restoration. This session fully composes each logical draw;
                // invalidate the previously presented frame until that fresh composition is available.
                hasFrame = false;
                break;
            case StatusMenuEffect.Recompose:
            case StatusMenuEffect.MarkDirty:
                hasFrame = false;
                break; // DrawPlan reconstructs the static canvas over a fresh background.
            case StatusMenuEffect.BeginPage:
                break; // Runtime has already initialized exact native page state.
            default:
                throw new NotSupportedException($"Unknown status operation {command.Kind}.");
        }
    }

    private void HandleStatusInput()
    {
        var journal = statusJournal ?? throw new InvalidOperationException("Status journal catalog absent.");
        if (statusRuntime.Menu.InputDelay != 0)
        {
            statusRuntime.Input(default, state, items, statusEventDescriptions, journal, HandleStatus);
            return;
        }

        bool cancel = Query(bindings.Cancel), accept = false, up = false, down = false, left = false, right = false, cycle = false;
        if (!cancel)
        {
            accept = Query(bindings.Confirm);
        }

        if (!cancel && !accept)
        {
            up = Query(bindings.Up);
        }

        if (!cancel && !accept && !up)
        {
            down = Query(bindings.Down);
        }

        if (!cancel && !accept && !up && !down)
        {
            left = Query(bindings.Previous);
        }

        if (!cancel && !accept && !up && !down && !left)
        {
            right = Query(bindings.Next);
        }

        if (!cancel && !accept && !up && !down && !left && !right)
        {
            cycle = Query(bindings[0, 7]);
            if (!cycle)
            {
                cycle = Query(0x0f);
            }
        }

        statusRuntime.Input(new(cancel, accept, up, down, left, right, cycle), state, items, statusEventDescriptions, journal, HandleStatus);
    }

    private void ComposeStatus(Span<ushort> target)
    {
        (statusBackground ?? throw new InvalidOperationException("Status background absent.")).CopyTo(target);
        var renderer = statusRenderer ?? throw new InvalidOperationException("Status sprites have been released.");
        var frame = statusClock.AdvanceLogicalFrame(statusRuntime);
        renderer.DrawPlan(renderer.BuildPlan(statusRuntime, state, frame, statusJournal, statusAlternateJournalHeading), frame, target);
    }
}
