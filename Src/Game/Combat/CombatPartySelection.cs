using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Combat;

public enum CombatSelectionInput
{
    Previous,
    Next,
    Toggle,
    Finish
}

/// <summary>424100 participation/leader state. Only actual finish input can complete selection.</summary>
public sealed class CombatPartySelection
{
    private readonly GameState working = new();
    private readonly bool[] eligible = new bool[6];
    private readonly byte[] selected = new byte[6];
    private readonly int[] leaders = new int[2];
    public IReadOnlyList<PartyMember> Party => working.Party;
    public ReadOnlySpan<byte> Selected => selected;
    public ReadOnlySpan<int> Leaders => leaders.AsSpan(0, PlayerCount);
    public int PlayerCount
    {
        get;
    }
    public int ErrorCode
    {
        get; private set;
    }
    public bool Completed
    {
        get; private set;
    }
    public uint InputRevision
    {
        get; private set;
    }
    public uint FinishRevision
    {
        get; private set;
    }

    public void ClearDisplayedError() => ErrorCode = 0;
    public bool PointerSelect(int player, int actor)
    {
        if ((uint)player >= (uint)PlayerCount || (uint)actor >= 6 || !eligible[actor])
        {
            return false;
        }

        if (leaders[player] != actor)
        {
            leaders[player] = actor;
            InputRevision++;
        }

        return true;
    }

    public CombatPartySelection(GameState state, int configuredPlayers)
    {
        if (configuredPlayers is not (1 or 2))
        {
            throw new NotSupportedException("Native battle selection supports one or two players.");
        }

        PartyCodec.Copy(state.Party, working.Party);
        int count = 0;
        for (int actor = 0; actor < 6; actor++)
        {
            eligible[actor] = working.GetMemberStatus(actor) != 0;
            working.Party[actor].PendingAction = eligible[actor] ? 1 : 0;
            if (!eligible[actor])
            {
                continue;
            }

            selected[actor] = unchecked((byte)working.Party[actor].SelectedForBattle);
            count++;
            if (working.Party[actor].Health == 0)
            {
                working.Party[actor].Health = 1;
            }

            leaders[count == 1 ? 0 : 1] = actor;
        }

        if (count == 0)
        {
            throw new InvalidDataException("Battle selection has no eligible party member.");
        }

        PlayerCount = count < 2 ? 1 : configuredPlayers;
    }

    // Query is called lazily in native per-player priority; sounds are explicit results for the future modal.
    public void ApplyInput(Func<int, CombatSelectionInput, bool> query, Action<string> playExclusiveSound)
    {
        if (Completed)
        {
            throw new InvalidOperationException("Battle party selection has completed.");
        }

        for (int player = 0; player < PlayerCount; player++)
        {
            int direction = query(player, CombatSelectionInput.Previous) ? -1 : query(player, CombatSelectionInput.Next) ? 1 : 0;
            if (direction != 0)
            {
                playExclusiveSound("move.wav");
                InputRevision = unchecked(InputRevision + 1);
                do
                {
                    leaders[player] = (leaders[player] + direction + 6) % 6;
                }
                while (!eligible[leaders[player]]);
            }

            if (query(player, CombatSelectionInput.Toggle))
            {
                playExclusiveSound("click.wav");
                InputRevision = unchecked(InputRevision + 1);
                selected[leaders[player]] = selected[leaders[player]] == 0 ? (byte)1 : (byte)0;
            }
        }

        if (!query(0, CombatSelectionInput.Finish))
        {
            return;
        }

        FinishRevision = unchecked(FinishRevision + 1);
        int count = 0;
        foreach (byte value in selected)
        {
            if (value != 0)
            {
                count++;
            }
        }

        ErrorCode = 0;
        if (count == 0)
        {
            ErrorCode = 1;
        }
        else if (count >= 4)
        {
            ErrorCode = 2;
        }
        else if (count < 2 && PlayerCount > 1)
        {
            ErrorCode = 3;
        }
        else
        {
            foreach (int leader in Leaders)
            {
                if (selected[leader] == 0)
                {
                    ErrorCode = 4;
                    break;
                }
            }

            if (PlayerCount == 2 && leaders[0] == leaders[1])
            {
                ErrorCode = 5;
            }
        }

        if (ErrorCode != 0)
        {
            playExclusiveSound("error.wav");
            return;
        }

        playExclusiveSound("start.wav");
        for (int actor = 0; actor < 6; actor++)
        {
            if (!eligible[actor])
            {
                continue;
            }

            int flag = unchecked((sbyte)selected[actor]);
            working.Party[actor].PendingAction = flag;
            working.Party[actor].SelectedForBattle = flag;
        }

        Completed = true;
    }

    public void Commit(GameState state)
    {
        if (!Completed)
        {
            throw new InvalidOperationException("Actual prebattle selection input is required before party commit.");
        }

        PartyCodec.Copy(working.Party, state.Party);
    }
}
