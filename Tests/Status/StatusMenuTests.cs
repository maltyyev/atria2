using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Resources;

namespace Atria.Tests.Status;

public static class StatusMenuTests
{
    public static string Run()
    {
        var menu = new StatusMenuController();
        var state = new GameState();
        var commands = new List<StatusMenuCommand>();
        state.SetPartyInt(0, 60, 1);
        state.SetPartyInt(3, 60, 2);
        state.SetPartyInt(5, 60, -1);
        menu.Enter(commands.Add);
        Check(menu.InputDelay == 2 && menu.Page == 0 && menu.SelectedActor == 0, "414f50 initial counters");
        for (int i = 0; i < 2; i++)
        {
            menu.Input(new(Cancel: true), state, commands.Add, _ => throw new Exception());
        }

        Check(commands.Count == 2 && menu.InputDelay == 0, "two input ticks suppress cancel");
        commands.Clear();
        menu.Input(new(NextActor: true), state, commands.Add, _ =>
        {
        });
        Check(menu.SelectedActor == 3 && commands[0] == new StatusMenuCommand(StatusMenuEffect.SelectActor, 3), "membership+60 skips empty slots");
        menu.Input(new(NextActor: true), state, commands.Add, _ =>
        {
        });
        menu.Input(new(NextActor: true), state, commands.Add, _ =>
        {
        });
        Check(menu.SelectedActor == 5, "right stops at last first-six slot and accepts negative nonzero membership");
        menu.Input(new(CycleActor: true), state, commands.Add, _ =>
        {
        });
        Check(menu.SelectedActor == 0, "Tab wraps first six");
        commands.Clear();
        menu.Input(new(PreviousPage: true, NextPage: true, NextActor: true), state, commands.Add, _ =>
        {
        });
        Check(menu.Page == 0 && menu.SelectedActor == 0 && commands.Count == 0, "bounded previous page consumes lower-priority keys");
        for (int i = 0; i < 7; i++)
        {
            menu.Input(new(NextPage: true), state, commands.Add, _ =>
            {
            });
        }

        Check(menu.Page == 4, "five native pages bounded without wrap");
        commands.Clear();
        menu.Input(new(Cancel: true, Accept: true), state, commands.Add, _ =>
        {
        });
        Check(commands.SequenceEqual(new[] { new StatusMenuCommand(StatusMenuEffect.Sound, 4), new(StatusMenuEffect.ReleaseSprites), new(StatusMenuEffect.PopMode), new(StatusMenuEffect.SetRestoreFlag, 1) }), "cancel priority and native release/pop/restore order");
        menu.SetDetailActive(true);
        bool delegated = false;
        menu.Input(new(Accept: true), state, commands.Add, _ => delegated = true);
        Check(delegated, "active detail routes required handler");
        state.OwnedItems[1] = 1;
        state.OwnedItems[3] = -2;
        state.OwnedItems[87] = 1;
        state.OwnedItems[99] = 1;
        state.OwnedItems[100] = 1;
        state.OwnedItems[4] = 1;
        state.OwnedItems[5] = 1;
        var list = new StatusTextList(state, 1);
        Check(list.Ids.SequenceEqual(new[] { 1, 4, 5, 87 }), "inventory contains only positive counts with actual item definitions");
        var sounds = new List<int>();
        int description = -1;
        for (int i = 0; i < 3; i++)
        {
            list.Input(0, sounds.Add, id => description = id);
        }

        Check(list.Row == 2 && list.Scroll == 1 && list.SelectedId == 87, "native three-row list scroll");
        list.Input(4, sounds.Add, id => description = id);
        Check(description == 87 && list.DescriptionOpen, "description retains native selectedID");
        list.Input(1, sounds.Add, _ =>
        {
        });
        Check(list.SelectedId == 87, "description blocks selection movement");
        Check(!list.Input(5, sounds.Add, _ =>
        {
        }) && !list.DescriptionOpen && sounds[^1] == 3, "description cancel closes with sound3");
        Check(list.Input(5, sounds.Add, _ =>
        {
        }) && sounds[^1] == 4, "second cancel leaves detail with sound4");
        state.SetQuestItemFlag(0, -1);
        state.SetQuestItemFlag(50, 1);
        Check(new StatusTextList(state, 3).Ids.SequenceEqual(new[] { 0, 50 }), "quest list includes51 nonzero flags includingrecord0");
        state.EventStatuses[0] = 4;
        state.EventStatuses[149] = 4;
        state.EventStatuses[150] = 4;
        state.EventStatuses[1] = 3;
        state.EventStatuses[2] = 1;
        state.EventStatuses[3] = 2;
        state.EventStatuses[4] = 5;
        Check(new StatusTextList(state, 4).Ids.SequenceEqual(new[] { 0, 2, 3 }), "journal excludes declined offers, empty records, sentinels and IDs outside catalog");
        byte[] itemBytes = new byte[88 * (98 + 310)];
        void ItemInt(int id, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(itemBytes.AsSpan(id * 98 + offset, 4), value);
        ItemInt(1, 30, 5);
        ItemInt(1, 58, 1);
        ItemInt(1, 62, 0);
        ItemInt(1, 74, 7);
        ItemInt(2, 30, 6);
        var items = ItemCatalog.Parse(itemBytes);
        var equipment = new GameState();
        equipment.SetMemberStatus(0, 1);
        equipment.SetPartyInt(0, 64, 0);
        equipment.SetPartyInt(0, 200, 1);
        equipment.SetPartyInt(0, 252, 3);
        equipment.SetPartyInt(0, 100, 10);
        for (int slot = 0; slot < 6; slot++)
        {
            equipment.SetPartyInt(0, 228 + slot * 4, -1);
        }

        equipment.OwnedItems[1] = 1;
        equipment.OwnedItems[2] = -1;
        equipment.AvailableItems[1] = 1;
        Check(StatusEquipment.Candidates(equipment, items, 0, 1).SequenceEqual(new[] { 1 }), "equipment requires positive owned and available counts");
        Check(StatusEquipment.Equip(equipment, items, 0, 1, 1) == StatusEquipResult.Changed && equipment.GetPartyInt(0, 100) == 17
&& equipment.GetPartyInt(0, 232) == 1 && equipment.AvailableItems[1] == 0 && equipment.OwnedItems[1] == 1, "equip modifier0 updates party100 and available only");
        Check(StatusEquipment.Equip(equipment, items, 0, 1, 1) == StatusEquipResult.Unavailable, "availability guard precedes same-item guard");
        Check(StatusEquipment.Candidates(equipment, items, 0, 1).Length == 0, "reserved last item no longer offered for another slot");
        Check(StatusEquipment.Remove(equipment, items, 0, 1) == StatusEquipResult.Changed && equipment.GetPartyInt(0, 100) == 10
&& equipment.AvailableItems[1] == 1, "415c60 modifier0 subtracts and returns available item");
        Check(StatusEquipment.Remove(equipment, items, 0, 1) == StatusEquipResult.EmptySlot, "unequip sentinel minus1");
        return "Status root checks passed (414f50/416f90 input priority, party membership, bounded selection, page and exit contracts).";
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
