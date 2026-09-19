using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <summary>A room icon on the map, with where it floats in the world.</summary>
/// <param name="Kind">What the icon says the room is, or null for an icon not identified yet.</param>
public sealed record MapPoint(int MapX, int MapY, BoardRoomKind? Kind, float WorldX, float WorldZ);

public sealed class BoardJoinResult
{
    public BoardJoinResult(IReadOnlyDictionary<int, MapPoint> byEvent, IReadOnlyList<string> problems, int columnUnit)
    {
        ByEvent = byEvent;
        Problems = problems;
        ColumnUnit = columnUnit;
    }

    /// <summary>Each room's icon, by event index. The start has none.</summary>
    public IReadOnlyDictionary<int, MapPoint> ByEvent { get; }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>Map units per board column, taken from the icons themselves.</summary>
    public int ColumnUnit { get; }

    public bool IsComplete => Problems.Count == 0;
}

/// <summary>
/// Which map icon belongs to which room.
///
/// The icons carry a map position and a kind; the rooms carry a board cell and a kind. Rows pair by
/// order — the icon row nearest the start with the room row nearest the start — and columns by
/// position, the middle column being map X zero. Every pair must agree on the kind, so a board whose
/// icons have shifted, or a join that is off by a row, fails loudly instead of sending the walker to
/// the wrong platform.
/// </summary>
public static class BoardJoin
{
    public static BoardJoinResult Join(BoardGraph graph, IReadOnlyList<MapPoint> markers)
    {
        var problems = new List<string>();
        var byEvent = new Dictionary<int, MapPoint>();

        var rooms = graph.Nodes.Where(node => !node.IsStart).ToList();

        var unit = markers.Select(marker => Math.Abs(marker.MapX)).Where(x => x > 0).DefaultIfEmpty(1).Min();

        // Y counts down the board window and map Y counts down the map, and the start is at the bottom
        // of both: the largest value is the row nearest the start.
        var roomRows = rooms.Select(node => node.Y).Distinct().OrderByDescending(y => y).ToList();
        var markerRows = markers.Select(marker => marker.MapY).Distinct().OrderByDescending(y => y).ToList();

        if (roomRows.Count != markerRows.Count)
        {
            problems.Add($"The board has {roomRows.Count} rows of rooms and the map {markerRows.Count} rows of icons.");
            return new BoardJoinResult(byEvent, problems, unit);
        }

        var claimed = new HashSet<MapPoint>();

        foreach (var room in rooms)
        {
            var row = roomRows.IndexOf(room.Y);
            var candidates = markers.Where(marker => markerRows.IndexOf(marker.MapY) == row
                                                     && (int)Math.Round(marker.MapX / (double)unit) == room.Column)
                                    .ToList();

            if (candidates.Count != 1)
            {
                problems.Add($"Event {room.EventIndex} (row {row}, column {room.Column}) has " +
                             $"{candidates.Count} icons where one was expected.");
                continue;
            }

            var marker = candidates[0];
            if (marker.Kind is { } kind && kind != room.Kind)
            {
                problems.Add($"Event {room.EventIndex} is a {room.Kind} but its icon says {kind}.");
                continue;
            }

            if (!claimed.Add(marker))
            {
                problems.Add($"Two rooms claim the icon at {marker.MapX}/{marker.MapY}.");
                continue;
            }

            byEvent[room.EventIndex] = marker;
        }

        var spare = markers.Count - claimed.Count;
        if (spare > 0 && problems.Count == 0)
            problems.Add($"{spare} icon(s) belong to no room.");

        return new BoardJoinResult(byEvent, problems, unit);
    }
}
