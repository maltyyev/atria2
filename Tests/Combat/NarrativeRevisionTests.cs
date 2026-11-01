using System.Security.Cryptography;
using System.Text;
using Atria.Game.Dialogue;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;
using Atria.Tests.Render;

namespace Atria.Tests.Combat;

internal static class NarrativeRevisionTests
{
    private sealed class Effects : IScriptEffects
    {
        public List<string> Lines { get; } = [];
        public List<(string Key, int Placement, int Face)> Turns { get; } = [];
        public List<int> Battles { get; } = [];
        public List<string> Timeline { get; } = [];

        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Call.Function == "dialogue_text")
            {
                string key = Encoding.ASCII.GetString(effect.Call.Arguments[2].Bytes.Span);
                Lines.Add(key);
                Timeline.Add(key);
                Turns.Add((key, effect.Call.Arguments[0].Integer, effect.Call.Arguments[1].Integer));
            }
            else if (effect.Kind == ScriptEffectKind.ForcedBattleWithPostAction)
            {
                Battles.Add(effect.Call.Arguments[0].Integer);
                Timeline.Add("battle:" + effect.Call.Arguments[0].Integer);
                state.PendingBattle = state.EndingResetFlag = 0;
            }
            else
            {
                throw new InvalidOperationException("Unexpected narrative effect.");
            }
        }
    }

    public static string Run(string root, string? manualSave = null)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        using var legacy = GameData.Open("NarrativeSceneLegacySources");
        var currentState = InitialGameState.Create(items, messages);
        int resumes = 0;
        int partyVariants = 0;
        var keys = new HashSet<string>();
        var turns = new Dictionary<string, (int Placement, int Face)>();
        string[] companions = ["mist", "swallow", "brian", "emerald", "tiger"];
        var heroFaces = new Dictionary<int, string> { [1] = "roland", [3] = "mist", [4] = "swallow", [5] = "brian", [2] = "emerald", [8] = "tiger" };
        var heard = new HashSet<string>();
        foreach (var scene in NarrativeScenes.All)
        {
            currentState = InitialGameState.Create(items, messages);
            if (scene.Conditions.TryGetProperty("quests", out var phases))
                foreach (var phase in phases.EnumerateArray())
                    if (phase[1].GetInt32() != 0)
                        currentState.Journal.Restore(phase[0].GetInt32(), (QuestMilestones)phase[1].GetInt32());
            byte[] beforeScene = NativeGameStateCodec.Encode(currentState);
            Check(GameResources.Read(root, "SCRIPT.000", Encoding.ASCII.GetBytes(scene.Id)).AsSpan().SequenceEqual(scene.Source), "new scene resolves revised source");
            var newEffects = Execute(scene.Id, scene.Source, currentState, capture: true);
            foreach (var edition in legacy.RootElement.GetProperty(scene.Id).EnumerateArray())
            {
                byte[] oldSource = Encoding.ASCII.GetBytes(edition.GetString()!);
                string oldHash = Convert.ToHexString(SHA256.HashData(oldSource));
                byte[] selected = GameResources.Read(root, "SCRIPT.000", Encoding.ASCII.GetBytes(scene.Id + ".scr"), oldHash);
                Check(selected.AsSpan().SequenceEqual(oldSource), "old continuation resolves exact source");
                var previousState = NativeGameStateCodec.Decode(beforeScene);
                var oldEffects = Execute(scene.Id, selected, previousState, capture: true);
                Check(oldEffects.Turns.Count > 0, "archived scene remains executable");
            }

            if (scene.Id == "NAR0001")
            {
                Check(newEffects.Lines.Count is >= 8 and <= 10 && newEffects.Turns[0] == ("eiren.revised.greeting.text", 1, 33), "opening starts with the mason portrait and stays concise");
            }
            if (scene.Id == "NAR0002")
            {
                string[] ordered = ["eiren.staging.arrival.text", "eiren.revised.challenge.text", "battle:906",
                    "eiren.staging.copyDefiance.text", "eiren.staging.copyQuestion.text", "eiren.staging.copyAnswer.text",
                    "battle:907", "eiren.staging.rescue.text", "eiren.staging.worker.text", "eiren.staging.lookInside.text",
                    "eiren.staging.outside.text", "eiren.staging.workerFear.text", "eiren.staging.escort.text"];
                int previous = -1;
                foreach (string turn in ordered)
                {
                    int index = newEffects.Timeline.IndexOf(turn);
                    Check(index > previous, "storehouse introduction, two battles, spoken copying explanation and rescue order: " + turn);
                    previous = index;
                }
            }

            if (scene.Id == "NAR0018")
            {
                string[] ordered = ["eiren.seals.humanArrival.text", "eiren.seals.guardOrder.text",
                    "eiren.seals.mistReach.text", "eiren.seals.guardRepeat.text", "eiren.seals.mistTruth.text",
                    "eiren.seals.rolandFight.text", "battle:916", "eiren.seals.humanAfter.text",
                    "eiren.seals.rolandRegret.text", "eiren.seals.mistRegret.text", "eiren.expedition.seal2Broken.text"];
                int previous = -1;
                foreach (string turn in ordered)
                {
                    int index = newEffects.Timeline.IndexOf(turn);
                    Check(index > previous, "captives are approached before combat and mourned only after victory: " + turn);
                    previous = index;
                }
            }

            if (scene.Id == "NAR0020")
            {
                Check(newEffects.Battles.SequenceEqual([918]), "current finale uses the three revised guard waves");
                Check(newEffects.Timeline.IndexOf("eiren.revised.challengeMotive.text") < newEffects.Timeline.IndexOf("battle:918"),
                    "Eiren is confronted about his crimes before the final battle");
            }

            for (int mask = 0; mask < 1 << companions.Length; mask++)
            {
                var variant = NativeGameStateCodec.Decode(beforeScene);
                for (int i = 0; i < companions.Length; i++)
                {
                    variant.Heroes[variant.HeroCatalog.Find(companions[i])].MemberStatus = (mask & (1 << i)) == 0 ? 0 : 1;
                }
                if (scene.Conditions.TryGetProperty("all", out var required)
                    && required.EnumerateArray().Any(id => !variant.HasHero(id.GetString()!)))
                {
                    continue;
                }
                var variantEffects = Execute(scene.Id, scene.Source, variant, capture: false);
                if (scene.Id == "NAR0002")
                {
                    bool mist = variant.HasHero("mist");
                    Check(variantEffects.Lines.Contains("eiren.staging.notesToMist.text") == mist
                        && variantEffects.Lines.Contains("eiren.staging.notesMist.text") == mist
                        && variantEffects.Lines.Contains("eiren.staging.notesAlone.text") != mist,
                        "discovery includes only present companions and has a solo fallback");
                }
                foreach (var turn in variantEffects.Turns)
                {
                    if (heroFaces.TryGetValue(turn.Face, out string? speaker))
                    {
                        Check(variant.HasHero(speaker), "absent companion cannot speak: " + turn.Key);
                        heard.Add(speaker);
                    }
                    if (turn.Key == "eiren.party.recognizeAnswer.text")
                    {
                        Check(variant.HasHero("mist") && variantEffects.Lines.Contains("eiren.party.mistRecognize.text"), "Eiren answers only a present Mist");
                    }
                    turns[turn.Key] = (turn.Placement, turn.Face);
                    keys.Add(turn.Key);
                }
                partyVariants++;
            }
        }
        Check(companions.All(heard.Contains), "every requested companion participates");

        try
        {
            NarrativeScenes.Read("NAR0001"u8, new string('0', 64));
            throw new InvalidOperationException("Unknown source hash was accepted.");
        }
        catch (InvalidDataException)
        {
        }

        var previousLanguage = GameText.Current;
        using var faces = new FaceSpriteSource(Path.Combine(root, "face.SPR"));
        using var sprites = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        var tiles = SpriteFile.Decode(sprites.ReadMember(sprites.FindResource("ETC"u8)));
        string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "eiren-staging-01512"));
        Directory.CreateDirectory(output);
        try
        {
            foreach (string locale in new[] { "ru-RU", "be-BY" })
            {
                var catalog = new LocalizationCatalog(Path.Combine(root, "..", "Localization"), locale);
                Check(catalog.Issues.Count == 0, "complete valid locale: " + locale);
                GameText.Use(catalog);
                var capturedFaces = new HashSet<int>();
                Check(catalog.Get("messages.162").Contains(locale == "ru-RU" ? "Саллин" : "Саллін", StringComparison.Ordinal), "consistent town title");
                foreach (string key in keys)
                {
                    string text = catalog.Get(key);
                    Check(text.Length > 0 && text.Count(character => character == '[') <= 1, "one speaker label per turn: " + key);
                    var turn = turns[key];
                    bool speech = text.StartsWith('[');
                    Check(speech ? turn.Placement is 0 or 1 && turn.Face >= 0 : turn.Placement == -1 && turn.Face == -1, "only narration has no portrait: " + key);
                    if (heroFaces.TryGetValue(turn.Face, out string? hero))
                    {
                        Check(text.StartsWith($"[{catalog.Get("ui.characters." + hero)}]", StringComparison.Ordinal), "portrait matches the speaking hero: " + key);
                    }
                    var request = new DialogueRequest(turn.Placement, turn.Face, false, []) { TextKey = key, Paragraph = text };
                    var session = new DialogueSession(request, false);
                    session.PrepareLayout(font);
                    Check(session.Request.DisplayLines!.All(line => font.MeasureText(line) <= 440), "portrait-width wrapping: " + key);
                    var face = speech ? faces.Load(turn.Face) : null;
                    for (int i = 0; i < 8; i++) session.AdvancePresentation();
                    var pixels = Enumerable.Repeat((ushort)0x2925, 640 * 480).ToArray();
                    DialogueRaster.Draw(session.Request, session.AdvancePresentation(), tiles, face, font, pixels);
                    if (speech && (capturedFaces.Add(turn.Face) || key == "eiren.staging.notesMist.text"))
                    {
                        UnicodeTextTests.SaveBitmap(Path.Combine(output, $"{locale}-{key}.bmp"), pixels);
                    }
                }
            }
        }
        finally
        {
            GameText.Use(previousLanguage);
        }

        if (manualSave is not null)
        {
            var image = NativeSaveImage.Parse(File.ReadAllBytes(manualSave));
            var loader = new SessionSnapshotLoader(root, new GameState(), new LegacyRandom(1), font, messages, items, _ => { });
            _ = loader.Prepare(image.Context, image.State);
            Check(NarrativeScenes.After(image.State, "43SALIN", "enter")?.Id == "NAR0001", "actual manual save precedes the opening");
            var snapshot = SessionRuntimeCodec.Decode(image.Context);
            var scene = NarrativeScenes.All[0];
            byte[] source = Encoding.ASCII.GetBytes(legacy.RootElement.GetProperty(scene.Id)[0].GetString()!);
            var checkpoint = new ScriptCheckpoint(scene.Id, "run", Convert.ToHexString(SHA256.HashData(source)), 0, 0, []);
            _ = loader.Prepare(SessionRuntimeCodec.Encode(snapshot with { Script = checkpoint, ExactResume = true }), image.State);
            Console.WriteLine("Actual manual save and legacy authored continuation prepared without modifying the save.");
        }
        return $"Narrative revision PASS: {keys.Count} turns, both locales/portraits/layout, {partyVariants} party variants, all source editions, {resumes} checkpoint replays, legacy and current continuations replay exactly once.";

        Effects Execute(string member, byte[] source, GameState state, bool capture)
        {
            var program = ScriptProgram.Parse(member, source);
            var effects = new Effects();
            var host = new CheckpointScriptHost(new ScriptStateHost(state, effects));
            var checkpoints = new List<(byte[] State, ScriptCallSnapshot[] Calls)>();
            if (capture)
            {
                host.AfterCall = _ => checkpoints.Add((NativeGameStateCodec.Encode(state), host.Capture()));
            }
            new ScriptMachine(program, host).Invoke("run");
            byte[] expected = NativeGameStateCodec.Encode(state);
            foreach (var checkpoint in checkpoints)
            {
                var saved = new ScriptCheckpoint(member, "run", Convert.ToHexString(SHA256.HashData(source)), 0, 0, checkpoint.Calls);
                byte[] restoredSource = GameResources.Read(root, "SCRIPT.000", Encoding.ASCII.GetBytes(member), saved.SourceHash);
                var restoredProgram = ScriptProgram.Parse(member, restoredSource);
                CheckpointScriptHost.Validate(restoredProgram, saved);
                var resumed = NativeGameStateCodec.Decode(checkpoint.State);
                var replay = new CheckpointScriptHost(new ScriptStateHost(resumed, new Effects()), saved.Calls);
                new ScriptMachine(restoredProgram, replay).Invoke("run");
                replay.VerifyReplayComplete();
                Check(expected.AsSpan().SequenceEqual(NativeGameStateCodec.Encode(resumed)), "resume has no duplicate effects");
                resumes++;
            }
            return effects;
        }
    }

    private static void Check(bool value, string reason)
    {
        if (!value)
        {
            throw new InvalidOperationException("Narrative revision: " + reason);
        }
    }
}
