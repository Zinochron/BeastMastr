using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <summary>
/// What a room on the board is. The numbers match the room list's own kind ids, so a value read out
/// of the game converts by cast; <see cref="Start"/> is the platform a run begins on, which the list
/// never shows.
/// </summary>
public enum BoardRoomKind
{
    Start = -1,
    Enemy = 0,
    EliteEnemy = 1,
    Boss = 2,
    Shop = 3,
    Campsite = 4,
    Treasure = 5,
    RandomEnemyOrTreasure = 6,
}

/// <summary>One cell of the board as the game describes it: a room, or one piece of a link.</summary>
public sealed record BoardCell(int X, int Y, int Type, int EventIndex, int LinkedEventIndex);

/// <summary>What an event is: which move it belongs to and what kind of room it is.</summary>
public sealed record BoardEventInfo(int EventIndex, int Move, BoardRoomKind Kind);

/// <param name="Column">Left of the start is negative, right positive, in board cells.</param>
public sealed record BoardNode(int EventIndex, int Move, BoardRoomKind Kind, int X, int Y, int Column)
{
    public bool IsStart => Kind == BoardRoomKind.Start;

    public bool Fights => Kind is BoardRoomKind.Enemy or BoardRoomKind.EliteEnemy or BoardRoomKind.Boss
                              or BoardRoomKind.RandomEnemyOrTreasure;
}

/// <summary>
/// The board as a graph: rooms, and which room leads to which.
///
/// Built from the game's own description rather than from where tiles are drawn. Every cell is a room
/// or a piece of a link from its event to the event it names, and a link can span several cells, so
/// edges are collected as a set. Nothing here is guessed from screen positions; what does not add up
/// is listed in <see cref="Problems"/>, and a graph with problems is not walked.
/// </summary>
public sealed class BoardGraph
{
    /// <summary>The cell type the game uses for a room. Everything else is part of a link.</summary>
    public const int RoomCellType = 1;

    /// <summary>Padding at the end of a board's rows: all five bytes zero.</summary>
    public const int EmptyCellType = 0;

    private readonly Dictionary<int, BoardNode> nodes;
    private readonly Dictionary<int, List<int>> successors;
    private readonly Dictionary<int, List<int>> predecessors;

    private BoardGraph(Dictionary<int, BoardNode> nodes, Dictionary<int, List<int>> successors,
                       Dictionary<int, List<int>> predecessors, int centreX, List<string> problems)
    {
        this.nodes = nodes;
        this.successors = successors;
        this.predecessors = predecessors;
        CentreX = centreX;
        Problems = problems;
    }

    /// <summary>Every room, in event order. Event 0 is the start.</summary>
    public IReadOnlyList<BoardNode> Nodes => nodes.Values.OrderBy(node => node.EventIndex).ToList();

    public IReadOnlyList<string> Problems { get; }

    public bool IsValid => Problems.Count == 0;

    /// <summary>The start's column. Columns are counted from here.</summary>
    public int CentreX { get; }

    public BoardNode? Start => nodes.Values.FirstOrDefault(node => node.IsStart);

    public BoardNode? Boss => nodes.Values.FirstOrDefault(node => node.Kind == BoardRoomKind.Boss);

    /// <summary>The highest event index — the one the room list shows first.</summary>
    public int MaxEventIndex => nodes.Count == 0 ? -1 : nodes.Keys.Max();

    public int LastMove => nodes.Count == 0 ? 0 : nodes.Values.Max(node => node.Move);

    public BoardNode? Node(int eventIndex) => nodes.GetValueOrDefault(eventIndex);

    public IReadOnlyList<BoardNode> Next(int eventIndex) =>
        successors.TryGetValue(eventIndex, out var next) ? next.Select(index => nodes[index]).ToList() : [];

    public IReadOnlyList<BoardNode> Previous(int eventIndex) =>
        predecessors.TryGetValue(eventIndex, out var previous) ? previous.Select(index => nodes[index]).ToList() : [];

