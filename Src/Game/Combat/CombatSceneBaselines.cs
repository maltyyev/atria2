using Atria.Resources;

namespace Atria.Game.Combat;

internal static class CombatSceneBaselines
{
    private static readonly int[] offsets = Load();
    public static int Count => offsets.Length;
    public static int GetDepthOffset(int type) => (uint)type < offsets.Length ? offsets[type] : throw new InvalidDataException("Unknown combat scene type.");
    private static int[] Load()
    {
        using var document = GameData.Open("CombatSceneBaselines");
        var rows = document.RootElement.GetProperty("Types");
        var values = new int[rows.GetArrayLength()];
        int index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("Type").GetInt32() != index)
            {
                throw new InvalidDataException("Unordered combat scene types.");
            }

            values[index++] = row.GetProperty("DepthOffset").GetInt32();
        }
        return values;
    }
}
