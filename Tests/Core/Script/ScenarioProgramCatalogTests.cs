using System.Security.Cryptography;
using System.Text;
using Atria.Core.Game.State;
using Atria.Core.Resources;
using Atria.Core.Resources.Scripts;

namespace Tests.Core.Script;

public static class ScenarioProgramCatalogTests
{
    /// <summary>Checks session cache identity and historical authored-source selection from real catalog data.</summary>
    public static string Run(string root)
    {
        using var legacy = GameData.Open("NarrativeSceneLegacySources");
        var archived = legacy.RootElement.EnumerateObject().First();
        string member = archived.Name;
        byte[] archivedBytes = Encoding.ASCII.GetBytes(archived.Value[0].GetString()!);
        string archivedHash = Convert.ToHexString(SHA256.HashData(archivedBytes));
        byte[] currentBytes = NarrativeScenes.All.Single(scene => scene.Id == member).Source;
        string currentHash = Convert.ToHexString(SHA256.HashData(currentBytes));
        Check(archivedHash != currentHash, "fixture provides two authored source editions");

        var catalog = new ScenarioProgramCatalog(root);
        ScenarioSource archivedSource = catalog.LoadSource(Encoding.ASCII.GetBytes(member), archivedHash);
        Check(archivedSource.SourceHash == archivedHash && archivedSource.Bytes.AsSpan().SequenceEqual(archivedBytes),
            "saved hash selects the archived source bytes");
        var archivedProgram = catalog.GetProgram(archivedSource);

        ScenarioSource aliasSource = catalog.LoadSource(Encoding.ASCII.GetBytes(member.ToLowerInvariant() + ".scr"), archivedHash);
        Check(aliasSource.Member == member && catalog.GetProgram(aliasSource) == archivedProgram,
            "canonical member plus raw hash reuses the same immutable program");

        ScenarioSource currentSource = catalog.LoadSource(Encoding.ASCII.GetBytes(member));
        Check(currentSource.SourceHash == currentHash && currentSource.Bytes.AsSpan().SequenceEqual(currentBytes),
            "unversioned load selects the current authored source");
        var currentProgram = catalog.GetProgram(currentSource);
        Check(currentProgram != archivedProgram && currentProgram.SourceSha256 == currentHash.ToLowerInvariant(),
            "different editions of one member remain distinct and parser provenance stays lowercase");

        string unknownHash = new('0', 64);
        if (unknownHash == archivedHash || unknownHash == currentHash)
        {
            unknownHash = new('F', 64);
        }

        try
        {
            catalog.LoadSource(Encoding.ASCII.GetBytes(member), unknownHash);
            throw new InvalidOperationException("Unknown authored source hash was accepted.");
        }
        catch (InvalidDataException exception) when (exception.Message == "Unknown authored scenario continuation source.")
        {
        }

        return $"Scenario catalog: archived/current source editions, canonical cache identity, and unknown replay rejection passed for {member}.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Scenario catalog: " + message);
        }
    }
}
