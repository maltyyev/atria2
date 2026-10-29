using System.Buffers.Binary;
using System.Globalization;

namespace Atria.Game.Combat;

public static class CombatParticleMagic
{
    //40d550 binds actual MAGIC%02d.ANI for id-100. Caller owns active-magic lifecycle.
    public static CombatAnimationAssets Load(string root, int id) => CombatAnimationAssets.Load(root, System.Text.Encoding.ASCII.GetBytes("MAGIC" + unchecked(id - 100).ToString("D2", CultureInfo.InvariantCulture)));
    //40b730 mass resurrection. Does not invent an animation/input action for the revived actor.
    public static int Resurrect(IReadOnlyList<CombatActorImage> actors, CombatActorImage current, int mode)
    {
        int count = 0;
        byte team = current.State.Team;
        int strength = current.State.MagicRank;
        foreach (var actor in actors)
        {
            CombatActorState fields = actor.State;
            if (actor.IsSummon || fields.Health >= 1 || actor.Binding is not { } binding || fields.Team != team)
            {
                continue;
            }

            int type = binding.Info.Type;
            if (type is not (12 or 21 or 22 or 23 or 37) && mode == 1)
            {
                continue;
            }

            fields.Active = 1;
            count++;
            fields.FlashTicks = 250;
            fields.Health = Math.Min(fields.MaximumHealth, unchecked(strength * 50));
            fields.FlashColor = 0;
            fields.Rank = 3;
            if (count > 4)
            {
                return count;
            }
        }

        return count;
    }

}
