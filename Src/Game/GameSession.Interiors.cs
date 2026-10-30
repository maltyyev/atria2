using Atria.Script;
using Atria.Game.Navigation;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly InteriorController interior = new();
    private readonly ShopRenderClock shopClock = new();
    private InteriorActionMenuAdapter? interiorActions;
    private TownSceneAssets? interiorAssets;
    private ShopSession? trade;
    private ShopRenderer? shopRenderer;
    private SpriteFile? itemSprites;
    private ShopStock[]? shopStocks;
    private InteriorActionMenuAdapter InteriorActions => interiorActions ??= new(actions, interior, state, displayMessages, () => scriptMember, choice => RunScript(choice.ScriptMember.Span, choice.Function.Span, ScenarioContinuation.InteriorInteraction), LeaveInterior, BeginTrade, () =>
    {
        itemSprites = null;
        shopRenderer = null;
    }, Sound);

    private void ResetShopStocks()
    {
        var catalog = ShopCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "SHOP.DAT")));
        shopStocks = Enumerable.Range(0, ShopCatalog.Count).Select(i => ShopStock.FromCatalog(catalog, i)).ToArray();
    }

    private void EnterInterior(int mode, TownSceneAssets prepared)
    {
        var outer = town.Scene.GetObject(town.Selection);
        interiorAssets = prepared;
        trade = null;
        shopClock.Reset();
        interior.Enter(mode, outer, interiorAssets.Scene, HandleInterior);
        music.Update(musicId);
    }

    private void HandleInterior(InteriorCommand command)
    {
        switch (command.Kind)
        {
            case InteriorCommandKind.SetEventRange:
                break; // Owned by InteriorController, consumed by actual offer selection.
            case InteriorCommandKind.SetScriptContext:
                scriptMember = command.Bytes.ToArray();
                state.ScriptActive = 0;
                pendingHandler = [];
                break;
            case InteriorCommandKind.QueueEnter:
                pendingHandler = command.Bytes.ToArray();
                state.ScriptActive = 1;
                break;
            case InteriorCommandKind.InitializeObjectSprites:
                if (interiorAssets?.Objects is null || !interiorAssets.Scene.SpriteReference.Span.SequenceEqual(command.Bytes.Span))
                {
                    throw new InvalidDataException("Interior sprite load does not match scene metadata.");
                }

                break;
            case InteriorCommandKind.InitializeItemSprites:
                itemSprites = SpriteFile.Decode(Read("AT2SPR.000", command.Bytes.Span));
                shopRenderer = null;
                break;
            case InteriorCommandKind.SelectEventOffer:
                if (InteriorEventOffers.Select(state, interior.EventRange) is null)
                {
                    interior.SetEventOfferEnabled(0);
                }

                break;
            case InteriorCommandKind.OpenActionMenu:
                InteriorActions.OpenAction();
                break;
            case InteriorCommandKind.Sound:
                Sound(command.Value);
                break;
            default:
                throw new NotSupportedException($"Interior operation {command.Kind} has no session binding.");
        }
    }

    private void HandleInteriorInput()
    {
        if (interior.Mode == GameMode.Shop && (interior.Buying || interior.Selling))
        {
            var current = trade ?? throw new InvalidOperationException("Trade state is absent.");
            bool confirm = Query(bindings.Confirm), cancel = !confirm && Query(bindings.Cancel);
            bool left = false, right = false, up = false, down = false, heldUp = false, heldDown = false;
            if (!confirm && !cancel)
            {
                if (current.QuantityActive)
                {
                    heldUp = host.IsHeld(bindings.Up);
                    if (!heldUp)
                    {
                        heldDown = host.IsHeld(bindings.Down);
                    }
                }
                else
                {
                    left = Query(bindings.Previous);
                    if (!left)
                    {
                        right = Query(bindings.Next);
                    }

                    if (!left && !right)
                    {
                        up = Query(bindings.Up);
                        if (!up)
                        {
                            down = Query(bindings.Down);
                        }
                    }
                }
            }

            var result = current.HandleInput(new(confirm, cancel, left, right, up, down, heldUp, heldDown));
            if (result.Sound >= 0)
            {
                Sound(result.Sound);
            }

            if (result.ReturnToActionMenu)
            {
                trade = null;
                interior.ReturnFromTrade(HandleInterior);
            }

            return;
        }

        //420/430 first-entry branches return before reading the confirm edge;410 continues to read it.
        bool readConfirm = !interior.FirstEntry || interior.Mode == GameMode.Interior;
        interior.HandleInput(readConfirm && Query(bindings.Confirm), HandleInterior);
    }

    private void BeginTrade(bool buying)
    {
        if (itemSprites is null)
        {
            throw new InvalidOperationException("Shop item sprites have been released.");
        }

        shopRenderer ??= new(
            itemSprites,
            etc,
            smallFont,
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false),
            items,
            File.ReadAllBytes(Path.Combine(root, "ITEM_EX.DAT")),
            displayMessages
        );
        trade = new(state, items);
        if (buying)
        {
            if (shopStocks is null)
            {
                throw new InvalidOperationException("New-game shop stock was not initialized.");
            }

            if ((uint)state.ShopNumber >= (uint)shopStocks.Length)
            {
                throw new InvalidDataException("Script selected an invalid shop number.");
            }

            trade.BeginBuy(shopStocks[state.ShopNumber]);
        }
        else
        {
            trade.BeginSell();
        }
    }

    private void LeaveInterior()
    {
        int leavingMode = state.Mode;
        actions.CloseAll();
        //428100 deliberately retains existing68104 for interiors. The actual script determines permission.
        RunScript(scriptMember, "pre_out"u8, ScenarioContinuation.LeaveBuilding, leavingMode);
        CompleteLeaveInterior(leavingMode);
    }

    private void CompleteLeaveInterior(int leavingMode)
    {
        if (state.Mode is GameMode.Defeat or GameMode.Ending || state.EnterPermission == 0)
        {
            return;
        }

        if (leavingMode == 430)
        {
            Sound(4);
        }

        state.ModeStackDepth = -1;
        saveDisabled = false;
        actions.SetRenderEnabled(false);
        state.People = Array.Empty<ScriptPerson>();
        trade = null;
        CommitLocation(LocationKind.Town, townArrival: false);
    }

    private void ComposeInterior(Span<ushort> target)
    {
        var assets = interiorAssets ?? throw new InvalidOperationException("Interior assets absent.");
        assets.Background565.CopyTo(target);
        var frame = interior.BuildRenderFrame(assets.Objects is not null);
        if (frame.DrawObject)
        {
            var sprites = assets.Objects!;
            if ((uint)frame.ObjectIndex >= (uint)sprites.Frames.Length)
            {
                throw new InvalidDataException("Interior frame is absent.");
            }
            //4215e0 passes mode5;439530 uses Copy for modes other than1/2/3.
            SpriteRaster.Draw565(sprites.Frames[frame.ObjectIndex], target, 640, 480, 640, frame.X, frame.Y, SpriteBlend.Copy);
        }

        if (frame.DrawBuy || frame.DrawSell)
        {
            var current = trade ?? throw new InvalidOperationException("Trade state absent during composition.");
            (shopRenderer ?? throw new InvalidOperationException("Shop raster absent.")).Draw(current, state, shopClock.AdvanceLogicalFrame(current.Count > 0), target);
        }
    }
}
