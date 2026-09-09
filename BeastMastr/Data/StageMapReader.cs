using System.Collections.Generic;
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
