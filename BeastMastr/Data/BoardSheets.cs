using System;
using System.Collections.Generic;
using BeastMastr.Rules;
using Lumina.Excel;

namespace BeastMastr.Data;

/// <summary>
/// The boards as the game data describes them: <c>XBMContentStageEventMap</c> for the cells and
/// <c>XBMContentStageEvent</c> for what each event is. Both are subrow sheets, one row per board.
///
/// Reading them needs no window, so a board is known anywhere once its row id is — and the row id is
/// the one thing the board window has to be opened for.
/// </summary>
public static class BoardSheets
{
    private static readonly Dictionary<uint, BoardGraph?> Graphs = [];

    public static IReadOnlyList<BoardCell> Cells(uint board)
    {
        var cells = new List<BoardCell>();
        var sheet = Services.Data.Excel.GetSubrowSheet<RawSubrow>(null, XbmColumns.StageEventMap.Sheet);
        if (!sheet.TryGetRow(board, out var rows))
            return cells;

        foreach (var row in rows)
        {
            cells.Add(new BoardCell(Int(row, XbmColumns.StageEventMap.X),
                                    Int(row, XbmColumns.StageEventMap.Y),
                                    Int(row, XbmColumns.StageEventMap.Type),
                                    Int(row, XbmColumns.StageEventMap.EventIndex),
                                    Int(row, XbmColumns.StageEventMap.LinkedEventIndex)));
        }

        return cells;
    }

    public static IReadOnlyList<BoardEventInfo> Events(uint board)
    {
        var events = new List<BoardEventInfo>();
        var sheet = Services.Data.Excel.GetSubrowSheet<RawSubrow>(null, XbmColumns.StageEvent.Sheet);
        if (!sheet.TryGetRow(board, out var rows))
            return events;

        foreach (var row in rows)
        {
            events.Add(new BoardEventInfo(row.SubrowId,
                                          Int(row, XbmColumns.StageEvent.Move),
                                          BoardGraph.KindOfEventType(Int(row, XbmColumns.StageEvent.EventType))));
        }

        return events;
    }

    /// <summary>The board's graph from the sheets, built once per board. Null when the board is not in the data.</summary>
    public static BoardGraph? Graph(uint board)
    {
        if (Graphs.TryGetValue(board, out var known))
            return known;

        BoardGraph? graph = null;
        try
        {
            var events = Events(board);
            if (events.Count > 0)
                graph = BoardGraph.Build(Cells(board), events);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Could not read board {board} from the sheets.");
        }

        Graphs[board] = graph;
        return graph;
    }

    private static int Int(RawSubrow row, int column) => Convert.ToInt32(row.ReadColumn(column));
}
