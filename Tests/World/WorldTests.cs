using System.Buffers.Binary;
using Atria.Game.World;
using Atria.Resources;

namespace Atria.Tests.World;

public static class WorldTests
{
    public static string Run(string resourceRoot)
    {
        static void Check(bool ok, string text)
        {
            if (!ok)
            {
                throw new InvalidOperationException(text);
            }
        }

        Check(NativeModeEntry.MusicId(300, 27, 7) == 8 && NativeModeEntry.MusicId(330, 0, 0, () => 32767) == 20, "World music direct assignments and original rand parity.");
        Check(NativeModeEntry.MusicId(240, 0, 0) == 26 && NativeModeEntry.MusicId(1100, 0, 0) == 27, "Credits/gameover music IDs.");
        var entry = NativeModeEntry.Steps(400);
        Check(entry[0].Operation == ModeEntryOperation.LoadTownScene && entry[1].Operation == ModeEntryOperation.LoadTownBackground
&& entry[^1].Operation == ModeEntryOperation.UpdateMusic, "Town entry order keeps required scene operation.");
        using var amf = BinderArchive.Open(Path.Combine(resourceRoot, "AT2AMF.000"));
        byte[] raw = amf.ReadMember(amf.FindResource("ground1.amf"u8));
        var map = WorldMapContext.Parse(raw);
        var scene = map.SelectScene(4, 4);
        Check(scene.SceneIndex == 1 && scene.SceneMember.Span.SequenceEqual("01mercen.sod"u8) && scene.ScriptMember.Span.SequenceEqual("01mercen.scr"u8), "Native initial AMF coordinate/index/ref fixture.");
        var store = new WorldAreaStore();
        byte[] inline = Enumerable.Repeat((byte)0x5a, 417).ToArray();
        byte[] payload = Enumerable.Repeat((byte)0x6b, 32800).ToArray();
        store.Import(0, inline, payload);
        store.Capture(0, map);
        var captured = store.GetInline(0);
        var tiles = store.GetPayload(0);
        Check(captured.AsSpan(0, 100).SequenceEqual(inline.AsSpan(0, 100)), "Area capture preserves names.");
        Check(BinaryPrimitives.ReadInt32LittleEndian(captured.AsSpan(100, 4)) == 1
&& BinaryPrimitives.ReadInt32LittleEndian(captured.AsSpan(117, 4)) == 10, "Packed area initialized/dimension offsets.");
        Check(captured.AsSpan(141, 16).IndexOfAnyExcept((byte)0) < 0, "Four transient inline fields zeroed.");
        Check(tiles.AsSpan(0, 128).SequenceEqual(map.GetTiles().AsSpan(0, 128)), "First128 tile bytes copied exactly.");
        int firstString = 128;
        byte[] originalTiles = map.GetTiles();
        int nul = originalTiles.AsSpan(firstString, 20).IndexOf((byte)0);
        Check(nul >= 0 && tiles[firstString + nul] == 0, "Tile string includes NUL.");
        if (nul < 19)
        {
            Check(tiles[firstString + nul + 1] == 0x6b, "Native string copy preserves destination tail.");
        }

        store.Restore(0, map);
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(resourceRoot, "TEXTmsg.dat")));
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(resourceRoot, "ATITEM.DAT")));
        var activation = NewGameActivation.Prepare(items, messages, raw);
        Check(activation.State.Mode == 400 && activation.State.WorldX == 4 && activation.State.ModeStackDepth == 0 && activation.Areas.IsInitialized(0), "Newgame state/map/area/root-mode activation.");
        Check(activation.State.Money == 200 && activation.FirstScene.SceneIndex == 1, "Initial state preserved before town scripts.");
        var firstArea = activation.Areas.GetInline(0);
        Check(firstArea.AsSpan(0, messages[298].Length).SequenceEqual(messages[298].Span), "Area name from native TEXTmsg298.");
        return "World static contracts: music assignments, real GROUND1 activation, packed area sync and string-tail preservation passed.";
    }
}
