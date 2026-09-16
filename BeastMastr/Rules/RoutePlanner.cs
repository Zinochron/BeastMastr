using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <param name="Order">Room kinds from most to least wanted. Kinds left out rank below every listed one.</param>
/// <param name="CampsiteBelowHpShare">
/// A campsite goes to the front whenever the most hurt familiar has less than this share of its HP left.
/// </param>
/// <param name="AvoidElite">Never take an elite room where the board offers something else.</param>
public sealed record RoutePreferences(IReadOnlyList<BoardRoomKind> Order, float CampsiteBelowHpShare, bool AvoidElite)
{
    public static readonly IReadOnlyList<BoardRoomKind> DefaultOrder =
    [
        BoardRoomKind.Treasure,
        BoardRoomKind.Shop,
        BoardRoomKind.Campsite,
        BoardRoomKind.Enemy,
        BoardRoomKind.RandomEnemyOrTreasure,
        BoardRoomKind.EliteEnemy,
    ];

    public static RoutePreferences Default => new(DefaultOrder, 0.6f, false);
}

/// <param name="Events">The rooms to take, one per move, starting with the next one.</param>
/// <param name="ChosenByHand">The events among them that were picked on the board rather than by preference.</param>
public sealed record RoutePlan(IReadOnlyList<int> Events, IReadOnlySet<int> ChosenByHand, IReadOnlyList<string> Notes)
{
    public int Next => Events.Count > 0 ? Events[0] : -1;
}

/// <summary>
/// Which way to go.
///
/// Picks one whole path to the boss, not one room at a time: a room chosen for itself can lead into a
/// lane whose next rooms are worse than the other lane's, and only a whole path sees that. The board is
/// small and strictly forward, so every path is scored and the best kept.
///
/// A room chosen by hand on the board is binding wherever it can still be reached. Planned again
/// before every step, so a campsite comes forward the moment the team gets hurt.
/// </summary>
public static class RoutePlanner
{
    private const int ElitePenalty = 1000;

    /// <param name="from">The event the run is at. Its own room is not part of the plan.</param>
    /// <param name="chosen">Rooms picked on the board, by event index.</param>
    /// <param name="lowestFamiliarHpShare">The most hurt familiar's share of HP, 1 when not known.</param>
    /// <param name="blocked">Edges not to take — the terrain scan found them unsafe.</param>
    public static RoutePlan Plan(BoardGraph graph, int from, IReadOnlySet<int> chosen, RoutePreferences preferences,
                                 float lowestFamiliarHpShare, IReadOnlySet<(int From, int To)> blocked)
    {
        var notes = new List<string>();

        if (graph.Node(from) is not { } start)
            return new RoutePlan([], new HashSet<int>(), ["The run's position is not on this board."]);

        var reachable = Reachable(graph, from, blocked);

        // A room picked on a move this path can still reach binds that move; one it cannot reach is
        // reported and dropped rather than making every path invalid.
        var binding = new Dictionary<int, int>();
        foreach (var pick in chosen.OrderBy(pick => pick))
        {
            if (graph.Node(pick) is not { } node || node.Move <= start.Move)
                continue;

            if (!reachable.Contains(pick))
            {
                notes.Add($"The chosen room on move {node.Move} can no longer be reached from here.");
                continue;
            }

            if (binding.ContainsKey(node.Move))
            {
                notes.Add($"Two rooms are chosen on move {node.Move}; the first one counts.");
                continue;
            }

            binding[node.Move] = pick;
        }

        var hurt = lowestFamiliarHpShare < preferences.CampsiteBelowHpShare;
        var best = new Dictionary<int, (int Score, List<int> Path)?>();

        (int Score, List<int> Path)? Best(int eventIndex)
        {
            if (best.TryGetValue(eventIndex, out var known))
                return known;

            var node = graph.Node(eventIndex)!;
            (int Score, List<int> Path)? result = null;

            if (node.Kind == BoardRoomKind.Boss)
            {
                result = (Weight(node.Kind, preferences, hurt), [eventIndex]);
            }
            else
            {
                foreach (var next in graph.Next(eventIndex))
                {
                    if (blocked.Contains((eventIndex, next.EventIndex)))
                        continue;

                    if (binding.TryGetValue(next.Move, out var bound) && bound != next.EventIndex)
                        continue;

                    if (Best(next.EventIndex) is not { } tail)
                        continue;

                    var score = tail.Score + (node.EventIndex == from ? 0 : Weight(node.Kind, preferences, hurt));

                    // Ties go to the lower event index, so the same board plans the same way twice.
                    if (result == null || score > result.Value.Score ||
                        (score == result.Value.Score && next.EventIndex < result.Value.Path[1]))
                    {
                        result = (score, [eventIndex, .. tail.Path]);
                    }
                }
            }

            best[eventIndex] = result;
            return result;
        }

        if (Best(from) is not { } plan)
        {
            notes.Add("No path to the boss is left open.");
            return new RoutePlan([], new HashSet<int>(), notes);
        }

        var events = plan.Path.Skip(1).ToList();
        var byHand = events.Where(binding.ContainsValue).ToHashSet();
        return new RoutePlan(events, byHand, notes);
    }

    /// <summary>How much a room is wanted. Higher is better; the order list decides, the team's health can override it.</summary>
    public static int Weight(BoardRoomKind kind, RoutePreferences preferences, bool familiarsHurt)
    {
        if (kind == BoardRoomKind.Campsite && familiarsHurt)
            return 100;

        var rank = preferences.Order.ToList().IndexOf(kind);
        var weight = rank < 0 ? 0 : preferences.Order.Count - rank;

        if (kind == BoardRoomKind.EliteEnemy && preferences.AvoidElite)
            weight -= ElitePenalty;

        return weight;
    }

    private static HashSet<int> Reachable(BoardGraph graph, int from, IReadOnlySet<(int From, int To)> blocked)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in graph.Next(current))
            {
                if (!blocked.Contains((current, next.EventIndex)) && seen.Add(next.EventIndex))
                    queue.Enqueue(next.EventIndex);
            }
        }

        return seen;
    }
}
