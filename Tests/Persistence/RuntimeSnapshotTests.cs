using Atria.Game;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Persistence;

internal static class RuntimeSnapshotTests
{
    public static void Run(string root)
    {
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Runtime snapshot: " + message);
            }
        }

        void Reject(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                return;
            }

            throw new InvalidOperationException("Corrupt runtime snapshot accepted.");
        }

        var stack = new ModeStack();
        stack.StartNewGame();
        stack.Push(320);
        stack.Push(200);
        stack.Push(220);
        Check(stack.LegacySaveMode() == 400, "native46fd14+depth*4 selects slot depth-3");
        var bank = stack.CaptureRuntime();
        bank.Slots[10] = 12345;
        var restored = new ModeStack();
        restored.RestoreRuntime(bank);
        Check(restored.CaptureRuntime().Slots[10] == 12345, "inactive mode slots retained");
        Reject(() => restored.RestoreRuntime(bank with { Slots = new int[10] }));
        var random = new LegacyRandom(1);
        Check(random.Next() == 41, "native CRT seed1 first result");
        var rng = random.CaptureRuntime();
        var transport = new SnapshotMusicTransport();
        var cadence = new Atria.Audio.MusicController(transport);
        cadence.RestoreRuntime(new(true, 10));
        cadence.Update(10);
        Check(transport.Plays == 0, "retained alternate pass does not play early");
        cadence.Update(10);
        Check(transport.Plays == 1, "next native active pass restarts a stopped cached track");
        var continuation = new LegacyRandom(0);
        continuation.RestoreRuntime(rng);
        Check(continuation.State == 2745024 && continuation.Calls == 1 && continuation.Next() == 18467, "resume original CRT state without reseeding call count");
        var torches = new DungeonTorch[20];
        torches[19] = new(false, -123, 456, 789, -42);
        torches[7] = new(true, 4, 5, 6, 7);
        var visibility = new DungeonVisibility();
        var values = Enumerable.Range(0, 100).Select(i => i % 3).ToArray();
        var scratch = Enumerable.Range(20, 9).ToArray();
        visibility.RestoreRuntime(new(values, torches, 19, scratch));
        values[0] = 99;
        torches[19] = default;
        scratch[0] = 0;
        Check(visibility.Values[0] == 0 && visibility.GetTorchBank()[19] == new DungeonTorch(false, -123, 456, 789, -42) && visibility.NextTorch == 19
&& visibility.ScratchNeighbor(0) == 20, "owned banks preserve inactive torch payload, active torch, ring and neighbor scratch");
        Reject(() => visibility.RestoreRuntime(visibility.CaptureRuntime() with { NextTorch = 20 }));
        using var amf = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
        byte[] raw = amf.ReadMember(amf.FindResource("ground1.amf"u8));
        var map = WorldMapContext.Parse(raw);
        using var ani = BinderArchive.Open(Path.Combine(root, "ANI.000"));
        var animation = AnimationFile.Decode(ani.ReadMember(ani.FindResource("CHIPCHAR"u8)));
        var state = new GameState
        {
            ModeStackDepth = 5,
            DungeonFogDirty = 9
        };
        var dungeon = new DungeonController(state, new DungeonMap(map), animation, random, initializeState: false);
        Check(state.ModeStackDepth == 5 && state.DungeonFogDirty == 9, "detached dungeon preparation does not write shared state");
        var retained = dungeon.CaptureRuntime() with
        {
            Visibility = visibility.CaptureRuntime(),
            PendingFunction = "retained"u8.ToArray(),
            AmbientCalls = 199,
            RandomAmbientCalls = 349
        };
        dungeon.RestoreRuntime(retained);
        Check(dungeon.CaptureRuntime().AmbientCalls == 199 && dungeon.PendingFunction.Span.SequenceEqual("retained"u8), "ambient and pending-handler banks preserved");
        var town = new TownController();
        var townSnapshot = town.CaptureRuntime() with
        {
            EffectFrames = [1, 2, 3, 4, 5],
            EffectCounters = [6, 7, 8, 9, 10]
        };
        town.RestoreRuntime(townSnapshot, null, null);
        Check(town.CaptureRuntime().EffectCounters[4] == 10, "retained town effects need not have an active town scene");
        byte[] endingSource = File.ReadAllBytes(Path.Combine(root, "END.DAT"));
        var ending = Atria.Game.Termination.EndingText.Parse(endingSource, 2);
        Check(ending[0][..12].SequenceEqual(endingSource.AsSpan(16100, 12)), "ending464020 alias preserves first12 raw bytes at native section2 offset16100");
        var retainedMenu = new Atria.Game.Menu.MainMenuController();
        retainedMenu.RestoreRetained(new(1, 3, 2));
        retainedMenu.Enter();
        Check(retainedMenu.CaptureRetained() == new Atria.Game.Menu.MainMenuRetainedSnapshot(1, 3, 2), "main200 entry retains native reveal cursor/counter/confirmation flash");
        var retainedCredits = new Atria.Game.Credits.CreditsController();
        retainedCredits.RestoreRetained(new(9));
        retainedCredits.Enter();
        Check(retainedCredits.SpeedCounter == 9, "credits entry retains native speed counter");
        var retainedDefeat = new Atria.Game.Termination.DefeatController();
        retainedDefeat.RestoreRetained(new(17));
        retainedDefeat.AdvanceFrame();
        Check(retainedDefeat.DrawCounter == 18, "defeat counter resumes retained phase");
        Reject(() => retainedMenu.RestoreRetained(new(2, 7, 0)));
        Reject(() => retainedCredits.RestoreRetained(new(11)));
        Reject(() => retainedDefeat.RestoreRetained(new(21)));
        var legacyRaw = Enumerable.Repeat((byte)0x5a, LegacySaveImage.Size).ToArray();
        "ATRIA-II SAVE FILE (c)Jamie system development\0"u8.CopyTo(legacyRaw);
        legacyRaw[257] = 2;
        legacyRaw[258] = 4;
        legacyRaw[259] = 1;
        var legacy = LegacySaveImage.Parse(legacyRaw);
        var mappedSettings = legacy.ReadSettings();
        mappedSettings.Set(3, 1, 2);
        var settingsPatched = legacy.WithSettings(mappedSettings).Encode();
        Check(settingsPatched[257] == 3 && settingsPatched[258] == 1 && settingsPatched[259] == 2 && settingsPatched[256] == 0x5a
&& settingsPatched[260] == 0x5a && settingsPatched[356] == 0x5a, "legacy settings use exact257..259 offsets and preserve opaque prefix/remainder");
        var snapshot = new SessionRuntimeSnapshot
        {
            SavedMode = 400,
            Modes = stack.CaptureRuntime(),
            Random = rng,
            CurrentMap = new("ground1.amf"u8.ToArray(), map.GetHeader(), map.GetTiles()),
            CurrentMapKind = 0,
            Dialogue = new(true, 80, 3),
            Music = new(true, 10),
            PlayingMusicTrack = 10,
            Location = new(1, "01mercen.sod"u8.ToArray(), "01mercen.scr"u8.ToArray(), 0),
            Town = town.CaptureRuntime(),
            Exploration = null,
            Dungeon = null,
            ScriptMember = "01mercen.scr"u8.ToArray(),
            PendingHandler = [],
            TownName = "retained town"u8.ToArray(),
            ReturnMap = "ground1.amf"u8.ToArray(),
            TownFile = "01mercen.sod"u8.ToArray(),
            PreviousTown = 1,
            MusicId = 3,
            LabelDelay = -3,
            ObjectActionField = 17,
            EntryTextMode = 4,
            SelectedEquipmentActor = 2,
            AwardMusicTrack = 9,
            SaveDisabled = false,
            StatusAlternateJournalHeading = true,
            Settings = [1, 2, 4, 1],
            FrameThreshold = 17,
            EndingTextPrefix = [2, 0, 0, 0, 99, 0, 0, 0, 1, 0, 0, 0],
            MainMenu = new(1, 7, 2),
            Credits = new(9),
            Defeat = new(17),
            RetainedBattleCounter = 1,
            RetainedBattleVisualDetail = 3,
            RetainedBattleRenderPeriod = 205
        };
        var encoded = SessionRuntimeCodec.Encode(snapshot);
        var decoded = SessionRuntimeCodec.Decode(encoded);
        var expandedMusic = SessionRuntimeCodec.Decode(SessionRuntimeCodec.Encode(snapshot with
        {
            Music = new(false, 40),
            MusicId = 40,
            PlayingMusicTrack = 40
        }));
        Check(expandedMusic.Music.CachedTrack == 40 && expandedMusic.MusicId == 40 && expandedMusic.PlayingMusicTrack == 40,
            "music IDs are validated against files at activation, not a fixed count in the save codec");
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { PlayingMusicTrack = -1 }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { Music = new(false, 0) }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { Music = new(false, -2) }));
        Check(decoded.RetainedBattleVisualDetail == 3 && decoded.RetainedBattleRenderPeriod == 205, "Nondefault native detail and render period survive session save context");
        Check(decoded.RetainedBattleCounter == 1 && decoded.MainMenu.Flash == 2 && decoded.Credits.SpeedCounter == 9 && decoded.Defeat.DrawCounter == 17
&& decoded.EndingTextPrefix[4] == 99 && decoded.FrameThreshold == 17 && decoded.Dialogue.QuestionInput && decoded.Dialogue.PanelX == 80 && decoded.Dialogue.Selection == 3 && decoded.Random == rng && decoded.Town.EffectFrames[4] == 5 && decoded.ObjectActionField == 17 && decoded.Modes.Slots[3] == 220 && decoded.Settings[2] == 4, "NativeAOT typed context retains exact runtime bank values");
        using (var json = System.Text.Json.JsonDocument.Parse(encoded.Payload))
        using (var missing = new MemoryStream())
        {
            using (var writer = new System.Text.Json.Utf8JsonWriter(missing))
            {
                writer.WriteStartObject();
                foreach (var property in json.RootElement.EnumerateObject())
                {
                    if (property.Name != "MainMenu")
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            Reject(() => SessionRuntimeCodec.Decode(new NativeRuntimeContext("atria.session", 1, missing.ToArray())));
        }

        Reject(() => SessionRuntimeCodec.Decode(new NativeRuntimeContext("atria.session", 2, encoded.Payload.Span)));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { CurrentMap = snapshot.CurrentMap with { Tiles = new byte[32799] } }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { Settings = [1, 5, 4, 1] }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { FrameThreshold = 14 }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { RetainedBattleCounter = 2 }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { RetainedBattleVisualDetail = 4 }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { RetainedBattleRenderPeriod = 26 }));
        Reject(() => SessionRuntimeCodec.Encode(snapshot with { RetainedBattleRenderPeriod = 245 }));
        var checkpoint = new Atria.Script.ScriptCheckpoint("fixture.scr", "run", new string('A', 64), 3, 410, [new("get_money", 17, [new(false, 25, [])], 0)]);
        var extended = snapshot with
        {
            ExactResume = true,
            SaveReason = "После боя",
            Script = checkpoint,
            NextBattleSeed = 12345,
            ScriptReturnLocation = snapshot.Location
        };
        var resumed = SessionRuntimeCodec.Decode(SessionRuntimeCodec.Encode(extended));
        Check(resumed.ExactResume && resumed.SaveReason == "После боя" && resumed.NextBattleSeed == 12345
&& resumed.Script!.Calls[0].Arguments[0].Integer == 25 && resumed.ScriptReturnLocation!.SceneIndex == 1, "AOT checkpoint source generation preserves extended context");
        Reject(() => SessionRuntimeCodec.Encode(extended with { Script = checkpoint with { Calls = null! } }));
        Reject(() => SessionRuntimeCodec.Encode(extended with { Script = checkpoint with { Calls = [null!] } }));
        Reject(() => SessionRuntimeCodec.Encode(extended with { Script = checkpoint with { Member = null! } }));
        using (var json = System.Text.Json.JsonDocument.Parse(encoded.Payload))
        using (var old = new MemoryStream())
        {
            using (var writer = new System.Text.Json.Utf8JsonWriter(old))
            {
                writer.WriteStartObject();
                foreach (var property in json.RootElement.EnumerateObject())
                {
                    if (property.Name is not ("ExactResume" or "SaveReason" or "Script" or "Interior" or "ScriptReturnLocation" or "NextBattleSeed"))
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            Check(!SessionRuntimeCodec.Decode(new("atria.session", 1, old.ToArray())).ExactResume, "pre-autosave schema remains loadable");
        }

        VerifyLibrarySnapshot(root, snapshot);
        Atria.Tests.Localization.SaveLocalizationTests.Verify(root, snapshot, new DungeonController(new GameState(), new DungeonMap(map), animation, new LegacyRandom(1), initializeState: false).CaptureRuntime());
    }

    private static void VerifyLibrarySnapshot(string root, SessionRuntimeSnapshot template)
    {
        var townAssets = TownSceneAssets.Load(root, "32tiger.sod"u8);
        var town = new TownController();
        town.SetSavedSelection(4);
        town.Enter(townAssets.Scene, townAssets.Objects, 32, 32, "32tiger.sod"u8.ToArray(), default,
            TownCatalog.Get(32).MusicTrack, _ => { });
        var doorway = townAssets.Scene.GetObject(town.Selection);
        var libraryAssets = TownSceneAssets.Load(root, doorway.GetString(0).Span);
        var interior = new InteriorController();
        interior.Enter(GameMode.Interior, doorway, libraryAssets.Scene, _ => { });
        interior.HandleInput(false, _ => { });
        var stack = new ModeStack();
        stack.StartNewGame();
        stack.Push(GameMode.Interior);
        var snapshot = template with
        {
            SavedMode = GameMode.Interior,
            ExactResume = true,
            Modes = stack.CaptureRuntime(),
            Location = new(32, "32tiger.sod"u8.ToArray(), "32tiger.scr"u8.ToArray(), 0),
            Town = town.CaptureRuntime(),
            Interior = interior.CaptureRuntime(),
            ScriptMember = libraryAssets.Scene.ScriptReference.ToArray(),
            TownFile = "32tiger.sod"u8.ToArray(),
            PreviousTown = 32
        };
        var liveState = new GameState { Money = 12345, Mode = GameMode.MainMenu };
        byte[] liveStateBefore = NativeGameStateCodec.Encode(liveState);
        var savedState = new GameState { Mode = GameMode.Interior };
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true);
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var loader = new SessionSnapshotLoader(root, liveState, new LegacyRandom(1), font, messages, items, _ => { });
        byte[] source = GameResources.Read(root, "SCRIPT.000", snapshot.ScriptMember);
        var checkpoint = new ScriptCheckpoint("32_LIBRA", "enter", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)), 0, GameMode.Interior, []);
        var metadata = LegacySlotMetadata.Empty([]).WithCurrent("Library"u8, "Tiger Castle"u8, GameMode.Interior, 0, 32, 1, []);
        var shops = LegacyShopProjection.DecodeAll(new byte[30 * 612]);
        foreach (ScriptCheckpoint? replay in new ScriptCheckpoint?[] { null, checkpoint })
        {
            NativeRuntimeContext encoded = SessionRuntimeCodec.Encode(snapshot with { Script = replay });
            var image = NativeSaveImage.Capture(savedState, GameMode.Interior, new WorldAreaStore(), shops, metadata, encoded);
            var loaded = NativeSaveImage.Parse(image.Encode());
            PreparedSession prepared = loader.Prepare(loaded.Context, loaded.State);
            var restored = new InteriorController();
            restored.RestoreRuntime(prepared.Snapshot.Interior!, prepared.InteriorAssets!.Scene);
            if (restored.CaptureRuntime() != snapshot.Interior || prepared.Town.Selection != 4
                || !prepared.Snapshot.ScriptMember.AsSpan().SequenceEqual("32_libra.scr"u8)
                || !NativeGameStateCodec.Encode(liveState).AsSpan().SequenceEqual(liveStateBefore))
            {
                throw new InvalidOperationException("Library save preparation must restore the real interior without changing live state or saved script identity.");
            }
        }
        Console.WriteLine("Library snapshot: serialized interior load, canonical checkpoint plus filename validation, controller restoration and untouched live state passed.");
    }

    private sealed class SnapshotMusicTransport : Atria.Audio.IMusicTransport
    {
        public int Plays;
        public Atria.Audio.MusicPlaybackState State => Atria.Audio.MusicPlaybackState.Stopped;

        public void Stop()
        {
        }

        public void Play(int track)
        {
            Plays++;
        }
    }
}
