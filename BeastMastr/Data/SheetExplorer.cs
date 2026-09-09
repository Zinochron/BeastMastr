using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;

namespace BeastMastr.Data;

/// <summary>
/// Reads any Excel sheet by name without a schema, one raw column at a time.
///
/// The Beastmaster sheets are unnamed upstream, so nothing here can go through Lumina's generated
/// structs — those only expose the handful of columns somebody has already named. Reading raw is
/// the only way to see the rest, and seeing the rest is the whole point of Phase 0.
/// </summary>
public static class SheetExplorer
{
    /// <summary>One row, already stringified, because the UI only ever prints it.</summary>
    public sealed record Row(uint RowId, string[] Cells);

    public sealed record Dump(string Sheet, int RowCount, ExcelColumnDataType[] ColumnTypes, List<Row> Rows)
    {
        public int ColumnCount => ColumnTypes.Length;
    }

    /// <summary>
    /// A page of <paramref name="sheet"/> starting at row index <paramref name="offset"/>, or null
    /// when the sheet does not exist in this client — which for an XBM sheet means the patch moved
    /// or renamed it, and is worth seeing rather than swallowing.
    /// </summary>
    public static Dump? Read(string sheet, int offset, int limit)
    {
        ExcelSheet<RawRow> raw;
        try
        {
            raw = Services.Data.Excel.GetSheet<RawRow>(null, sheet);
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, $"Sheet \"{sheet}\" could not be opened.");
            return null;
        }

        var types = raw.Columns.Select(column => column.Type).ToArray();
        var rows = new List<Row>();

        var start = Math.Clamp(offset, 0, Math.Max(0, raw.Count - 1));
        var end = Math.Min(raw.Count, start + Math.Max(1, limit));

        for (var i = start; i < end; i++)
        {
            var row = raw.GetRowAt(i);
            var cells = new string[types.Length];

            for (var column = 0; column < types.Length; column++)
                cells[column] = Cell(row, column, types[column]);

            rows.Add(new Row(row.RowId, cells));
        }

        return new Dump(sheet, raw.Count, types, rows);
    }

    /// <summary>
    /// Strings are read through the typed accessor rather than <c>ReadColumn</c>, because the boxed
    /// object for a string column is a SeString whose ToString carries payload markup along with it.
    /// </summary>
    private static string Cell(RawRow row, int column, ExcelColumnDataType type)
    {
        try
        {
            if (type == ExcelColumnDataType.String)
                return row.ReadStringColumn(column).ExtractText();

            return row.ReadColumn(column)?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, $"Column {column} of row {row.RowId} could not be read.");
            return "?";
        }
    }

    /// <summary>
    /// The first string column of <paramref name="sheet"/> row <paramref name="rowId"/>. Enough to
    /// turn a raw id in some unnamed column into a name you recognise, which is how a column gets
    /// identified in the first place.
    /// </summary>
    public static string? ResolveName(string sheet, uint rowId)
    {
        try
        {
            var raw = Services.Data.Excel.GetSheet<RawRow>(null, sheet);
            if (!raw.TryGetRow(rowId, out var row))
                return null;

            for (var column = 0; column < row.Columns.Count; column++)
            {
                if (row.Columns[column].Type != ExcelColumnDataType.String)
                    continue;

                var text = row.ReadStringColumn(column).ExtractText();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, $"Could not resolve {sheet}#{rowId}.");
            return null;
        }
    }

    /// <summary>Tab separated, so a dump pastes straight into README-DEV.md or a spreadsheet.</summary>
    public static string ToTsv(Dump dump)
    {
        var text = new StringBuilder();

        text.Append("RowId");
        for (var column = 0; column < dump.ColumnCount; column++)
            text.Append('\t').Append(column).Append('/').Append(dump.ColumnTypes[column]);
        text.AppendLine();

        foreach (var row in dump.Rows)
        {
            text.Append(row.RowId);
            foreach (var cell in row.Cells)
                text.Append('\t').Append(cell.Replace('\t', ' ').Replace('\n', ' '));
            text.AppendLine();
        }

        return text.ToString();
    }
}
