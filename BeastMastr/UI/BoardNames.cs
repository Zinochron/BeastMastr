using System.Linq;
using BeastMastr.Data;
using BeastMastr.Rules;

namespace BeastMastr.UI;

/// <summary>How rooms are named and coloured wherever the board is shown.</summary>
public static class BoardNames
{
    public static string Name(BoardRoomKind kind) => kind switch
    {
        BoardRoomKind.Start => "Start",
        BoardRoomKind.Enemy => "Enemy",
        BoardRoomKind.EliteEnemy => "Elite",
        BoardRoomKind.Boss => "Boss",
        BoardRoomKind.Shop => "Shop",
        BoardRoomKind.Campsite => "Campsite",
        BoardRoomKind.Treasure => "Treasure",
        BoardRoomKind.RandomEnemyOrTreasure => "Random",
        _ => kind.ToString(),
    };

    /// <summary>ABGR, as the draw list wants it.</summary>
    public static uint Color(BoardRoomKind kind) => kind switch
    {
        BoardRoomKind.Start => 0xFFFFFFFF,
        BoardRoomKind.Enemy => 0xFF4050E0,
        BoardRoomKind.EliteEnemy => 0xFF2090F0,
        BoardRoomKind.Boss => 0xFFC040C0,
        BoardRoomKind.Shop => 0xFF40D0E0,
        BoardRoomKind.Campsite => 0xFF50C850,
        BoardRoomKind.Treasure => 0xFF30C0FF,
        _ => 0xFFA0A0A0,
    };

    /// <summary>A room as the board window described it, or its kind and move when that is not known.</summary>
    public static string Describe(Configuration configuration, BoardModel board, BoardGraph graph, int eventIndex)
    {
        if (graph.Node(eventIndex) is not { } node)
            return "nothing";

        var saved = board.BoardRowId == configuration.LastBoardRowId
                        ? configuration.LastBoard.Rooms.FirstOrDefault(room => room.Index == graph.RoomListIndex(eventIndex)
                                                                               && room.Move == node.Move
                                                                               && room.Kind == (int)node.Kind)
                        : null;

        return saved != null ? saved.Label : $"{Name(node.Kind)} on move {node.Move}";
    }
}
