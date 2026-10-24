using Atria.Game.Exploration;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Dungeon;

public sealed record DungeonSceneAssets(WorldSceneAssets Base, SpriteFile ObjectMarkers, AnimationFile TorchAnimation, SpriteFile TorchSprites)
{
    public static DungeonSceneAssets Load(string root, WorldMapContext map, ReadOnlyMemory<byte> originalAmf)
    {
        var world = WorldSceneAssets.Load(root, map, originalAmf);
        using var spr = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        using var ani = BinderArchive.Open(Path.Combine(root, "ANI.000"));
        byte[] Read(BinderArchive archive, ReadOnlySpan<byte> member)
        {
            int index = archive.FindResource(member);
            if (index < 0)
            {
                throw new InvalidDataException("Missing exact dungeon render resource.");
            }

            return archive.ReadMember(index);
        }

        var torch = AnimationFile.Decode(Read(ani, "torch.ani"u8));
        return new(world, SpriteFile.Decode(Read(spr, "d_obj.spr"u8)), torch, SpriteFile.Decode(Read(spr, torch.SpriteReference)));
    }
}
