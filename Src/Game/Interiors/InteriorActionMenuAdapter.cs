using Atria.Game.ActionMenu;
using Atria.Game.State;

namespace Atria.Game.Interiors;

/// <summary>Maps actual generic action-menu callbacks to interior/talk/trade hosts without inventing script outcomes.</summary>
public sealed class InteriorActionMenuAdapter
{
    private readonly ActionMenuController menus;
    private readonly InteriorController interior;
    private readonly GameState state;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    private readonly Func<ReadOnlyMemory<byte>> currentScript;
    private readonly Action<InteriorTalkChoice> executeScript;
    private readonly Action leaveWithPreOut;
    private readonly Action<bool> beginTrade;
    private readonly Action releaseItemSprites;
    private readonly Action<int> sound;
    private InteriorTalkMenu? talk;
    public InteriorActionMenuAdapter(
        ActionMenuController menus,
        InteriorController interior,
        GameState state,
        IReadOnlyList<ReadOnlyMemory<byte>> messages,
        Func<ReadOnlyMemory<byte>> currentScript,
        Action<InteriorTalkChoice> executeScript,
        Action leaveWithPreOut,
        Action<bool> beginTrade,
        Action releaseItemSprites,
        Action<int> sound
    )
    {
        this.menus = menus;
        this.interior = interior;
        this.state = state;
        this.messages = messages;
        this.currentScript = currentScript ?? throw new ArgumentNullException(nameof(currentScript));
        this.executeScript = executeScript ?? throw new ArgumentNullException(nameof(executeScript));
        this.leaveWithPreOut = leaveWithPreOut ?? throw new ArgumentNullException(nameof(leaveWithPreOut));
        this.beginTrade = beginTrade ?? throw new ArgumentNullException(nameof(beginTrade));
        this.releaseItemSprites = releaseItemSprites ?? throw new ArgumentNullException(nameof(releaseItemSprites));
        this.sound = sound ?? throw new ArgumentNullException(nameof(sound));
    }

    public void OpenAction() => menus.OpenInterior(interior.Mode, messages);
    public void OpenTalk()
    {
        EventOffer? offer = null;
        if (interior.EventRange.Enabled != 0)
        {
            offer = InteriorEventOffers.Select(state, interior.EventRange);
            if (offer is null)
            {
                interior.SetEventOfferEnabled(0);
            }
        }

        talk = InteriorTalkMenu.Build(state, currentScript(), offer, messages);
        if (talk.Choices.Count == 0)
        {
            // Quest-only rooms legitimately become empty after their offers are exhausted.
            // Keep the ordinary action/leave menu instead of creating an invalid zero-row selector.
            menus.CloseAll();
            OpenAction();
            return;
        }

        var currentTalk = talk;
        menus.OpenTalk(currentTalk.Choices.Select(c => c.Label).ToArray(), offer is not null, interior.Mode,
            () => currentTalk.Choices.Select(c => c.Label).ToArray());
    }

    public void HandleCommand(ActionMenuCommand command)
    {
        switch (command.Kind)
        {
            case ActionMenuEffect.Sound:
                sound(command.Value);
                return;
            case ActionMenuEffect.LeaveInterior:
                leaveWithPreOut();
                return;
            case ActionMenuEffect.ReturnInteriorAction:
                //4279d0 cancellation pushes the ordinary interior menu without first popping the talk entry.
                OpenAction();
                return;
            case ActionMenuEffect.InvokeChoice:
                if (command.MenuType == 8)
                {
                    var choice = (talk ?? throw new InvalidOperationException("Talk menu is absent.")).Select(command.Value);
                    menus.CloseAll();
                    executeScript(choice);
                    menus.CloseAll();
                    if (state.Mode is 410 or 420 or 430)
                    {
                        menus.OpenInterior(state.Mode, messages);
                    }

                    return;
                }

                if (command.MenuType is not (3 or 5 or 6))
                {
                    throw new NotSupportedException("Unrecovered interior action callback.");
                }

                menus.CloseAll();
                interior.ChooseAction(command.Value, HandleInteriorCommand);
                return;
            default:
                throw new NotSupportedException("This adapter does not process town callbacks.");
        }
    }

    private void HandleInteriorCommand(InteriorCommand command)
    {
        switch (command.Kind)
        {
            case InteriorCommandKind.OpenTalkMenu:
                OpenTalk();
                break;
            case InteriorCommandKind.BeginBuy:
                beginTrade(true);
                break;
            case InteriorCommandKind.BeginSell:
                beginTrade(false);
                break;
            case InteriorCommandKind.ReleaseItemSprites:
                releaseItemSprites();
                break;
            case InteriorCommandKind.LeaveWithPreOut:
                leaveWithPreOut();
                break;
            default:
                throw new NotSupportedException("Unhandled interior action command.");
        }
    }
}
