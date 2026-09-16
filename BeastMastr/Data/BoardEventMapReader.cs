using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// The board window's own model of the board: which cell is which event, which link joins which
/// two events, and which event the run is on.
///
/// <see cref="StageMapReader"/> reads where the tiles are drawn and nothing else, and had to work out
/// rows and moves from screen positions. The component holds the answer outright —
/// <c>EventMapEntries</c> is the same five bytes per cell as the <c>XBMContentStageEventMap</c> sheet,
/// and <c>CurrentEventIndex</c> is the event the board marks — so nothing here is inferred from
/// where something happens to be drawn.
/// </summary>
public static unsafe class BoardEventMapReader
{
    /// <summary>One cell of the board as the sheet describes it. See <see cref="XbmColumns.StageEventMap"/>.</summary>
    public sealed record Cell(int Index, int X, int Y, int Type, int EventIndex, int LinkedEventIndex, uint State)
    {
        public bool IsRoom => Type == XbmColumns.StageEventMap.RoomCellType;
    }

    /// <summary>
    /// One drawn tile. <paramref name="CellIndex"/> is its index into <see cref="Snapshot.Cells"/>,
    /// handed out by the component rather than guessed from the position.
    /// </summary>
    /// <param name="Template">Which of the component's five templates drew it: rooms, straight links, the diagonals.</param>
    /// <param name="NodeId">The tile's own node id — 30001 upward for rooms, 40001 for straight links, and so on.</param>
    public sealed record Tile(int Template, uint TemplateNodeId, uint NodeId, int CellIndex, bool IsCurrent,
                              uint TimelineState, bool Visible, Vector2 ScreenPosition, Vector2 Size)
    {
        public Vector2 Centre => ScreenPosition + (Size / 2f);
    }

    /// <param name="BoardRowId">Which board, as the row of <c>XBMContentStageEventMap</c>. The board's signature.</param>
    /// <param name="CurrentEventIndex">
    /// The event the window has selected — it follows clicks on the tiles, so it is **not** where the
    /// run stands. <see cref="MarkedEvent"/> is.
    /// </param>
    public sealed record Snapshot(string Addon, uint BoardRowId, int GridSize, int GridHalfSize, bool Loaded,
                                  int CurrentEventIndex, IReadOnlyList<Cell> Cells, IReadOnlyList<Tile> Tiles)
    {
        /// <summary>
        /// The event of the room tile flagged as current, or -1. Unlike <see cref="CurrentEventIndex"/>
        /// this does not follow the selection.
        /// </summary>
        public int MarkedEvent
        {
            get
            {
                foreach (var tile in Tiles)
                {
                    if (tile.IsCurrent && tile.CellIndex >= 0 && tile.CellIndex < Cells.Count && Cells[tile.CellIndex].IsRoom)
                        return Cells[tile.CellIndex].EventIndex;
                }

                return -1;
            }
        }

        /// <summary>Whether any drawn tile is marked current — which only ever happens inside a run.</summary>
        public bool MarksCurrent
        {
            get
            {
                foreach (var tile in Tiles)
                {
                    if (tile.IsCurrent)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>The board as the window has it now, or null when no board window is up.</summary>
    public static Snapshot? Read()
    {
        if (!StageMapReader.Find(out var addonName, out _, out var map))
            return null;

        var cells = new List<Cell>();
        if (map->IsEventMapLoaded && map->EventMapEntries != null)
        {
            for (var i = 0; i < map->EventMapEntryCount; i++)
            {
                var entry = map->EventMapEntries[i];
                cells.Add(new Cell(i, entry.X, entry.Y, entry.Type, entry.EventIndex, entry.LinkedEventIndex,
                                   entry.State));
            }
        }

        var tiles = new List<Tile>();
        var template = 0;
        foreach (ref var entry in map->Entries)
        {
            var components = entry.Components;
            var indices = entry.EventMapEntryIndices;
            var current = entry.IsCurrentEvent;
            var states = entry.TimelineStates;

            for (var i = 0; i < entry.ComponentCount && i < components.Length; i++)
            {
                var component = components[i].Value;
                if (component == null)
                    continue;

                var node = component->OwnerNode;
                if (node == null)
                    continue;

                var resNode = (AtkResNode*)node;
                tiles.Add(new Tile(template,
                                   entry.TemplateNodeId,
                                   resNode->NodeId,
                                   indices[i],
                                   current[i],
                                   states[i],
                                   resNode->IsVisible(),
                                   new Vector2(resNode->ScreenX, resNode->ScreenY),
                                   new Vector2(resNode->GetWidth() * resNode->ScaleX,
                                               resNode->GetHeight() * resNode->ScaleY)));
            }

            template++;
        }

        return new Snapshot(addonName, map->XBMContentStageEventMapRowId, map->GridSize, map->GridHalfSize,
                            map->IsEventMapLoaded, map->CurrentEventIndex, cells, tiles);
    }
}
