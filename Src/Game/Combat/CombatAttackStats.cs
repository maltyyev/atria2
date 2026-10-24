namespace Atria.Game.Combat;

public readonly record struct CombatAttackStats(int Attack, int AttackBonus, int Defense, int DefenseBonus, int CriticalChance, int CriticalBonus, int Rank, byte Facing);
