using Atria.Platform.Input;
using System.Buffers.Binary;
using System.Text;
using Atria.Game.Combat;
using Atria.Game.Controls;
using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Platform;
using Atria.Platform.Win32;
using Atria.Resources;
using Atria.Tests.Combat;

namespace Atria.Tests.Controls;

public static class ControlRedesignTests
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception("Controls: " + message);
        }
    }

    private static int I(object b, int at) => LegacyActorFixture.ReadInt32LittleEndian(b.FixtureSpan(at));
    private static void W(object b, int at, int v) => LegacyActorFixture.WriteInt32LittleEndian(b.FixtureSpan(at), v);
    private sealed class Pads : IGamepadDevice
    {
        public PadSample[] Samples = new PadSample[4];
        public int Rumbles;
        public PadSample Read(int slot) => Samples[slot];
        public void Vibrate(int slot, ushort left, ushort right)
        {
            if (left + right > 0)
            {
                Rumbles++;
            }
        }
    }

    private sealed class Effects : ICombatControlEffects
    {
        public bool GlobalEffectBusy => false;

        public void PlaySound(int id)
        {
        }

        public void SpawnActorParticle(int actor, int id, uint now)
        {
        }

        public void ApplyFrameEffect(int actor, int id, int argument, uint now)
        {
        }

        public void ShowConsumedItemNotice(int id, uint now)
        {
        }

        public void ShowRankNotice(int rank, uint now)
        {
        }
    }

    public static string Run(string root)
    {
        var legacy = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        var profile = ControlProfile.Defaults(legacy);
        var pad = new Pads();
        using var input = new InputRouter(profile, legacy, pad);
        bool[] keys = new bool[256], pressed = new bool[256];
        bool[] mouse = new bool[3];
        long time = 0;
        void Poll()
        {
            input.Poll(keys, pressed, mouse, true, time++);
            Array.Clear(pressed);
        }

        Poll();
        keys[17] = true;
        Poll();
        Check(input.Held(0, GameAction.Up), "W moves up");
        Check(input.Menu(GameAction.MenuUp), "W menu alias");
        Check(!input.Menu(GameAction.MenuUp), "one physical edge consumed once");
        keys[17] = false;
        Poll();
        keys[36] = true;
        Poll();
        Check(input.Press(0, GameAction.Attack), "J attacks");
        Check(!input.Press(0, GameAction.Attack), "holding cannot queue repeats");
        input.Clear();
        Check(!input.Held(0, GameAction.Attack), "held input blocked across context");
        keys[36] = false;
        Poll();
        keys[36] = true;
        Poll();
        Check(input.Press(0, GameAction.Attack), "release rearms blocked key");
        keys[36] = false;
        Poll();
        pressed[36] = true;
        Poll();
        Poll();
        Check(input.Press(0, GameAction.Attack), "short tap retained until logic consumes it");
        input.Poll(keys, pressed, mouse, true, time++, [true, false, false]);
        Poll();
        Check(input.Press(0, GameAction.Attack), "mouse down/up in one pump is retained");
        keys[17] = true;
        Poll();
        Check(input.Menu(GameAction.MenuUp, true), "menu initial edge");
        time += 348;
        Poll();
        Check(!input.Menu(GameAction.MenuUp, true), "menu repeat initial delay");
        time += 2;
        Poll();
        Check(input.Menu(GameAction.MenuUp, true), "menu repeat after delay");
        keys[17] = false;
        Poll();
        pad.Samples[0] = new(true, 0x4000, 0, 0, 9000, 0, 0, 0);
        time = 2000;
        Poll();
        Check(input.Press(0, GameAction.Attack) && input.Held(0, GameAction.Right), "XInput X and right stick threshold");
        Check(!input.Press(1, GameAction.Attack), "pad0 does not control player2");
        pad.Samples[0] = pad.Samples[0] with
        {
            LX = 7000
        };
        Poll();
        Check(input.Held(0, GameAction.Right), "stick hysteresis retains direction");
        pad.Samples[0] = pad.Samples[0] with
        {
            LX = 5000
        };
        Poll();
        Check(!input.Held(0, GameAction.Right), "stick exits deadzone hysteresis");
        input.Rumble(0);
        Check(pad.Rumbles == 1, "bounded optional rumble");
        pad.Samples[0] = default;
        Poll();
        Check(input.FocusPauseRequested && !input.Held(0, GameAction.Attack), "unplug releases and requests pause");
        pad.Samples[1] = new(true, 0x1000, 0, 0, 0, 0, 0, 0);
        time += 2000;
        Poll();
        Check(input.Press(1, GameAction.Jump) && !input.Press(0, GameAction.Jump), "second gamepad ownership");
        pad.Samples[1] = pad.Samples[1] with
        {
            LX = 16000,
            LY = 16000
        };
        Poll();
        Check(input.Held(1, GameAction.Up) && input.Held(1, GameAction.Right), "independent pad diagonal");
        input.ClearPlayer(0);
        Check(input.Held(1, GameAction.Right), "player1 context clear preserves player2 held input");
        keys[30] = true;
        Poll();
        input.Poll(keys, pressed, mouse, false, time++);
        Check(!input.Held(0, GameAction.Left) && input.FocusPauseRequested, "focus loss clears movement");
        Array.Clear(keys);
        Poll();
        profile.Players[0].Quick[0][0] = "v:1:32";
        profile.Players[0].Direct["0/p:6"] = [InputBinding.Key(34), default, InputBinding.Pad(15)];
        var decoded = ControlProfileStore.Decode(ControlProfileStore.Encode(profile));
        Check(decoded.Players[0].Quick[0][0] == "v:1:32" && decoded.Players[0].Direct["0/p:6"][2].Code == 15, "explicit AOT-compatible profile schema");
        decoded.Players[1].PadSlot = decoded.Players[0].PadSlot;
        bool rejected = false;
        try
        {
            ControlProfileStore.Decode(ControlProfileStore.Encode(decoded));
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, "duplicate pad ownership rejected");
        pad.Samples[0] = new(true, 0, 0, 0, 0, 0, 0, 0);
        time += 2000;
        Poll();
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 1
        };
        Poll();
        Check(!input.PressQuick(0, 0), "bare D-pad does not execute quick slot");
        pad.Samples[0] = pad.Samples[0] with
        {
            RightTrigger = 255
        };
        Poll();
        Check(input.PressQuick(0, 0), "RT pressed after held D-pad forms chord");
        Poll();
        Check(!input.PressQuick(0, 0), "held chord fires only once");
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 0
        };
        Poll();
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 1
        };
        Poll();
        Check(input.PressQuick(0, 0), "held RT plus fresh D-pad fires");
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 0,
            RightTrigger = 0
        };
        Poll();
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 1,
            RightTrigger = 255
        };
        Poll();
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 0,
            RightTrigger = 0
        };
        Poll();
        Check(input.PressQuick(0, 0) && !input.PressQuick(0, 0), "short complete chord retained exactly once after release");
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 0,
            RightTrigger = 0
        };
        Poll();
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 1
        };
        Poll();
        Check(input.PressQuick(0, 0, true), "selector assignment needs no RT");
        pad.Samples[0] = pad.Samples[0] with
        {
            Buttons = 0
        };
        Poll();
        keys[2] = true;
        Poll();
        Check(input.PressQuick(0, 0), "keyboard digit needs no modifier");
        keys[2] = false;
        Poll();
        var clock = new MonotonicModalClock();
        clock.Pause();
        uint paused = clock.Milliseconds;
        Thread.Sleep(15);
        Check(clock.Milliseconds == paused, "pause freezes logical time");
        clock.Continue();
        Thread.Sleep(5);
        Check(clock.Milliseconds >= paused + 1, "resume clock");
        var catalog = new MoveCatalog(root);
        var assets = new Dictionary<string, CombatAnimationAssets>();
        int variants = 0, commands = 0;
        var scene = CombatSceneDefinition.Load(root, new(FixedEncounterKind.Scripted, 2));
        var runtime = new CombatSceneRuntime(root, scene, 0);
        byte[] controls = [10, 11, 12, 13, 14, 15, 16, 17];
        for (int party = 0; party < 6; party++)
        {
            for (int form = 1; form <= 5; form++)
            {
                var info = catalog.Info(party, form);
                string name = Encoding.ASCII.GetString(info.AnimationResource.Span);
                if (!assets.TryGetValue(name, out var art))
                {
                    art = CombatAnimationAssets.Load(root, info.AnimationResource.Span);
                    assets.Add(name, art);
                }

                CombatActorImage Actor(int flags = 32, int mode = 6, int jump = 0, int facing = 1)
                {
                    var image = new CombatActorImage();
                    image.Binding = new(0, 0, info, art);
                    image.Animation = new(image.State, image.Binding.Info, art);
                    image.Animation.BeginMotion(0, 100);
                    W(image.State, 0xad, flags);
                    W(image.State, 0xb1, mode);
                    W(image.State, 0xb9, jump);
                    image.State.Facing = unchecked((byte)facing);
                    W(image.State, 0x70, 300);
                    W(image.State, 0x78, 350);
                    W(image.State, 0x178, 2000);
                    W(image.State, 0x17c, 9);
                    W(image.State, 0x170, 100);
                    W(image.State, 0x224, 1);
                    for (int i = 0; i < 6; i++)
                    {
                        W(image.State, 0x208 + 4 * i, -1);
                    }

                    return image;
                }

                for (int v = info.FirstVariation; v < info.VariationEnd; v++)
                {
                    int d = info.Attacks[v].Direction, flags = info.Attacks[v].RequiredFlags;
                    if (d is not (1 or 2 or 3 or 4 or 6 or 7 or 8 or 9))
                    {
                        continue;
                    }

                    Check(catalog.All(party).Any(m => m.Id == $"v:{d}:{flags}"), "catalog covers every directed V row");
                    foreach (int facing in new[]
                    {
                        1,
                        -1
                    }
                    )
                    {
                        var expected = Actor(flags, facing: facing);
                        var actual = Actor(flags, facing: facing);
                        var held = new HashSet<byte>
                        {
                            15
                        };
                        if (d is 1 or 2 or 3)
                        {
                            held.Add(13);
                        }

                        if (d is 7 or 8 or 9)
                        {
                            held.Add(12);
                        }

                        if (d is 1 or 4 or 7)
                        {
                            held.Add(facing == 1 ? (byte)10 : (byte)11);
                        }

                        if (d is 3 or 6 or 9)
                        {
                            held.Add(facing == 1 ? (byte)11 : (byte)10);
                        }

                        CombatManualControl.ContinueAction(expected, controls, new(held.Contains), 500);
                        Check(CombatSemanticControl.Variation(actual, d, flags, 500), "legal direct transition accepted");
                        Check(expected.State.AsSpan().SequenceEqual(actual.State) && expected.Animation!.SequenceIndex == actual.Animation!.SequenceIndex, $"transition parity {party}/{form}/{v}/{facing}");
                        variants++;
                        var invalid = Actor(0);
                        byte[] before = invalid.State.ToArray();
                        Check(!CombatSemanticControl.Variation(invalid, d, flags, 500) && before.AsSpan().SequenceEqual(invalid.State), "reject closed continuation window without mutation");
                        string id = $"v:{d}:{flags}";
                        var quick = Actor(0, 0, facing: facing);
                        var direct = Actor(0, 0, facing: facing);
                        Check(CombatSemanticControl.QuickVariation(quick, id, 500), "quick move starts from rest");
                        int selected = Enumerable.Range(info.FirstVariation, info.VariationEnd - info.FirstVariation)
                            .First(i => info.Attacks[i].Direction == d && info.Attacks[i].RequiredFlags == flags);
                        direct.Animation!.BeginVariation(selected, 500);
                        Check(quick.State.AsSpan().SequenceEqual(direct.State)
                            && quick.Animation!.SequenceIndex == direct.Animation.SequenceIndex,
                            "quick move binds exactly the chosen original variation, without a prelude");
                        foreach (var blocked in new[] { Actor(64, 7), Actor(290, 0, 16), Actor(0, 0) })
                        {
                            if (blocked.State.ActionFlags == 0) blocked.State.Health = 0;
                            byte[] snapshot = blocked.State.ToArray();
                            Check(!CombatSemanticControl.QuickVariation(blocked, id, 501)
                                && snapshot.AsSpan().SequenceEqual(blocked.State), "busy/airborne/dead quick request rejected without mutation");
                        }
                    }
                }

                var airOld = Actor(290, 0, 16);
                var airNew = Actor(290, 0, 16);
                CombatManualControl.ContinueAction(airOld, controls, new(scan => scan == 14), 500);
                CombatSemanticControl.AirAttack(airNew, 500);
                Check(airOld.State.AsSpan().SequenceEqual(airNew.State) && airOld.Animation!.SequenceIndex == airNew.Animation!.SequenceIndex, "aerial attack parity including duplicate rows");
                for (int a = 0; a < info.BasicAttackCount; a++)
                {
                    var attack = Actor(1, 0);
                    attack.Animation!.BeginAttack(a, 500);
                    Check(attack.Animation.SequenceIndex == info.Attacks[a].Animation, "all A sequences resolve");
                }

                var runOld = Actor(1, 2);
                var runNew = Actor(1, 2);
                CombatManualControl.Walk(runOld.State, info, runOld.Animation!, controls, new(scan => scan == 11), 500);
                CombatManualControl.Walk(runNew.State, info, runNew.Animation!, controls, new(scan => scan == 11), 500, true);
                Check(runOld.State.AsSpan().SequenceEqual(runNew.State), "run button preserves native running speed and timing");
                CombatManualControl.Attack(runOld.State, info, runOld.Animation!, 600);
                CombatManualControl.Attack(runNew.State, info, runNew.Animation!, 600);
                Check(runOld.State.AsSpan().SequenceEqual(runNew.State) && runOld.Animation!.SequenceIndex == runNew.Animation!.SequenceIndex, "running attack parity");
                for (int p = 0; p < info.Commands.Count; p++)
                {
                    var command = info.Commands[p];
                    int cat = command.Category;
                    if (cat == 0)
                    {
                        continue;
                    }

                    string id = "p:" + command.Icon;
                    Check(catalog.All(party).Any(m => m.Id == id), "catalog covers P row");
                    var old = Actor(1, 0);
                    var modern = Actor(1, 0);
                    int first = p;
                    while (first > 0 && info.Commands[first - 1].Category == cat)
                    {
                        first--;
                    }

                    W(old.State, 0x2c, cat);
                    W(old.State, 0x34, p - first);
                    W(old.State, 0x30, 0);
                    CombatManualControl.ExecuteCommand(runtime, old, new GameState(), 500, false, _ =>
                    {
                    }, _ =>
                    {
                    }, _ =>
                    {
                    });
                    Check(CombatSemanticControl.Execute(id, runtime, modern, new GameState(), 500, false, _ =>
                    {
                    }, _ =>
                    {
                    }, _ =>
                    {
                    }), "legal MP command fires");
                    Check(old.State.AsSpan().SequenceEqual(modern.State) && old.Animation!.SequenceIndex == modern.Animation!.SequenceIndex, "MP command parity " + id);
                    commands++;
                    var locked = Actor(1, 0);
                    W(locked.State, 0x17c, 0);
                    byte[] before = locked.State.ToArray();
                    Check(!CombatSemanticControl.Execute(id, runtime, locked, new GameState(), 500, false, _ =>
                    {
                    }, _ =>
                    {
                    }, _ =>
                    {
                    }) && before.AsSpan().SequenceEqual(locked.State), "rank lock cannot be bypassed");
                    var busy = Actor(1, 0);
                    before = busy.State.ToArray();
                    Check(!CombatSemanticControl.Execute(id, runtime, busy, new GameState(), 500, true, _ =>
                    {
                    }, _ =>
                    {
                    }, _ =>
                    {
                    }) && before.AsSpan().SequenceEqual(busy.State), "global effect lock");
                    if (command.ManaCost > 0)
                    {
                        var empty = Actor(1, 0);
                        W(empty.State, 0x178, 0);
                        before = empty.State.ToArray();
                        Check(!CombatSemanticControl.Execute(id, runtime, empty, new GameState(), 500, false, _ =>
                        {
                        }, _ =>
                        {
                        }, _ =>
                        {
                        }) && before.AsSpan().SequenceEqual(empty.State), "MP shortage rejects without mutation");
                    }
                }
            }
        }

        // Exercise the actual controller, not only isolated move handlers.
        var state = InitialGameState.Create(ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT"))), LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        var combat = new CombatSceneRuntime(root, scene, 0);
        combat.BindDeclaredAnimations(0);
        var chosen = new CombatPartySelection(state, 1);
        chosen.ApplyInput((_, a) => a is CombatSelectionInput.Toggle or CombatSelectionInput.Finish, _ =>
        {
        });
        combat.PopulateSelectedParty(chosen);
        combat.BindAllAttributes(0, 0, 0, () => 1);
        combat.BindPlayerRoles(chosen);
        var liveProfile = ControlProfile.Defaults(legacy);
        var livePads = new Pads();
        using var live = new InputRouter(liveProfile, legacy, livePads);
        var adapter = new CombatScanInput(_ => false)
        {
            Modern = live
        };
        var controller = new CombatActorController(combat, state, legacy, adapter, new Effects(), () => 1);
        int index = combat.Scene.PartyStartIndex;
        var hero = combat.Actors[index];
        Array.Clear(keys);
        Array.Clear(pressed);
        Array.Clear(mouse);
        live.Poll(keys, pressed, mouse, true, 0);
        keys[36] = true;
        live.Poll(keys, pressed, mouse, true, 1);
        W(hero.State, 0xad, 1);
        controller.Process(index, 500);
        Check(hero.Animation!.SequenceIndex == hero.Binding!.Info.Attacks[0].Animation, "physical J reaches native first attack through controller");
        keys[36] = false;
        live.Poll(keys, pressed, mouse, true, 2);
        W(hero.State, 0xad, 0);
        hero.State.ChainMode = 0;
        keys[2] = true;
        live.Poll(keys, pressed, mouse, true, 3);
        byte[] rejectedFields = hero.State.ToArray();
        controller.Process(index, 501);
        Check(!rejectedFields.AsSpan().SequenceEqual(hero.State) && hero.Animation!.SequenceIndex != hero.Binding!.Info.Attacks[0].Animation, "digit quick slot immediately starts variation through controller");
        live.Clear();
        W(hero.State, 0xad, 0);
        controller.Process(index, 502);
        Check(I(hero.State, 0xad) < 2, "context change does not leave a delayed action");
        keys[2] = false;
        live.Poll(keys, pressed, mouse, true, 4);
        W(hero.State, 0xad, 32);
        W(hero.State, 0xb9, 0);
        W(hero.State, 0xb1, 6);
        keys[2] = true;
        live.Poll(keys, pressed, mouse, true, 5);
        controller.Process(index, 503);
        Check(I(hero.State, 0xad) != 32, "fresh legal quick request dispatches variation");
        Array.Clear(keys);
        livePads.Samples[0] = new(true, 0, 0, 0, 0, 0, 0, 0);
        live.Poll(keys, pressed, mouse, true, 2000);
        hero.Animation!.BeginMotion(0, 600);
        W(hero.State, 0xad, 0);
        hero.State.ChainMode = 0;
        livePads.Samples[0] = livePads.Samples[0] with
        {
            Buttons = 1,
            RightTrigger = 255
        };
        live.Poll(keys, pressed, mouse, true, 2001);
        controller.Process(index, 601);
        Check(hero.Animation.SequenceIndex != hero.Binding!.Info.Attacks[0].Animation && I(hero.State, 0xad) != 32, "RT+D-pad immediately starts variation through actual controller");
        return $"Controls: physical/chord edges, controller lifecycle, profiles, pause clock, 30 real INF forms; {variants} directed transitions AND immediate quick moves from rest (both facings), rejected states, aerial/A sequences and {commands} MP commands verified against legacy handlers.";
    }
}
