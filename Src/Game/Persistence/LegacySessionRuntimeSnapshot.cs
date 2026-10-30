using System.Text.Json.Serialization;
using Atria.Game.Persistence.Compatibility;

namespace Atria.Game.Persistence;

/// <summary>Version-one fields are read-only compatibility data, never navigation state.</summary>
public sealed record LegacySessionRuntimeSnapshot : SessionRuntimeSnapshot
{
    [JsonRequired]
    public required LegacyModeStackSnapshot Modes { get; init; }

    public SessionLocationSnapshot? ScriptReturnLocation { get; init; }

    [JsonRequired]
    public required int PreviousTown { get; init; }
}
