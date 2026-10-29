using System.Security.Cryptography;
using Atria.Game.Combat;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Resources;

namespace Atria.Tests.Resources;

/// <summary>Golden hashes computed from the pre-migration recovered tables, not from the JSON definitions.</summary>
internal static class GameDefinitionTests
{
    public static string Run()
    {
        Verify(Enumerable.Range(0, TownCatalog.Count).Select(i => TownCatalog.Get(i).MusicTrack), "74F954A888E7FA381142F10567288D3610BA5531C78EBE55EFFC2100FF468D7C", "town music");
        Verify(Enumerable.Range(0, CombatSceneBaselines.Count).Select(CombatSceneBaselines.GetDepthOffset), "5AD181318554702CEA6DF8B137D36F65187B32A2DDD75743C64A0E9645CE37DD", "scene baselines");
        var values = new List<int>();
        for (int i = 0; i < CombatAttributeCatalog.Count; i++)
        {
            var row = CombatAttributeCatalog.Get(i);
            values.AddRange([row.Health, row.Attack, row.Defense, row.CriticalChance, row.RecoveryInterval, row.MagicRank, row.AwardBonus]);
            foreach (var growth in row.RegionGrowth)
            {
                values.AddRange([growth.Health, growth.Attack, growth.Defense]);
            }
        }
        Verify(values, "63A67E405FA15FD7C5BE6A3E4343C6C0B10B317687BBA3BE6BB850C6BF672C7A", "enemy attributes and regional growth");
        values.Clear();
        for (int kind = 0; kind < ClassCatalog.Count; kind++)
        {
            for (int level = 1; level < ProgressionRules.Thresholds.Length; level++)
            {
                var member = new PartyMember { Class = kind };
                ProgressionCatalog.Apply(member, level);
                for (int i = 0; i < PartyMember.AttributeCount; i++)
                {
                    values.Add(member.GetAttribute(i));
                }

                values.Add(member.Immunity);
                values.Add(member.Leadership);
            }
        }
        Verify(values, "7D69F725766ABB41E7097B77267759BA2FC5FB4B04247EA0C3243790BAE7A946", "all class/level stat gains");
        var party = new GameState(OriginalRoster.Catalog);
        byte[] original = new byte[2800];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = unchecked((byte)(i * 37 + 11));
        }

        PartyCodec.Decode(original, party.Party);
        InitialPartyDefinition.Apply(party.Party);
        VerifyBytes(PartyCodec.Encode(party.Party), "9E997C3C343CD692B18683EC30FB43B879609EEAEA037FA08CC31CC8DA4E0B22", "initial assignments including retained fields");
        return "Game definitions match pre-migration tables: 52 town tracks, 60 baselines, 52 enemy stat rows, 72 class/level gain rows and six initial characters.";
    }

    private static void Verify(IEnumerable<int> values, string expected, string name)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (int value in values)
        {
            writer.Write(value);
        }

        VerifyBytes(stream.ToArray(), expected, name);
    }

    private static void VerifyBytes(byte[] bytes, string expected, string name)
    {
        if (Convert.ToHexString(SHA256.HashData(bytes)) != expected)
        {
            throw new InvalidOperationException($"Game definition differs from original recovered table: {name}.");
        }
    }
}
