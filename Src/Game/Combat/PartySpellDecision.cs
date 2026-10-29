using Atria.Game.Exploration;

namespace Atria.Game.Combat;

internal sealed class PartySpellDecision
{
    private const uint DecisionInterval = 600;
    private const uint SpellCooldownMilliseconds = 10_000;
    private const int ExpensiveManaPercent = 30;
    private const int HealingHealthPercent = 50;
    private readonly CombatSceneRuntime runtime;
    private readonly CombatParticleMagicRuntime magic;
    private readonly CombatProjectileForecast forecast;
    private readonly uint[] nextDecision;

    public PartySpellDecision(string root, CombatSceneRuntime runtime, CombatParticleMagicRuntime magic,
        CombatHitExecutor hits, LegacyRandom random)
    {
        this.runtime = runtime;
        this.magic = magic;
        forecast = new(root, runtime, hits, random);
        nextDecision = new uint[runtime.Actors.Count];
    }

    public int Select(int actorIndex, uint now, out byte facing)
    {
        var image = runtime.Actors[actorIndex];
        var actor = image.State;
        facing = actor.Facing;
        if (actor.Team < 100 || actor.ControlledBy != 0 || !actor.ActionState.CanStartGroundAttack
            || magic.ActiveMagic is not null || now <= nextDecision[actorIndex])
        {
            return -1;
        }
        nextDecision[actorIndex] = now + DecisionInterval;
        if (!runtime.Actors.Any(IsEnemy) || runtime.Actors.Any(t => t.State.Active != 0 && t.State.Health > 0
            && t.State.ActionFlags == (int)CombatActionFlags.Command))
        {
            return -1;
        }

        int best = -1, bestScore = 0;
        var commands = image.Binding!.Info.Commands;
        bool needsManaSupply = !commands.Any(command => command.Category > 0 && command.Category <= actor.MagicRank
            && command.ManaCost > 0 && command.ManaCost <= actor.Mana);
        for (int i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            if (command.Category <= 0 || command.Category > actor.MagicRank || command.ManaCost <= 0
                || (command.ManaCost > actor.Mana && (!needsManaSupply
                    || CombatManualControl.FindManaSupplySlot(actor, command.ManaCost) < 0)) || (runtime.IsWeakEncounter
                && (long)command.ManaCost * 100 > (long)actor.MaximumMana * ExpensiveManaPercent))
            {
                continue;
            }
            int score = Score(image, command, now, out byte direction);
            if (score > bestScore || (score > 0 && score == bestScore && command.ManaCost < commands[best].ManaCost))
            {
                best = i;
                bestScore = score;
                facing = direction;
            }
        }
        return best;
    }

    public void Cast(int actorIndex, uint now) =>
        nextDecision[actorIndex] = now + SpellCooldownMilliseconds;

    internal int Score(CombatActorImage image, CombatCommand command, uint now, out byte facing)
    {
        facing = image.State.Facing;
        var frames = image.Binding!.Assets.Animation.Sequences[command.Animation].Frames;
        if (frames.Any(f => CombatProjectileForecast.IsProjectile(f.Effect)))
        {
            int current = forecast.Score(image, command, facing);
            byte opposite = facing == 1 ? (byte)255 : (byte)1;
            int reversed = forecast.Score(image, command, opposite);
            if (reversed > current)
            {
                facing = opposite;
            }
            int targets = Math.Max(current, reversed);
            return targets == 0 ? 0 : 300 + targets;
        }

        int score = 0;
        bool effectFound = false;
        foreach (var frame in frames)
        {
            if (frame.Effect == 0)
            {
                continue;
            }
            effectFound = true;
            int useful = EffectScore(image, (CombatSpellEffect)frame.Effect, now);
            if (useful == 0)
            {
                return 0;
            }
            score = Math.Max(score, useful);
        }
        if (effectFound)
        {
            return score;
        }

        // Mana-consuming melee commands still need an enemy within ordinary melee reach.
        const int MeleeReach = 80, MeleeDepthTolerance = 14;
        if (frames.Any(f => f.Damage > 0))
        {
            foreach (var enemy in runtime.Actors)
            {
                var target = enemy.State;
                if (IsEnemy(enemy) && Math.Abs(target.Depth - image.State.Depth) < MeleeDepthTolerance
                    && Math.Abs(target.TrackingX - image.State.TrackingX) < MeleeReach
                    && target.HurtBottom >= image.State.Depth - image.Animation!.CurrentSprite.Height / 2)
                {
                    facing = target.TrackingX >= image.State.TrackingX ? (byte)1 : (byte)255;
                    return 250;
                }
            }
        }
        return 0;
    }

