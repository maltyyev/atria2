using System.Security.Cryptography;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>413ca0 ->4374f0: actual ANI.000 animation and its referenced active SPR.000 sprite.</summary>
public sealed record CombatAnimationAssets(AnimationFile Animation, SpriteFile Sprites)
{
    private const string OriginalWarriorHash = "9843F0F10AABF26EC90FF08FEB8E8B47D2C13F0CC867DCB79ADC696B9FD60FFB";
    private const int WarriorStrikeSequence = 27;
    private const int WarriorStrikeFrame = 10;

    public static CombatAnimationAssets Load(string resourceRoot, ReadOnlySpan<byte> animationName)
    {
        if (animationName.SequenceEqual("EIREN.ANI"u8))
        {
            return NecromancerCombatAssets.Animation(resourceRoot);
        }

        using var animations = BinderArchive.Open(Path.Combine(resourceRoot, "ANI.000"));
        int animationIndex = animations.FindResource(animationName);
        if (animationIndex < 0)
        {
            throw new FileNotFoundException("Required combat animation is missing.");
        }

        byte[] source = animations.ReadMember(animationIndex);
        AnimationFile animation = AnimationFile.Decode(source);
        if (animations.Entries[animationIndex].Name == "WARRIOR"
            && Convert.ToHexString(SHA256.HashData(source)) == OriginalWarriorHash)
        {
            AnimationFrame[] frames = animation.Sequences[WarriorStrikeSequence].Frames;
            frames[WarriorStrikeFrame] = frames[WarriorStrikeFrame] with { UsesLegacyWarriorStrikeSound = true };
        }

        using var sprites = BinderArchive.Open(Path.Combine(resourceRoot, "SPR.000"));
        int spriteIndex = sprites.FindResource(animation.SpriteReference);
        if (spriteIndex < 0)
        {
            throw new FileNotFoundException("Required combat sprite is missing.");
        }

        SpriteFile sprite = SpriteFile.Decode(sprites.ReadMember(spriteIndex));
        return new CombatAnimationAssets(animation, sprite);
    }
}
