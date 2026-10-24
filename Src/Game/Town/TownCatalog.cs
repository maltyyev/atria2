using Atria.Resources;

namespace Atria.Game.Town;

public readonly record struct TownDefinition(int Id, int NameMessageId, int MusicTrack);

public static class TownCatalog
{
    private static readonly TownDefinition[] towns = Load();
    public static int Count => towns.Length;

    public static TownDefinition Get(int townId)
    {
        if ((uint)townId >= (uint)towns.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(townId));
        }

        return towns[townId];
    }

    public static ReadOnlyMemory<byte> Name(IReadOnlyList<ReadOnlyMemory<byte>> messages, int townId)
    {
        int messageId = Get(townId).NameMessageId;
        if ((uint)messageId >= (uint)messages.Count)
        {
            throw new InvalidDataException("Town name is absent from the message resource.");
        }

        return messages[messageId];
    }

    private static TownDefinition[] Load()
    {
        using var data = GameData.Open("Towns");
        var result = new List<TownDefinition>();
        foreach (var row in data.RootElement.GetProperty("Towns").EnumerateArray())
        {
            var town = new TownDefinition(row.GetProperty("Id").GetInt32(), row.GetProperty("NameMessageId").GetInt32(), row.GetProperty("MusicTrack").GetInt32());
            if (town.Id != result.Count || town.NameMessageId < 0 || town.MusicTrack <= 0)
            {
                throw new InvalidDataException("Town definitions require ordered IDs, message IDs and positive logical music tracks.");
            }

            result.Add(town);
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("Town definitions are empty.");
        }

        return result.ToArray();
    }
}