    internal int EffectScore(CombatActorImage owner, CombatSpellEffect effect, uint now)
    {
        var a = owner.State;
        bool Allies(Func<CombatActorImage, bool> predicate) => runtime.Actors.Any(t => IsAlly(t) && predicate(t));
        bool Enemies(Func<CombatActorImage, bool> predicate) => runtime.Actors.Any(t => IsEnemy(t) && predicate(t));
        bool Personal(int code) => Active(owner.State.StatusBank, code, now, 0);
        bool Global(int code) => Active(magic.GlobalStatus, code, now, code is 16 or 26 or 27 or 31 or 32 ? 1 : 0);
        bool meaningfulFight = !runtime.IsWeakEncounter;

        return effect switch
        {
            CombatSpellEffect.Resurrection => runtime.Actors.Any(t => t.Binding is not null && t.State.Team >= 100
                && t.State.SpawnCondition == 0 && t.State.Health <= 0) ? 1_200 : 0,
            CombatSpellEffect.PartyHeal or CombatSpellEffect.FullHeal => Allies(NeedsHealing) ? 1_000 : 0,
            CombatSpellEffect.SelfHeal => NeedsHealing(owner) ? 1_000 : 0,
            CombatSpellEffect.CurePoison => Allies(t => t.State.Poison != 0) ? 900 : 0,
            // Dispel removes friendly buffs AND enemy debuffs too: do not undo useful effects.
            CombatSpellEffect.Dispel => Allies(t => t.State.Poison != 0 || t.State.Slow != 0
                || t.State.AttackBonus < 0 || t.State.DefenseBonus < 0)
                && !Allies(t => t.State.AttackBonus > 0 || t.State.DefenseBonus > 0 || t.State.Haste != 0 || t.State.MagicBarrier != 0)
                && !Enemies(t => t.State.AttackBonus < 0 || t.State.DefenseBonus < 0 || t.State.Poison != 0 || t.State.Slow != 0) ? 800 : 0,
            CombatSpellEffect.Judgment => Enemies(_ => true) ? 300 : 0,
            CombatSpellEffect.Exorcism => Enemies(t => IsUndead(t.Binding!.Info.Type)) ? 300 : 0,
            // This spell can damage its caster and the entire party. Leave that risk to manual control.
            CombatSpellEffect.IndiscriminateDamage => 0,
            CombatSpellEffect.Critical => meaningfulFight && !Personal(7) && a.CriticalBonus <= 0 ? 100 : 0,
            CombatSpellEffect.Defense => meaningfulFight && !Personal(8) && a.DefenseBonus <= 0 ? 100 : 0,
            CombatSpellEffect.Haste => meaningfulFight && !Personal(11) && a.Haste == 0 ? 100 : 0,
            CombatSpellEffect.Invulnerability => meaningfulFight && !Personal(12) && a.Invulnerable == 0 ? 100 : 0,
            CombatSpellEffect.Concealment => meaningfulFight && !Personal(28) && a.Invulnerable == 0 ? 100 : 0,
            CombatSpellEffect.PartyAttack => meaningfulFight && !Global(17) && Allies(t => t.State.AttackBonus <= 0) ? 100 : 0,
            CombatSpellEffect.Barrier or CombatSpellEffect.PartyDefense => meaningfulFight && !Global(30)
                && Allies(t => t.State.DefenseBonus <= 0) ? 100 : 0,
            CombatSpellEffect.PartyBlessing => meaningfulFight && !Global(35)
                && Allies(t => t.State.AttackBonus <= 0 && t.State.DefenseBonus <= 0) ? 100 : 0,
            CombatSpellEffect.HolyAttack => meaningfulFight && !Global(40) && Enemies(t => IsUndead(t.Binding!.Info.Type)) ? 100 : 0,
            CombatSpellEffect.Guard => meaningfulFight && !Personal(49) && a.GuardBonus <= 0 ? 100 : 0,
            CombatSpellEffect.Attack => meaningfulFight && !Personal(52) && a.AttackBonus <= 0 ? 100 : 0,
            CombatSpellEffect.Berserk => meaningfulFight && !Personal(54) && a.AttackBonus <= 0
                && (long)a.Health * 4 >= (long)owner.InitialHealth * 3 ? 100 : 0,
            CombatSpellEffect.Slow => meaningfulFight && !Global(16) && Enemies(t => t.State.Slow == 0 && t.State.MagicRank < 10) ? 200 : 0,
            CombatSpellEffect.WeakenAttack => meaningfulFight && !Global(26) && Enemies(t => t.State.AttackBonus >= 0 && t.State.MagicRank < 10) ? 200 : 0,
            CombatSpellEffect.WeakenDefense => meaningfulFight && !Global(27) && Enemies(t => t.State.DefenseBonus >= 0) ? 200 : 0,
            CombatSpellEffect.Plague => meaningfulFight && !Global(31) && Enemies(t => t.State.Poison == 0) ? 200 : 0,
            CombatSpellEffect.Poison => meaningfulFight && !Global(32) && Enemies(t => t.State.Poison == 0 && t.State.MagicRank < 10) ? 200 : 0,
            _ => 0
        };
    }

    private static bool Active(IEnumerable<CombatStatusSlot> slots, int code, uint now, int side) =>
        slots.Any(s => s.Code == code - 1 && s.Deadline > (long)now && s.TeamSide == side);

    private static bool IsUndead(int type) => type is 12 or 21 or 22 or 23 or 37 or 41;
    private static bool NeedsHealing(CombatActorImage image) => image.State.Health > 0
        && image.State.Health < image.State.MaximumHealth
        && (long)image.State.Health * 100 < (long)image.InitialHealth * HealingHealthPercent;
    private static bool IsAlly(CombatActorImage image) => image.Binding is not null && image.State.Team >= 100
        && image.State.SpawnCondition == 0 && image.State.Active != 0 && image.State.Health > 0;
    private static bool IsEnemy(CombatActorImage image) => image.Binding is not null && image.State.Team < 100
        && image.State.SpawnCondition == 0 && image.State.Active != 0 && image.State.Health > 0;
}
