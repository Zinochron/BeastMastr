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

    /// <summary>
    /// The boards there are, in the order the entrance lists them: their row and their name. Read from
    /// <c>XBMContent</c>, whose first column is the duty the board is, so the names are the game's own.
    /// </summary>
    public static IReadOnlyList<(uint Row, string Name)> Boards()
    {
        if (boards != null)
            return boards;

        var found = new List<(uint Row, string Name)>();
        try
        {
            var sheet = Services.Data.Excel.GetSheet<RawRow>(null, XbmColumns.XbmContent.Sheet);
            var duties = Services.Data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>();
            foreach (var row in sheet)
            {
                var duty = Convert.ToUInt32(row.ReadColumn(XbmColumns.XbmContent.ContentFinderCondition));
                if (duty == 0)
                    continue;

                var name = duties.GetRowOrDefault(duty)?.Name.ExtractText() ?? string.Empty;
                found.Add((row.RowId, name.Length > 0 ? name : $"Board {row.RowId}"));
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not read the boards from XBMContent.");
        }

        boards = found;
        return boards;
    }

    /// <summary>What a board is called, or its row when the sheet does not say.</summary>
    public static string Name(uint board)
    {
        foreach (var (row, name) in Boards())
        {
            if (row == board)
                return name;
        }

        return $"Board {board}";
    }

    private static List<(uint Row, string Name)>? boards;

    private static int Int(RawSubrow row, int column) => Convert.ToInt32(row.ReadColumn(column));
}
