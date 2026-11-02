using Atria.Core.Game.Exploration;
using Atria.Core.Game.World;
using Atria.Core.Presentation.Exploration;
using Atria.Core.Presentation.Sprites;
using Atria.Core.Resources;
using Atria.Core.Resources.Codecs;
using Atria.Core.Resources.World;

namespace Atria.Core.Presentation.Dungeon;

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
