using Atria.Game.Combat;

namespace Atria.Tests.Combat;

public static class WaveManaTests
{
    private sealed class Actions(Action<int> process) : ICombatActorActions
    {
        public void Process(int index, uint now) => process(index);
    }

    private sealed class Effects : ICombatFrameEffects
    {
        public void PlaySound(int id) { }
        public void SpawnActorParticle(int actor, int id, uint now) { }
        public void ApplyFrameEffect(int actor, int id, int argument, uint now) { }
        public void ShowConsumedItemNotice(int id, uint now) => throw new InvalidOperationException("Wave refill must not use items.");
    }

    public static void Run(string root)
    {
        var (runtime, state) = PartySpellTests.Fixture(root);
        int firstParty = runtime.Scene.PartyStartIndex;
        foreach (var image in runtime.Actors)
        {
            image.State.SpawnCondition = 0;
            image.State.Active = image.Binding is null ? 0 : 1;
            image.State.AnimationDeadline = int.MaxValue;
            image.State.Mana = image.State.DisplayedMana = 17;
        }
        for (int i = firstParty; i < runtime.Actors.Count; i++)
        {
            runtime.Actors[i].State.MemberStatus = 1;
        }
        runtime.Actors[firstParty].State.ControlledBy = 1;
        var fallen = runtime.Actors[firstParty + 4].State;
        fallen.Health = fallen.Active = 0;
        var absent = runtime.Actors[firstParty + 5].State;
        absent.MemberStatus = absent.Active = 0;

        void Pending(int index, int condition)
        {
            var actor = runtime.Actors[index].State;
            actor.SpawnCondition = condition;
            actor.SpawnEncounterThreshold = actor.SpawnStartTime = actor.SpawnPlacement = 0;
        }
        Pending(0, 7);
        Pending(1, 7);
        Pending(2, 8);
        Pending(3, 9);
        runtime.Actors[3].State.Team = 100;

        bool expectRefill = false;
        var actions = new Actions(index =>
        {
            if (!expectRefill || index < firstParty) return;
            var member = runtime.Actors[index].State;
            Check(member.Mana == member.MaximumMana && member.DisplayedMana == member.MaximumMana,
                "mana and HUD refill before either manual or automatic party action");
            member.Mana--;
        });
        var camera = new CombatCameraBounds(runtime, new(830, 480), (_, _) => { });
        var frames = new CombatFrameRuntime(runtime, state, camera, actions, new Effects(), () => 1);
        frames.Step(6000);
        Check(runtime.Actors[firstParty].State.Mana == 17, "waiting for a wave does not regenerate mana");

        expectRefill = true;
        frames.Step(6001);
        Check(runtime.Actors[0].State.SpawnCondition == 0 && runtime.Actors[1].State.SpawnCondition == 0,
            "two real spawn-controller activations share the wave");
        Check(runtime.Actors[firstParty].State.Mana == runtime.Actors[firstParty].State.MaximumMana - 1,
            "one refill, no repeated overwrite after the party acts");
        Check(fallen.Mana == fallen.MaximumMana && fallen.Health == 0 && fallen.Active == 0,
            "fallen member receives mana without resurrection");
        Check(absent.Mana == 17 && runtime.Actors[0].State.Mana == 17, "absent party and enemies are untouched");

        expectRefill = false;
        runtime.Actors[firstParty].State.Mana = 13;
        frames.Step(6002);
        Check(runtime.Actors[firstParty].State.Mana == 13, "ordinary next frame does not refill again");
        expectRefill = true;
        frames.Step(9001);
        Check(runtime.Actors[2].State.SpawnCondition == 0, "later timed wave also refills");

        expectRefill = false;
        runtime.Actors[firstParty].State.Mana = 13;
        frames.Step(12001);
        Check(runtime.Actors[3].State.SpawnCondition == 0 && runtime.Actors[firstParty].State.Mana == 13,
            "friendly reinforcement does not count as an enemy wave");
        Console.WriteLine("Wave mana PASS: real activation, multiple enemies, later wave, pre-action HUD update, fallen/absent members and friendly exclusion.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Wave mana: " + message);
    }
}
