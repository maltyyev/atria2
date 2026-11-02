using System.Security.Cryptography;
using System.Text;
using Atria.Core.Game.Script;

namespace Atria.Core.Resources.Scripts;

/// <summary>Loads script editions and reuses immutable parsed programs within one game session.</summary>
internal sealed class ScenarioProgramCatalog
{
    private readonly string root;
    private readonly Dictionary<(string Member, string SourceHash), ScriptProgram> programs = new();

    /// <summary>Creates a catalog scoped to a single installation and session lifetime.</summary>
    public ScenarioProgramCatalog(string root) => this.root = root;

    /// <summary>Loads the selected source edition and records its raw-byte identity before parser repairs.</summary>
    public ScenarioSource LoadSource(ReadOnlySpan<byte> member, string? expectedSourceHash = null)
    {
        byte[] bytes = GameResources.Read(root, "SCRIPT.000", member, expectedSourceHash);
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(member)).ToUpperInvariant();
        string sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
        return new(name, sourceHash, bytes);
    }

    /// <summary>Returns the cached immutable program for the canonical member and exact raw source hash.</summary>
    public ScriptProgram GetProgram(ScenarioSource source)
    {
        var key = (source.Member, source.SourceHash);
        if (!programs.TryGetValue(key, out var program))
        {
            programs.Add(key, program = ScriptProgram.Parse(source.Member, source.Bytes));
        }

        return program;
    }
}

/// <summary>Canonical script identity plus the exact bytes used for replay validation and parsing.</summary>
internal sealed record ScenarioSource(string Member, string SourceHash, byte[] Bytes);
