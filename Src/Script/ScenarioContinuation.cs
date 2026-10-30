using System.Text.Json.Serialization;

namespace Atria.Script;

[JsonConverter(typeof(JsonStringEnumConverter<ScenarioContinuation>))]
public enum ScenarioContinuation
{
    None = 0,
    EnterBuilding = 1,
    LeaveTown = 2,
    InteriorInteraction = 3,
    LeaveBuilding = 4,
    EnterTown = 5
}
