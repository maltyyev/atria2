using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Atria.Game;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Persistence;

internal static class SessionMigrationTests
{
    public static string Run(string root, string fixtureDirectory)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        var loader = new SessionSnapshotLoader(root, new GameState(), new LegacyRandom(1), font, messages, items, _ => { });
        var files = Directory.GetFiles(fixtureDirectory, "*.sav", SearchOption.TopDirectoryOnly);
        Check(files.Length > 0, "old-save fixtures are required");
        NativeSaveImage? town = null, world = null;
        int count = 0;
        foreach (string path in files)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var image = NativeSaveImage.Parse(bytes);
            var prepared = loader.Prepare(image.Context, image.State);
            var upgraded = SessionRuntimeCodec.Encode(prepared.Snapshot);
            var reloaded = loader.Prepare(upgraded, image.State);
            Check(reloaded.Snapshot.SavedMode == image.State.Mode, "world identity survives migration");
            var json = JsonNode.Parse(upgraded.Payload.Span)!.AsObject();
            Check(upgraded.Version == 2 && !json.ContainsKey("PreviousTown") && !json.ContainsKey("Modes"), "v2 omits obsolete navigation fields");
            Check(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "original save unchanged");
            if (image.State.Mode == GameMode.Town) town ??= image;
            if (image.State.Mode == GameMode.World) world ??= image;
            count++;
        }

        Check(town is not null && world is not null, "town and world migration fixtures required");
        VerifyEarlySnapshot(loader, town!);
        VerifyContinuations(town!, world!);
        VerifyRecoveryCopies(root, town!);
        return $"Session migration: {count} existing native saves prepared as v1/v2 without writes; six legacy continuation values, invalid route rejection and byte-identical overwrite/rotation backups passed.";
    }

    private static void VerifyEarlySnapshot(SessionSnapshotLoader loader, NativeSaveImage town)
    {
        var json = JsonNode.Parse(town.Context.Payload.Span)!.AsObject();
        foreach (string key in new[] { "ExactResume", "SaveReason", "Script", "Interior", "ScriptReturnLocation", "NextBattleSeed", "AdmissionTarget" })
            json.Remove(key);
        json["Modes"] = new JsonObject { ["Depth"] = 3, ["Current"] = GameMode.Save,
            ["Slots"] = new JsonArray(GameMode.Town, 320, GameMode.MainMenu, GameMode.Save, 0, 0, 0, 0, 0, 0, 0) };
        var state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(town.State));
        state.ScriptActive = state.EndingResetFlag = 0;
        var old = new NativeRuntimeContext(SessionRuntimeCodec.Schema, 1, Encoding.UTF8.GetBytes(json.ToJsonString()));
        var prepared = loader.Prepare(old, state);
        Check(!prepared.Snapshot.ExactResume && prepared.Snapshot.SaveReason == ""
            && prepared.TownEntryCommands.Count(c => c.Kind == Atria.Game.Town.TownEffectKind.QueueEnterScript) == 1,
            "pre-autosave v1 data preserves its explicit one-time initialization behavior");
    }

    private static void VerifyContinuations(NativeSaveImage town, NativeSaveImage world)
    {
        foreach (var continuation in Enum.GetValues<ScenarioContinuation>())
        {
            var source = continuation == ScenarioContinuation.EnterTown ? world : town;
            var json = JsonNode.Parse(source.Context.Payload.Span)!.AsObject();
            json.Remove("AdmissionTarget");
            json["ExactResume"] = true;
            json["NextBattleSeed"] = 12345;
            json["Town"]!["PendingPreEnter"] = continuation == ScenarioContinuation.EnterBuilding;
            json["Town"]!["PendingPreOut"] = continuation == ScenarioContinuation.LeaveTown;
            int mode = source.State.Mode;
            if (continuation is ScenarioContinuation.InteriorInteraction or ScenarioContinuation.LeaveBuilding)
            {
                mode = GameMode.Interior;
                json["SavedMode"] = mode;
                json["Interior"] = new JsonObject
                {
                    ["Mode"] = mode,
                    ["EventRange"] = new JsonObject { ["First"] = 0, ["Last"] = 0 },
                    ["FirstEntry"] = false,
                    ["SelectedObject"] = 0
                };
            }
            if (continuation == ScenarioContinuation.EnterTown)
            {
                // The codec case tests conversion, not resource activation of a fabricated destination.
                json["ScriptReturnLocation"] = json["Location"]?.DeepClone();
                json["Location"] = JsonNode.Parse(town.Context.Payload.Span)!["Location"]!.DeepClone();
            }
            json["Script"] = new JsonObject
            {
                ["Member"] = "fixture", ["Function"] = "run", ["SourceHash"] = new string('A', 64),
                ["Continuation"] = (int)continuation,
                ["ContinuationMode"] = continuation == ScenarioContinuation.LeaveBuilding ? mode : 0,
                ["Calls"] = new JsonArray(new JsonObject { ["Function"] = "get_money", ["Offset"] = 17,
                    ["Arguments"] = new JsonArray(new JsonObject { ["IsString"] = false, ["Integer"] = 25, ["Bytes"] = "" }), ["Result"] = 0 })
            };

            var restored = SessionRuntimeCodec.Decode(new(SessionRuntimeCodec.Schema, 1, Encoding.UTF8.GetBytes(json.ToJsonString())));
            Check(restored.Script!.Continuation == continuation && restored.Script.Calls.Length == 1 && restored.NextBattleSeed == 12345,
                "continuation, replay log and battle seed preserved");
            if (continuation == ScenarioContinuation.EnterTown)
                Check(restored.AdmissionTarget is not null, "old candidate location becomes explicit destination");
            var encoded = SessionRuntimeCodec.Encode(restored);
            Check(SessionRuntimeCodec.Decode(encoded).Script!.Continuation == continuation, "semantic continuation roundtrip");
            Reject(() => SessionRuntimeCodec.Encode(restored with { Script = restored.Script with { Continuation = (ScenarioContinuation)999 } }));
        }
    }

    private static void VerifyRecoveryCopies(string root, NativeSaveImage old)
    {
        string directory = Path.GetFullPath(Path.Combine(root, "..", "audit", "migration-backups", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var store = new NativeSaveStore(directory, root);
        byte[] bytes = old.Encode();
        File.WriteAllBytes(store.GetSlotPath(0), bytes);
        File.WriteAllBytes(store.GetSlotPath(-1), bytes);
        File.WriteAllBytes(store.GetSlotPath(-2), bytes);
        var updated = NativeSaveImage.Capture(old.State, old.State.Mode, old.Areas, old.Shops, old.Metadata,
            SessionRuntimeCodec.Encode(SessionRuntimeCodec.Decode(old.Context)), old.LegacyTemplate);
        store.WriteNative(0, updated);
        store.WriteNative(0, updated);
        store.PruneAutomatic(1);
        var backups = Directory.GetFiles(Path.Combine(directory, "migration-backups"), "*.sav");
        Check(backups.Length == 2 && backups.All(path => SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(SHA256.HashData(bytes))),
            "manual overwrite and autosave pruning preserve old bytes once");
        Check(store.ReadSlot(0)!.Native!.Context.Version == 2, "new version committed after backup");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid migration input was accepted.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Migration: " + message);
    }
}
