using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;

namespace Atria.Game.Exploration;

public readonly record struct QuestMapPoint(int Area, int X, int Y);

/// <summary>Read-only objective locations and directed region exits from the shipped AMF/SOD assets.</summary>
public sealed class QuestMapIndex
{
    private const int RegionCount = 8;
    private const int SceneKindOffset = 88, SceneMemberOffset = 128;
    private const int ArrivalScriptKind = 99;
    private const int OfferEnabledField = 26, OfferVariantField = 27, OfferFirstField = 28, OfferLastField = 29;
    private sealed record Tile(QuestMapPoint Point, string Scene, int Kind, ReadOnlyMemory<byte> Raw);
    private sealed record Offer(QuestMapPoint Point, int Variant, int First, int Last);
    private sealed record Exit(QuestMapPoint Point, int Destination);
    private sealed record Target(int Quest, QuestMilestones Phase, string? Scene, string? AreaFunction, int? Event, QuestMapPoint? At);
    private readonly List<Tile> tiles = [];
    private readonly List<Offer> offers = [];
    private readonly List<Exit> exits = [];
    private readonly HashSet<int> nativeObjectives;
    private readonly Target[] targets;

    public QuestMapIndex(string root)
    {
        using var stream = typeof(QuestMapIndex).Assembly.GetManifestResourceStream("Atria.Content.Atria2.QuestObjectives.json")
            ?? throw new InvalidDataException("Missing quest objective definitions.");
        using var document = JsonDocument.Parse(stream);
        nativeObjectives = document.RootElement.GetProperty("nativeObjectives").EnumerateArray().Select(id => id.GetInt32()).ToHashSet();
        targets = document.RootElement.GetProperty("targets").EnumerateArray().Select(entry => new Target(
            entry.GetProperty("quest").GetInt32(), QuestJournalTriggers.ParseMilestone(entry.GetProperty("phase").GetString()),
            entry.TryGetProperty("scene", out var scene) ? scene.GetString() : null,
            entry.TryGetProperty("areaFunction", out var function) ? function.GetString() : null,
            entry.TryGetProperty("event", out var eventId) ? eventId.GetInt32() : null,
            entry.TryGetProperty("at", out var point) ? new(point[0].GetInt32(), point[1].GetInt32(), point[2].GetInt32()) : null)).ToArray();

        using var maps = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
        using var scenes = BinderArchive.Open(Path.Combine(root, "AT2SOD.000"));
        for (int area = 0; area < RegionCount; area++)
        {
            var map = AmfMap.Parse(maps.ReadMember(maps.FindResource(Encoding.ASCII.GetBytes($"ground{area + 1}.amf"))));
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var raw = map.GetTile(x, y).Raw;
                    var point = new QuestMapPoint(area, x, y);
                    string member = Encoding.ASCII.GetString(WorldMapContext.CString(raw.Span.Slice(SceneMemberOffset, 20)));
                    int kind = BinaryPrimitives.ReadInt32LittleEndian(raw.Span[SceneKindOffset..]);
                    tiles.Add(new(point, Path.GetFileNameWithoutExtension(member).ToUpperInvariant(), kind, raw));
                    if (kind == ArrivalScriptKind && member.StartsWith("ground", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(Path.GetFileNameWithoutExtension(member).AsSpan("ground".Length), out int next)
                        && next is >= 1 and <= RegionCount)
                    {
                        exits.Add(new(point, next - 1));
                    }
                    if (!member.EndsWith(".sod", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int index = scenes.FindResource(Encoding.ASCII.GetBytes(member));
                    if (index < 0)
                    {
                        continue;
                    }

                    byte[] bytes = scenes.ReadMember(index);
                    if (!SodV2.HasV2Signature(bytes))
                    {
                        continue;
                    }

                    var sod = SodV2.Parse(bytes);
                    for (int i = 0; i < sod.Count; i++)
                    {
                        var item = sod.GetObject(i);
                        if (item.GetInteger(OfferEnabledField) != 0)
                        {
                            offers.Add(new(point, item.GetInteger(OfferVariantField), item.GetInteger(OfferFirstField), item.GetInteger(OfferLastField)));
                        }
                    }
                }
            }
        }
    }

    public QuestMapPoint? Objective(GameState state)
    {
        if (state.Journal.ActiveQuest(state) is not { } id)
        {
            return null;
        }

        var progress = state.Journal.Progress(state, id);
        if (id < QuestJournal.OriginalEntryCount && progress.HasFlag(QuestMilestones.ObjectiveReached))
        {
            return Unique(offers.Where(offer => offer.Variant == 1 && id >= offer.First && id <= offer.Last).Select(offer => offer.Point));
        }
        foreach (var target in targets)
        {
            if (target.Quest != id || !progress.HasFlag(target.Phase))
            {
                continue;
            }

            if (target.Event is { } eventId)
            {
                return EventTarget(state, eventId, target.At);
            }

            if (target.At is { } at)
            {
                return at;
            }

            string name = (target.Scene ?? target.AreaFunction)!.ToUpperInvariant();
            return Unique(tiles.Where(tile => tile.Scene == name).Select(tile => tile.Point));
        }
        if (!nativeObjectives.Contains(id))
        {
            return null;
        }

        return EventTarget(state, id) ?? Unique(offers.Where(offer => offer.Variant == 2 && id >= offer.First && id <= offer.Last).Select(offer => offer.Point));
    }

    public QuestMapPoint? Marker(GameState state, int currentArea)
    {
        if (Objective(state) is not { } target)
        {
            return null;
        }

        if (target.Area == currentArea)
        {
            return target;
        }

        var visited = new HashSet<int> { currentArea };
        var queue = new Queue<(int Area, QuestMapPoint? FirstExit)>();
        queue.Enqueue((currentArea, null));
        while (queue.TryDequeue(out var step))
        {
            foreach (var exit in exits.Where(exit => exit.Point.Area == step.Area))
            {
                if (!visited.Add(exit.Destination))
                {
                    continue;
                }

                var first = step.FirstExit ?? exit.Point;
                if (exit.Destination == target.Area)
                {
                    return first;
                }

                queue.Enqueue((exit.Destination, first));
            }
        }
        return null;
    }

    private QuestMapPoint? EventTarget(GameState state, int id, QuestMapPoint? at = null) => Unique(tiles
        .Where(tile => (!at.HasValue || tile.Point == at.Value) && ExplorationRules.MatchingEvent(tile.Raw.Span, state.EventStatuses) == id).Select(tile => tile.Point));

    private static QuestMapPoint? Unique(IEnumerable<QuestMapPoint> candidates)
    {
        var points = candidates.Distinct().Take(2).ToArray();
        return points.Length == 1 ? points[0] : null;
    }
}