    public IReadOnlyList<BoardNode> OnMove(int move) =>
        nodes.Values.Where(node => node.Move == move).OrderBy(node => node.Column).ToList();

    /// <summary>Every edge, once each.</summary>
    public IEnumerable<(int From, int To)> Edges =>
        successors.SelectMany(pair => pair.Value.Select(to => (pair.Key, to)));

    /// <summary>
    /// Where an event sits in the room list the board window shows at the entrance: the list starts at
    /// the boss and counts back, so it is the distance from the highest event.
    /// </summary>
    public int RoomListIndex(int eventIndex) => MaxEventIndex - eventIndex;

    public static BoardGraph Build(IEnumerable<BoardCell> cells, IEnumerable<BoardEventInfo> events)
    {
        var problems = new List<string>();
        var info = new Dictionary<int, BoardEventInfo>();
        foreach (var e in events)
            info[e.EventIndex] = e;

        var cellList = cells.Where(cell => cell.Type != EmptyCellType).ToList();
        var rooms = cellList.Where(cell => cell.Type == RoomCellType).ToList();

        var centreX = rooms.FirstOrDefault(cell => cell.EventIndex == 0)?.X ?? 0;
        if (rooms.All(cell => cell.EventIndex != 0))
            problems.Add("The board has no start cell (event 0).");

        var nodes = new Dictionary<int, BoardNode>();
        foreach (var room in rooms)
        {
            if (nodes.ContainsKey(room.EventIndex))
            {
                problems.Add($"Event {room.EventIndex} has two room cells.");
                continue;
            }

            if (!info.TryGetValue(room.EventIndex, out var what))
            {
                problems.Add($"Event {room.EventIndex} has a cell but no description.");
                continue;
            }

            nodes[room.EventIndex] = new BoardNode(room.EventIndex, what.Move, what.Kind, room.X, room.Y,
                                                   room.X - centreX);
        }

        var successors = new Dictionary<int, List<int>>();
        var predecessors = new Dictionary<int, List<int>>();

        foreach (var link in cellList.Where(cell => cell.Type != RoomCellType))
        {
            if (!nodes.TryGetValue(link.EventIndex, out var from) ||
                !nodes.TryGetValue(link.LinkedEventIndex, out var to))
            {
                problems.Add($"A link joins event {link.EventIndex} to {link.LinkedEventIndex}, " +
                             "and one of them is not a room.");
                continue;
            }

            if (to.Move <= from.Move)
            {
                problems.Add($"A link runs from move {from.Move} back to move {to.Move}.");
                continue;
            }

            Add(successors, from.EventIndex, to.EventIndex);
            Add(predecessors, to.EventIndex, from.EventIndex);
        }

        var bosses = nodes.Values.Count(node => node.Kind == BoardRoomKind.Boss);
        if (bosses != 1)
            problems.Add($"The board has {bosses} boss rooms, not one.");

        var starts = nodes.Values.Count(node => node.IsStart);
        if (starts != 1)
            problems.Add($"The board has {starts} starts, not one.");

        foreach (var node in nodes.Values)
        {
            if (node.Kind != BoardRoomKind.Boss && !successors.ContainsKey(node.EventIndex))
                problems.Add($"Event {node.EventIndex} leads nowhere.");

            if (!node.IsStart && !predecessors.ContainsKey(node.EventIndex))
                problems.Add($"Nothing leads to event {node.EventIndex}.");
        }

        foreach (var list in successors.Values)
            list.Sort();

        foreach (var list in predecessors.Values)
            list.Sort();

        return new BoardGraph(nodes, successors, predecessors, centreX, problems);
    }

    private static void Add(Dictionary<int, List<int>> into, int key, int value)
    {
        if (!into.TryGetValue(key, out var list))
            into[key] = list = [];

        if (!list.Contains(value))
            list.Add(value);
    }

    /// <summary>Converts the sheet's event type to a room kind: 1 is the start, then the room list's ids plus two.</summary>
    public static BoardRoomKind KindOfEventType(int eventType) =>
        eventType <= 1 ? BoardRoomKind.Start : (BoardRoomKind)(eventType - 2);
}
