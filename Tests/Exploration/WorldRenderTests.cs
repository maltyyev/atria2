using System.Buffers.Binary;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Exploration;

public static class WorldRenderTests
{
    public static void Run(string root)
    {
        using var amfs = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
        byte[] original = amfs.ReadMember(amfs.FindResource("ground1.amf"u8));
        var shippedMap = WorldMapContext.Parse(original);
        var shippedAssets = WorldSceneAssets.Load(root, shippedMap, original);
        var shippedRenderer = new WorldSceneRenderer(shippedAssets);
        var state = new GameState
        {
            Mode = 300,
            WorldX = 4,
            WorldY = 4,
            Year = 1450,
            Month = 3,
            Day = 14,
            Money = 200
        };
        ExplorationRules.MarkArrival(shippedMap, 4, 4);
        shippedRenderer.RebuildBase(shippedMap, state);
        ushort[] target = new ushort[640 * 480];
        shippedRenderer.Draw(new(3, 0, 288, 182), null, target);
        Check(target.Any(p => p != 0), "real world ground/object/fog/CHIPCHAR composition");
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true);
        var hud = WorldHudRenderer.Load(root, font);
        hud.Draw(state, "world", target);
        state.WorldY = 5;
        hud.Draw(state, "world", target);
        state.Mode = 400;
        hud.Draw(state, "town", target, townAnchorY: 100);
        state.Mode = 410;
        hud.Draw(state, "interior", target);
        Check(WorldHudRenderer.OriginY(300, 4, false) == 421 && WorldHudRenderer.OriginY(300, 5, false) == 0, "normal world HUD avoids player half");
        Check(WorldHudRenderer.OriginY(400, 0, false, 79) == 421 && WorldHudRenderer.OriginY(400, 0, false, 80) == 0, "town anchor80 HUD boundary");
        Check(WorldHudRenderer.OriginY(410, 0, true) is null && WorldHudRenderer.OriginY(300, 4, true) is null, "modal suppresses lower HUD");
        Check(WorldHudRenderer.OriginY(500, 5, false) is null, "UI screen does not draw the world HUD");
        Check(WorldHudRenderer.OriginY(330, 5, false, dungeonHudEnabled: true) == 0, "enabled dungeon HUD uses its actual location");
        byte[] raw = (byte[])original.Clone();
        raw.AsSpan(313, 32800).Clear();
        raw[313 + 3] = 2;
        var map = WorldMapContext.Parse(raw);
        byte[] sod = new byte[490];
        "Object Tool ver2.0"u8.CopyTo(sod);
        Put(sod, 30, 1);
        Put(sod, 120, 50);
        Put(sod, 124, 50);
        Put(sod, 120 + 32, 1);
        Put(sod, 120 + 40, 3);
        var objectSprite = new SpriteFile(false, [Pixel(0xf800)]);
        SpriteFrame[] etc = Enumerable.Range(0, 48).Select(_ => Pixel(0)).ToArray();
        etc[38] = Pixel(0x001f);
        var characterSprite = new SpriteFile(false, [new(0, 0, 0, [], [], 1, 4, [new(0, 0, 0, [0x07e0], [])])]);
        var animation = new AnimationFile([], [new(0, [new(new ushort[28], 0, 0, 0)])]);
        var assets = new WorldSceneAssets(Enumerable.Repeat((ushort)0xffff, 640 * 480).ToArray(), SodV2.Parse(sod), objectSprite, new(false, etc), animation, characterSprite);
        var renderer = new WorldSceneRenderer(assets);
        state = new();
        state.Events[3] = 0;
        renderer.RebuildBase(map, state);
        renderer.Draw(new(0, 0, 100, 100), new(200, 200, 38), target);
        Check(target[0] == 0x39e7, "fog ETC16 HalfAll twice exact565 vector");
        Check(target[50 * 640 + 50] == 0xffff && !renderer.IsSceneReferenceBlocked(0), "unfulfilled SOD condition hides/locks the object (native425d38 JNZ skip)");
        Check(target[103 * 640 + 99] == 0 && target[100 * 640 + 100] == 0x07e0, "quarter-height diagonal black shadow before sprite");
        Check(target[200 * 640 + 200] == 0x001f && target[479 * 640] == 0 && target[639] == 0, "reveal order and native cleared border");
        state.Events[3] = 1;
        renderer.RebuildBase(map, state);
        state.Events[3] = 0;
        renderer.RebuildBase(map, state);
        renderer.Draw(new(0, 0, 100, 100), null, target);
        Check(target[50 * 640 + 50] == 0xf800 && renderer.IsSceneReferenceBlocked(0), "event1 permanently reveals/unlocks object (native425d3a clear)");
        // Eligible mode310 display predicate uses neighborhood byte1, not resource/terrain reveal acceptance.
        raw[313 + 9 * 328 + 1] = 1;
        map = WorldMapContext.Parse(raw);
        renderer.ResetRevealAnimation();
        for (int tick = 0; tick < 30; tick++)
        {
            Check(renderer.AdvanceReveal(map, 0, 1, 9, 0)!.Value.SpriteFrame == 38 + tick / 3, "reveal ten frames each three logical calls, flattened wrap");
        }

        Check(renderer.AdvanceReveal(map, 0, 1, 9, 0)!.Value.SpriteFrame == 38, "reveal frame wrap");
        Check(renderer.AdvanceReveal(map, 0, 1, 9, 0, false) is null, "reveal visibility gate");
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var areas = new WorldAreaStore();
        areas.ResetNames(messages);
        raw.AsSpan(313, 32800).Clear();
        Put(raw, 313 + 124, 0);
        int at = 313 + (4 * 10 + 4) * 328;
        Put(raw, at + 92, 2);
        Put(raw, at + 96, 3);
        "ground1.amf\0"u8.CopyTo(raw.AsSpan(at + 128, 20));
        map = WorldMapContext.Parse(raw);
        state = new()
        {
            WorldX = 4,
            WorldY = 4,
            ChangedChip = 1
        };
        state.Globals[73] = 11;
        var transition = WorldMapBoundary.PrepareTransition(state, map, areas, _ => raw);
        Check(transition.TargetX == 2 && transition.TargetY == 3 && transition.Entry.MapMember.Span.SequenceEqual("ground1.amf"u8), "map transition source+92/+96/+128");
        WorldMapBoundary.ApplyCoordinatesAndNativeResets(transition, state);
        Check(state.WorldX == 2 && state.WorldY == 3 && state.ModeStackDepth == -1 && state.Globals[73] == 0 && state.ChangedChip == 0, "transition state resets before actual mode push");
    }

    private static SpriteFrame Pixel(ushort color) => new(0, 0, 0, [], [], 1, 1, [new(0, 0, 0, [color], [])]);
    private static void Put(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
    private static void Check(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException("World raster: " + message);
        }
    }
}
