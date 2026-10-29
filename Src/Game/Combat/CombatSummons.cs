using System.Text;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>One finite group per summoner; reserved actors keep all combat index banks stable.</summary>
public sealed class CombatSummons(CombatSceneRuntime runtime)
{
    public const int FirstEffect = 1001;
    public const int LastEffect = 1003;
    private sealed record Group(int Owner, int[] Slots)
    {
        public uint Expires
        {
            get; set;
        }
    }
    private readonly List<Group> groups = [];
    private double medianLevel;
    private static readonly byte[] emptyActor = new byte[CombatActorCodec.ImageSize];
    public static bool IsEffect(int effect) => effect is >= FirstEffect and <= LastEffect;
    internal delegate CombatInfoBinding LoadBinding(ReadOnlySpan<byte> resource);

    internal void Prepare(CombatPartySelection selection, LoadBinding load)
    {
        var levels = selection.Party.Where(member => member.PendingAction != 0).Select(member => member.Level).Order().ToArray();
        medianLevel = levels.Length == 0 ? 1 : (levels[(levels.Length - 1) / 2] + levels[levels.Length / 2]) / 2.0;
        int next = runtime.Scene.Source.Count;
        for (int hero = 0; hero < selection.Party.Count; hero++)
        {
            int count = selection.HeroCatalog[hero].SummonSlots;
            if (count == 0)
            {
                continue;
            }

            int[] slots = Enumerable.Range(next, count).ToArray();
            next += count;
            if (selection.Party[hero].PendingAction == 0)
            {
                continue;
            }

            int owner = runtime.Scene.PartyStartIndex + hero;
            groups.Add(new(owner, slots));
            var binding = load(Encoding.ASCII.GetBytes(NecromancerCombatAssets.SummonDefinition.GetProperty("info").GetString()!));
            foreach (int slot in slots)
            {
                var image = runtime.Actors[slot];
                image.IsSummon = true;
                image.Binding = binding;
                image.Animation = new(image.State, binding.Info, binding.Assets);
                image.State.Team = 100;
            }
        }
    }

    public bool CanSummon(CombatActorImage owner) => groups.Any(group => ReferenceEquals(runtime.Actors[group.Owner], owner)
        && owner.State.Active != 0 && owner.State.Health > 0 && !group.Slots.Any(slot => runtime.Actors[slot].State.Active != 0));

    public void Cast(CombatActorImage owner, int effect, uint now)
    {
        if (!IsEffect(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        if (!CanSummon(owner))
        {
            return;
        }

        var group = groups.Single(group => ReferenceEquals(runtime.Actors[group.Owner], owner));
        int count = effect - FirstEffect + 1;
        if (count > group.Slots.Length)
        {
            throw new InvalidDataException("Summon command exceeds the hero's reserved group.");
        }

        var config = NecromancerCombatAssets.SummonDefinition;
        group.Expires = unchecked(now + config.GetProperty("durationMilliseconds").GetUInt32());
        for (int i = 0; i < count; i++)
        {
            int slot = group.Slots[i];
            var image = runtime.Actors[slot];
            var actor = image.State;
            // Controllers retain these state references; reset in place instead of replacing an actor.
            CombatActorCodec.DecodeInto(emptyActor, actor);
            Array.Fill(actor.Equipment, -1);
            actor.MemberStatus = actor.Active = actor.Rank = 1;
            actor.Team = 100;
            actor.CanTurn = 1;
            actor.Health = actor.MaximumHealth = actor.DisplayedHealth = actor.HealthBarMaximum =
                (int)Math.Floor(config.GetProperty("healthPerLevel").GetInt32() * medianLevel / count);
            actor.Attack = (int)Math.Floor(config.GetProperty("attackPerLevel").GetInt32() * medianLevel / count);
            actor.Defense = (int)Math.Floor(config.GetProperty("defensePerLevel").GetInt32() * medianLevel);
            actor.BaseRecoveryInterval = config.GetProperty("recoveryMilliseconds").GetInt32();
            int spacing = config.GetProperty("spawnSpacing").GetInt32();
            actor.AnchorX = actor.TrackingX = actor.SpriteX = owner.State.TrackingX + (i + 1) * spacing * (owner.State.Facing == 1 ? 1 : -1);
            actor.Depth = Math.Clamp(owner.State.Depth + (i - count / 2) * spacing, runtime.Scene.Header73, runtime.Scene.Header77);
            actor.Facing = owner.State.Facing;
            actor.JumpSequenceIndex = image.Binding!.NativeIndex;
            runtime.TargetIndices[slot] = -1;
            image.InitialHealth = actor.Health;
            image.CurrentCommandIndex = -1;
            image.Animation = new(actor, image.Binding.Info, image.Binding.Assets);
            image.Animation.BeginMotion(0, now);
        }
    }

    public void Update(uint now)
    {
        foreach (var group in groups)
        {
            var owner = runtime.Actors[group.Owner].State;
            bool expired = unchecked((int)(now - group.Expires)) >= 0 || owner.Active == 0 || owner.Health <= 0;
            foreach (int slot in group.Slots)
            {
                var actor = runtime.Actors[slot].State;
                if (!expired && actor.Health > 0)
                {
                    continue;
                }

                actor.Active = 0;
                actor.Health = 0;
                actor.SpawnCondition = 0;
                runtime.TargetIndices[slot] = -1;
            }
        }
    }
}
