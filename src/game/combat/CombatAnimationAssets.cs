using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>413ca0 ->4374f0: actual ANI.000 animation and its referenced active SPR.000 sprite.</summary>
public sealed record CombatAnimationAssets(AnimationFile Animation, SpriteFile Sprites)
{
    public static CombatAnimationAssets Load(string resourceRoot, ReadOnlySpan<byte> animationName)
    {
        using var animations = BinderArchive.Open(Path.Combine(resourceRoot, "ANI.000"));
        int animationIndex = animations.FindResource(animationName);
        if (animationIndex < 0) throw new FileNotFoundException("Required combat animation is missing.");
        AnimationFile animation = AnimationFile.Decode(animations.ReadMember(animationIndex));
        using var sprites = BinderArchive.Open(Path.Combine(resourceRoot, "SPR.000"));
        int spriteIndex = sprites.FindResource(animation.SpriteReference);
        if (spriteIndex < 0) throw new FileNotFoundException("Required combat sprite is missing.");
        SpriteFile sprite = SpriteFile.Decode(sprites.ReadMember(spriteIndex));
        return new CombatAnimationAssets(animation, sprite);
    }
}
