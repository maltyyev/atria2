using System.Text;
using Atria.Audio;
using Atria.Game.Dialogue;
using Atria.Game.Menu;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Platform;
using Atria.Platform.Win32;
using Atria.Render;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    internal static string VerifyGeographerIntegration(string root, string musicRoot, string fixture, bool guild = false)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(root, "..")), "audit", "implementation", guild ? "gatha-guild" : "geographer", Guid.NewGuid().ToString("N"));
        using var window = new NativeWindow("Atria geographer verification", 2560, 1440);
        using var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, 0);
        using var faces = new FaceSpriteSource(Path.Combine(root, "face.SPR"));
        var s = new GameSession(root, window, presenter, audio, transport, faces, 30, true, null, output);
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException("Geographer session: " + message);
            }
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(output, "saves"));
            File.Copy(fixture, s.PersistenceStore.GetSlotPath(0));
            s.LoadSessionSlot(0);
            Check(s.state.Mode == GameMode.Town && s.town.TownId == (guild ? 2 : 3), "user fixture is expected town");
            int objectIndex = guild ? 3 : 4;
            var selected = s.town.CaptureRuntime() with
            {
                Selection = objectIndex,
                SavedSelection = objectIndex
            };
            s.town.RestoreRuntime(selected, s.townAssets!.Scene, s.townAssets.Objects);
            s.town.ChooseAction(0, s.HandleTown);
            Check(s.state.Mode == GameMode.Interior && s.state.ScriptActive == 1, "actual doorway queues interior enter");
            s.ComposeScene(s.state, s.host.FrameBuffer);
            s.host.Present();
            window.Pump();
            s.RunScript(s.scriptMember, s.pendingHandler);
            Check(s.state.ScriptActive == 0 && s.scriptMember.AsSpan().SequenceEqual(guild ? "02_eve1.scr"u8 : "03_eve1.scr"u8), "queued scenario completed");
            s.interior.HandleInput(false, s.HandleInterior);
            if (guild)
            {
                Console.WriteLine("User guild quest statuses7..12: " + string.Join(",", s.state.EventStatuses.Skip(7).Take(6)));
                Check(s.state.People.Count == 0 && Atria.Game.Interiors.InteriorEventOffers.Select(s.state, s.interior.EventRange) is null, "user save has no actual talk choices");
                for (int i = 0; i < 8; i++)
                {
                    s.actions.Input(new(Accept: true), s.HandleAction);
                    Check(s.actions.Depth == 0 && s.actions.AdvanceLogicalFrame()!.Type == 3 && s.state.ScriptActive == 0, "repeated Talk stays safe and does not requeue enter");
                }
            }
            else
            {
                Check(Atria.Game.Interiors.InteriorEventOffers.Select(s.state, s.interior.EventRange)?.EventId == 13, "first geographer quest remains offered");
            }

            var before = s.CaptureSaveImage(410, true, "Geographer");
            s.WriteAutosave("Geographer");
            var row = s.PersistenceStore.InspectLibrary(false).First(r => r.Automatic);
            s.LoadSessionSlot(row.Index);
            Check(NativeGameStateCodec.Encode(s.state).AsSpan().SequenceEqual(NativeGameStateCodec.Encode(before.State)), "interior autosave reload state equality");
            Check(s.CaptureSaveImage(410, true, "Geographer").Context.Payload.Span.SequenceEqual(before.Context.Payload.Span), "interior autosave reload context equality");
            if (guild)
            {
                s.actions.Input(new(Accept: true), s.HandleAction);
                Check(s.actions.AdvanceLogicalFrame()!.Type == 3 && s.state.ScriptActive == 0, "empty Talk remains safe after reload");
            }

            s.ComposeScene(s.state, s.host.FrameBuffer);
            s.host.Present();
            window.Pump();
            WriteProbeBitmap(Path.Combine(output, "geographer.bmp"), s.host.FrameBuffer);
            s.LeaveInterior();
            Check(s.state.Mode == GameMode.Town, "pre_out returns to town");
            s.ComposeScene(s.state, s.host.FrameBuffer);
            s.host.Present();
            window.Pump();
            return (guild ? "Gatha guild native session passed: isolated user save, real entry, exhausted quest range, repeated Talk before/after interior autosave reload, state/context equality, exit and D3D11 frames." : "Geographer native session passed: isolated user-save load, actual doorway/queued script, quest13 offer, interior autosave/reload full state/context equality, exit to Uga and D3D11 frames.") + " Evidence: " + output;
        }
        finally
        {
            s.DisposeProgression();
            s.controls.Dispose();
            s.StopAudio();
        }
    }

    // Internal integration harness: no command exposed by the player executable; writes only to a fresh test directory.
    internal static string VerifyPersistenceIntegration(string root, string musicRoot, string fixture)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(root, "..")), "audit", "implementation", "session-autosave", Guid.NewGuid().ToString("N"));
        using var window = new NativeWindow("Atria persistence verification", 2560, 1440);
        using var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, 0);
        using var faces = new FaceSpriteSource(Path.Combine(root, "face.SPR"));
        var s = new GameSession(root, window, presenter, audio, transport, faces, 120, true, null, output);
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException("Session persistence: " + message);
            }
        }

        void Roundtrip(string reason)
        {
            s.modes.ResetDepth();
            s.modes.Push(s.state.Mode);
            s.state.ModeStackDepth = 0;
            var before = s.CaptureSaveImage(s.state.Mode, true, reason);
            s.WriteAutosave(reason);
            var row = s.PersistenceStore.InspectLibrary(false).First(r => r.Automatic);
            Check(row.Reason == reason, "checkpoint committed " + reason);
            s.state.Money += 999;
            s.LoadSessionSlot(row.Index);
            Check(NativeGameStateCodec.Encode(s.state).AsSpan().SequenceEqual(NativeGameStateCodec.Encode(before.State)), "full game state restored " + reason);
            var after = s.CaptureSaveImage(s.state.Mode, true, reason);
            Check(after.Context.Payload.Span.SequenceEqual(before.Context.Payload.Span), "runtime context restored " + reason);
            Check(LegacyShopProjection.EncodeAll(after.Shops).AsSpan().SequenceEqual(LegacyShopProjection.EncodeAll(before.Shops)), "shop state restored");
            for (int i = 0; i < 8; i++)
            {
                Check(after.Areas.GetPayload(i).AsSpan().SequenceEqual(before.Areas.GetPayload(i)), "area bank restored");
            }

            s.ComposeScene(s.state, s.host.FrameBuffer);
            s.host.Present();
            window.Pump();
            Console.WriteLine("Session checkpoint roundtrip: " + reason);
        }

        try
        {
            if (File.Exists(fixture))
            {
                Directory.CreateDirectory(Path.Combine(output, "saves"));
                File.Copy(fixture, s.PersistenceStore.GetSlotPath(0));
                s.LoadSessionSlot(0);
                Check(s.state.Mode is GameMode.World or GameMode.Dungeon or GameMode.Town, "old user save activation");
                Console.WriteLine("Existing user save loaded from isolated copy.");
            }

            s.HandleMenu(new(MenuEffect.ResetGame));
            s.HandleMenu(new(MenuEffect.SetNewGameGlobals));
            s.HandleMenu(new(MenuEffect.LoadGroundMap, Resource: "ground1.amf"));
            s.HandleMenu(new(MenuEffect.InitializeGroundMap, 0));
            s.HandleMenu(new(MenuEffect.CopyInitialGroundLocation));
            s.modes.StartNewGame();
            s.EnterMode(GameMode.Town);
            // Exercise storage and resource activation independently of the modal opening dialogue.
            s.state.ScriptActive = 0;
            s.pendingHandler = [];
            s.state.Globals[501] = 1;
            s.autosavePending = "";
            Roundtrip("Town");
            var townSave = s.CaptureSaveImage(400, true, "");
            s.PersistenceStore.WriteNative(20, townSave);
            int checkedInteriors = 0;
            for (int i = 0; i < s.town.Scene.Count; i++)
            {
                s.LoadSessionSlot(20);
                int kind = s.town.Scene.GetObject(i).GetInteger(7);
                if (kind is < 0 or > 2)
                {
                    continue;
                }

                var townState = s.town.CaptureRuntime() with
                {
                    Selection = i,
                    SavedSelection = i
                };
                s.town.RestoreRuntime(townState, s.townAssets!.Scene, s.townAssets.Objects);
                s.EnterMode(GameMode.Interior + kind * 10);
                s.state.ScriptActive = 0;
                s.pendingHandler = [];
                Roundtrip("Interior " + s.state.Mode);
                checkedInteriors++;
            }

            Check(checkedInteriors > 0, "interior checkpoints covered");
            s.LoadSessionSlot(20);
            s.LoadExplorationReturnMap("ground1.amf"u8.ToArray());
            s.EnterMode(GameMode.World);
            s.state.ScriptActive = 0;
            Roundtrip("World");
            // Real dungeon resource, with the original town-script entry and return path.
            byte[] dungeonBytes = s.Read("AT2AMF.000", "ed00.amf"u8);
            var dungeonMap = AmfMap.Parse(dungeonBytes);
            s.state.PendingDungeonMap = "ed00.amf"u8.ToArray();
            s.state.PendingDungeonScript = dungeonMap.EmbeddedScene!.ScriptReference.ToArray();
            s.state.DungeonEntryPending = 1;
            s.PrepareDungeon(false);
            s.EnterMode(GameMode.Dungeon);
            Roundtrip("Dungeon");
            s.state.PendingBattle = 2;
            s.state.BattleVariant = 0;
            s.state.EndingResetFlag = 1;
            s.nextBattleSeed = 42;
            Roundtrip("Queued battle");
            Check(s.nextBattleSeed == 42 && s.state.EndingResetFlag == 1, "queued battle and seed retained");
            s.state.EndingResetFlag = 0;
            s.nextBattleSeed = null;
            // Quest acceptance and repeated completion do not enqueue checkpoints.
            var scriptHost = new Atria.Script.CheckpointScriptHost(new ScriptStateHost(s.state, s))
            {
                BeforeCall = s.BeforeScenarioCall,
                AfterCall = s.AfterScenarioCall
            };
            s.autosavePending = "";
            foreach (int status in new[]
            {
                1,
                4,
                4
            }
            )
            {
                s.autosavePending = "";
                int previous = s.state.EventStatuses[4];
                scriptHost.Invoke(new("fixture", "set_eventstatus", 0, [new(4), new(status)]));
                Check((s.autosavePending.Length > 0) == (status == 4 && previous != 4), "quest completion edge only");
            }

            s.RequestAutosave("Задание завершено");
            s.FlushAutosave();
            Check(s.autosavePending.Length == 0, "quest flush committed");
            s.EnterMode(GameMode.Save);
            s.ComposeScene(s.state, s.host.FrameBuffer);
            s.host.Present();
            Directory.CreateDirectory(output);
            WriteProbeBitmap(Path.Combine(output, "save-menu.bmp"), s.host.FrameBuffer);
            s.EnterMode(GameMode.Settings);
            s.ComposeScene(s.state, s.host.FrameBuffer);
            s.host.Present();
            WriteProbeBitmap(Path.Combine(output, "settings.bmp"), s.host.FrameBuffer);
            return $"Session integration passed: old-save copy, town/{checkedInteriors} interiors/world/dungeon/pending battle, quest edge, full state/context/shop/area equality and real D3D11 frames. Evidence: {output}";
        }
        finally
        {
            s.DisposeProgression();
            s.controls.Dispose();
            s.StopAudio();
        }
    }

    private static void WriteProbeBitmap(string path, ReadOnlySpan<ushort> pixels)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4d42);
        writer.Write(54 + 640 * 480 * 3);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(640);
        writer.Write(480);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(640 * 480 * 3);
        writer.Write(new byte[16]);
        for (int y = 479; y >= 0; y--)
        {
            for (int x = 0; x < 640; x++)
            {
                int v = pixels[y * 640 + x];
                writer.Write((byte)((v & 31) * 255 / 31));
                writer.Write((byte)(((v >> 5) & 63) * 255 / 63));
                writer.Write((byte)((v >> 11) * 255 / 31));
            }
        }
    }
}
