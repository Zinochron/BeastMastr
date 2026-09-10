using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// Where the rooms are on screen, and which one you are standing in.
///
/// The board window carries no text at all, so this answers only "a room is here" — everything a
/// card says comes from <see cref="StageDetailReader"/>. What makes the pairing reliable is that
/// the board's own component hands out an index per position rather than leaving it to be guessed
/// from where the tiles sit.
/// </summary>
public static unsafe class StageMapReader
{
    /// <summary>One room as the board draws it.</summary>
    /// <param name="DetailIndex">The board's own index for this position, which is what pairs it with the room list.</param>
    /// <param name="IsCurrent">The room the run is standing in.</param>
    public sealed record BoardRoom(
        int DetailIndex,
        bool IsCurrent,
        Vector2 ScreenPosition,
        Vector2 Size)
    {
        public Vector2 Centre => ScreenPosition + (Size / 2f);
    }

    /// <summary>
    /// The board is drawn by two different windows: <c>XBMStageMap</c> before a run, on the board
    /// selection screen, and <c>XBMStageDetailList</c> during one, which embeds the same component.
    /// Looking only at the first meant the overlay never appeared inside a run at all.
    /// </summary>
    private static readonly string[] BoardWindows =
        [XbmColumns.StageMap.Addon, XbmColumns.StageDetailList.Addon];

    public static bool IsOpen => Find(out _, out _);

    private static bool Find(out AtkUnitBase* addon, out AtkComponentXBMContentStageEventMap* map)
    {
        foreach (var name in BoardWindows)
        {
            if (!AddonReader.TryGet(name, out var candidate))
                continue;

            var found = FindEventMap(candidate);
            if (found == null)
                continue;

            addon = candidate;
            map = found;
            return true;
        }

        addon = null;
        map = null;
        return false;
    }

    public static List<BoardRoom> Read()
    {
        var rooms = new List<BoardRoom>();

        if (!Find(out _, out var map))
            return rooms;

        foreach (ref var entry in map->Entries)
        {
            var components = entry.Components;
            var indices = entry.EventMapEntryIndices;
            var current = entry.IsCurrentEvent;

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i].Value;
                if (component == null)
                    continue;

                var node = component->OwnerNode;
                if (node == null || !node->IsVisible())
                    continue;

                rooms.Add(new BoardRoom(
                              i < indices.Length ? indices[i] : -1,
                              i < current.Length && current[i],
                              new Vector2(node->ScreenX, node->ScreenY),
                              new Vector2(node->GetWidth() * node->ScaleX, node->GetHeight() * node->ScaleY)));
            }
        }

        return rooms;
    }

    /// <summary>
    /// The board's tiles grouped into rows, bottom first, with the connector graphics dropped.
    ///
    /// The board is a ladder drawn bottom to top, and the connecting lines between two rooms are
    /// entries of the same component with positions of their own — so the rows alternate, room row,
    /// link row, room row. Taking every second row from the bottom leaves the rooms: the first is
    /// where the run starts, and after that there is exactly one row per move.
    ///
    /// That is checked rather than trusted. On a real board of twelve rooms across nine moves this
    /// produced ten rows holding 1, 1, 2, 2, 1, 1, 1, 2, 1, 1 tiles, which is the start plus the
    /// room list's own per-move counts, branch for branch. <see cref="MoveOf"/> repeats that check
    /// every time and gives up rather than answering from a board it does not recognise.
    /// </summary>
    public static List<List<BoardRoom>> Rows()
    {
        return Read()
               .GroupBy(room => MathF.Round(room.ScreenPosition.Y))
               .OrderByDescending(row => row.Key)
               .Where((_, index) => index % 2 == 0)
               .Select(row => row.OrderBy(room => room.ScreenPosition.X).ToList())
               .ToList();
    }

    /// <summary>
    /// Which move the run is standing on: 0 at the start, 1 once the first room is done. Returns -1
    /// when the board is not open, when it does not mark a current room, or when its rows do not
    /// match <paramref name="roomsPerMove"/> — the counts the room list gives for the same board.
    ///
    /// Disagreeing counts mean the row-to-move mapping does not hold here, and a move read off a
    /// mapping that does not hold is worse than no move at all: it would brief the wrong room with
    /// nothing to say it had.
    /// </summary>
    /// <param name="roomsPerMove">How many rooms each move offers, move 1 first.</param>
    public static int MoveOf(IReadOnlyList<int> roomsPerMove)
    {
        var rows = Rows();

        // One row per move, plus the row the run starts on.
        if (rows.Count != roomsPerMove.Count + 1)
            return -1;

        for (var move = 0; move < roomsPerMove.Count; move++)
        {
            if (rows[move + 1].Count != roomsPerMove[move])
                return -1;
        }

        for (var move = 0; move < rows.Count; move++)
        {
            if (rows[move].Any(room => room.IsCurrent))
                return move;
        }

        return -1;
    }

    /// <summary>
    /// The board's event map, found by walking the window rather than by node id — the id was 2 in
    /// the capture, but a component located by what it *is* survives a layout change that a
    /// hardcoded id would not.
    /// </summary>
    private static AtkComponentXBMContentStageEventMap* FindEventMap(AtkUnitBase* addon)
    {
        var list = addon->UldManager.NodeList;

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = list[i];
            if (node == null || (uint)node->Type < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component == null)
                continue;

            if (component->GetComponentType() == ComponentType.XBMContentStageEventMap)
                return (AtkComponentXBMContentStageEventMap*)component;
        }

        return null;
    }
}
