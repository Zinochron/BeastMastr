using System;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// Raw view of any Excel sheet. Exists because the Beastmaster sheets have no column names
/// upstream: the only way to learn that column 7 is the second beast action is to look at it
/// next to a beast whose actions you know.
/// </summary>
public sealed class SheetsTab : ITab
{
    private readonly Configuration configuration;

    private string sheetName;
    private int offset;
    private SheetExplorer.Dump? dump;
    private string status = string.Empty;

    // Link resolver: turn a raw id in some unnamed column into a name you recognise.
    private string linkSheet = "Action";
    private int linkRowId;
    private string linkResult = string.Empty;
    private string lastSavePath = string.Empty;

    public SheetsTab(Configuration configuration)
    {
        this.configuration = configuration;
        sheetName = configuration.LastSheet;
    }

    public string Title => "Sheets";
    public string Id => "sheets";

    public void Draw()
    {
        DrawPicker();
        ImGuiHelpers.ScaledDummy(4f);
        DrawLinkResolver();
        ImGuiHelpers.ScaledDummy(4f);
        DrawTable();
    }

    private void DrawPicker()
    {
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("##sheetName", ref sheetName, 64))
            dump = null;

        ImGui.SameLine();
        if (ImGui.Button("Load"))
            Load(0);

        ImGui.SameLine();
        using (var combo = ImRaii.Combo("##knownSheets", "Beastmaster sheets"))
        {
            if (combo.Success)
            {
                foreach (var (sheet, note) in BeastmasterData.Sheets)
                {
                    if (ImGui.Selectable(sheet))
                    {
                        sheetName = sheet;
                        Load(0);
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(note);
                }
            }
        }

        if (dump == null)
        {
            ImGui.TextDisabled(status.Length > 0 ? status : "Pick a sheet and press Load.");
            return;
        }

        ImGui.TextUnformatted($"{dump.Sheet}: {dump.RowCount} rows, {dump.ColumnCount} columns.");
        Widgets.HelpMarker(
            "Column count is worth writing down. If a patch changes it, every index this plugin " +
            "relies on has to be re-checked.");

        ImGui.SameLine();
        Widgets.CopyButton("Copy page as TSV", SheetExplorer.ToTsv(dump));

        if (ImGui.SmallButton("Save whole sheet to file"))
        {
            var whole = SheetExplorer.Read(sheetName, 0, int.MaxValue);
            lastSavePath = whole == null
                               ? "sheet vanished between load and save"
                               : Data.CaptureStore.Save(sheetName, SheetExplorer.ToTsv(whole))
                                 ?? "could not be written — see the log";
        }

        if (lastSavePath.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(lastSavePath);
        }

        var page = configuration.SheetPageSize;
        if (ImGui.SmallButton("<< first")) Load(0);
        ImGui.SameLine();
        if (ImGui.SmallButton("< prev")) Load(offset - page);
        ImGui.SameLine();
        if (ImGui.SmallButton("next >")) Load(offset + page);
        ImGui.SameLine();
        ImGui.TextDisabled($"rows {offset}..{Math.Min(dump.RowCount, offset + page) - 1}");
    }

    private void Load(int newOffset)
    {
        offset = Math.Max(0, newOffset);
        dump = SheetExplorer.Read(sheetName, offset, configuration.SheetPageSize);

        if (dump == null)
        {
            status = $"No sheet called \"{sheetName}\" in this client.";
            return;
        }

        status = string.Empty;
        configuration.LastSheet = sheetName;
        configuration.Save();
    }

    /// <summary>
    /// The workhorse of column identification: read an id out of the table, type it here with the
    /// sheet you suspect it points at, and see whether a name you recognise comes back.
    /// </summary>
    private void DrawLinkResolver()
    {
        if (!ImGui.CollapsingHeader("Resolve an id"))
            return;

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("target sheet", ref linkSheet, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("row id", ref linkRowId, 0);
        ImGui.SameLine();

        if (ImGui.Button("Resolve"))
        {
            var name = SheetExplorer.ResolveName(linkSheet, (uint)Math.Max(0, linkRowId));
            linkResult = name switch
            {
                null => $"{linkSheet}#{linkRowId}: no such sheet or row.",
                "" => $"{linkSheet}#{linkRowId}: row exists but has no text.",
                _ => $"{linkSheet}#{linkRowId} = {name}",
            };
        }

        if (linkResult.Length > 0)
            ImGui.TextWrapped(linkResult);
    }

    private void DrawTable()
    {
        if (dump == null || dump.Rows.Count == 0)
            return;

        // One extra column for the row id. ImGui's table column ceiling is 64, and some XBM sheets
        // are wider than that, so the tail is dropped rather than crashing the draw.
        var columns = Math.Min(dump.ColumnCount, 62);

        using var child = ImRaii.Child("##sheetBody", Vector2.Zero, true, ImGuiWindowFlags.HorizontalScrollbar);
        if (!child.Success)
            return;

        if (columns < dump.ColumnCount)
            ImGui.TextDisabled($"Showing the first {columns} of {dump.ColumnCount} columns.");

        using var table = ImRaii.Table("##sheetTable", columns + 1,
                                       ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                       ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("row");
        for (var column = 0; column < columns; column++)
            ImGui.TableSetupColumn($"{column}\n{Short(dump.ColumnTypes[column].ToString())}");

        ImGui.TableHeadersRow();

        foreach (var row in dump.Rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.RowId.ToString());

            for (var column = 0; column < columns; column++)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Cells[column]);
            }
        }
    }

    /// <summary>Column type names are long enough to push every column wide; the tail is what differs.</summary>
    private static string Short(string type) => type
                                                .Replace("PackedBool", "pb")
                                                .Replace("String", "str")
                                                .Replace("Bool", "b")
                                                .Replace("Int", "i")
                                                .Replace("UInt", "u")
                                                .Replace("Float", "f");

    public void Dispose() { }
}
