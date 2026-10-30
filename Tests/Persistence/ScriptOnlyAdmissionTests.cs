using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Atria.Game;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Persistence;

internal static class ScriptOnlyAdmissionTests
{
    public static string Run(string root, string fixture)
    {
        byte[] original = File.ReadAllBytes(fixture);
        var image = NativeSaveImage.Parse(original);
        var snapshot = SessionRuntimeCodec.Decode(image.Context);
        var map = WorldMapContext.Parse(GameResources.Read(root, "AT2AMF.000", snapshot.CurrentMap.Member));
        Check(snapshot.SavedMode == GameMode.World && map.AreaIndex == 6, "fixture is the Salin world area");
        image.State.WorldX = 4;
        image.State.WorldY = 5;
        var destination = map.SelectScene(image.State.WorldX, image.State.WorldY);
        Check(destination.SceneMember.Span.SequenceEqual("44core.sod"u8), "actual Core map destination");
        using var scenes = BinderArchive.Open(Path.Combine(root, "AT2SOD.000"));
        Check(scenes.FindResource(destination.SceneMember.Span) < 0, "Core intentionally has no town view");

        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        var live = new GameState();
        byte[] liveBefore = NativeGameStateCodec.Encode(live);
        var loader = new SessionSnapshotLoader(root, live, new LegacyRandom(1), font, messages, items, _ => { });
        int checkpoints = 0;
        foreach (string member in new[] { "44CORE", "NAR0004", "NAR0008" })
        {
            byte[] source = GameResources.Read(root, "SCRIPT.000", Encoding.ASCII.GetBytes(member));
            var point = new ScriptCheckpoint(member, member == "44CORE" ? "pre_enter" : "run",
                Convert.ToHexString(SHA256.HashData(source)), ScenarioContinuation.EnterTown, 0, []);
            var pending = snapshot with
            {
                ExactResume = true,
                Script = point,
                AdmissionTarget = new(destination.SceneIndex, destination.SceneMember.ToArray(), destination.ScriptMember.ToArray(), destination.AreaIndex),
                NextBattleSeed = 12345
            };
            var context = SessionRuntimeCodec.Encode(pending);
            CheckPrepared(context);

            // Recreate the old temporary-target/source layout, not merely the old version number.
            var legacy = JsonNode.Parse(context.Payload.Span)!.AsObject();
            legacy["ScriptReturnLocation"] = legacy["Location"]?.DeepClone();
            legacy["Location"] = legacy["AdmissionTarget"]!.DeepClone();
            legacy.Remove("AdmissionTarget");
            legacy["PreviousTown"] = 43;
            legacy["Modes"] = new JsonObject { ["Depth"] = 0, ["Current"] = GameMode.World,
                ["Slots"] = new JsonArray(GameMode.World, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) };
            legacy["Script"]!["Continuation"] = (int)ScenarioContinuation.EnterTown;
            CheckPrepared(new(SessionRuntimeCodec.Schema, 1, Encoding.UTF8.GetBytes(legacy.ToJsonString())));

            void CheckPrepared(NativeRuntimeContext value)
            {
                var prepared = loader.Prepare(value, image.State);
                Check(prepared.Snapshot.AdmissionTarget?.SceneIndex == 44 && prepared.Snapshot.Script?.Member == member,
                    "script-only continuation preserves destination and source");
                Check(prepared.Snapshot.NextBattleSeed == 12345 && prepared.TownEntryCommands.Count == 0,
                    "preparation preserves battle seed and does not invent town arrival");
                checkpoints++;
            }
        }
        Check(liveBefore.AsSpan().SequenceEqual(NativeGameStateCodec.Encode(live)), "detached preparation leaves live state untouched");
        Check(original.AsSpan().SequenceEqual(File.ReadAllBytes(fixture)), "user save unchanged");
        return $"Core admission PASS: absent town scene is valid for original/Eiren checkpoints; {checkpoints} v1/v2 loads, preserved seed and unchanged live/user state.";
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Core admission: " + message);
    }
}
