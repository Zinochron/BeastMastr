using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// The enemies of the room currently selected in the board window.
///
/// This is where the enemy information actually lives, and it took a wrong turn to find: the room
/// list's AtkValues carry only the move, the kind and a sentence, so the conclusion was that enemies
/// were not in the window and had to be caught from the hover panel. They are in the window — in its
/// **node tree**, which is not where AtkValues are.
///
/// `XBMStageDetailList` holds two lists: the rooms, and the enemies of whichever room is selected.
/// So no hovering is needed at all; clicking through the rooms fills everything in.
/// </summary>
public static unsafe class RoomEnemyReader
{
    public sealed record Stat(string Label, int Stars);

    public sealed record Enemy(string Name, string Weakness, IReadOnlyList<Stat> Stats)
    {
        /// <summary>The one line worth putting on a card.</summary>
        public string Summary =>
            Weakness.Length > 0 ? $"weak to {Weakness}" : string.Empty;
    }

    /// <param name="SelectedRoom">Index into the room list, which is also the room's index in <see cref="StageDetailReader"/>.</param>
    public sealed record Selection(int SelectedRoom, IReadOnlyList<Enemy> Enemies);

    /// <summary>Text node holding an enemy's name inside its row.</summary>
    private const uint EnemyNameNodeId = 9;

    /// <summary>Text node holding a room's description inside its row.</summary>
    private const uint RoomDescriptionNodeId = 11;

    public static Selection? Read()
    {
        if (!AddonReader.TryGet(XbmColumns.StageDetailList.Addon, out var addon))
            return null;

        AtkComponentList* rooms = null;
        AtkComponentList* enemies = null;

        foreach (var handle in Lists(addon))
        {
            var list = (AtkComponentList*)handle;
            if (rooms == null && HasRowWith(list, RoomDescriptionNodeId))
                rooms = list;
            else if (enemies == null && HasRowWith(list, EnemyNameNodeId))
                enemies = list;
        }

        if (rooms == null || enemies == null)
            return null;

        return new Selection(rooms->SelectedItemIndex, ReadEnemies(enemies));
    }

    private static List<Enemy> ReadEnemies(AtkComponentList* list)
    {
        var found = new List<Enemy>();

        for (var i = 0; i < list->ListLength; i++)
        {
            var renderer = list->GetItemRenderer(i);
            if (renderer == null)
                continue;

            var row = &renderer->AtkComponentButton.AtkComponentBase;
            var name = TextOf(row, EnemyNameNodeId);
            if (name.Length == 0)
                continue;

            var (weakness, stats) = ReadPairs(row);
            found.Add(new Enemy(name, weakness, stats));
        }

        return found;
    }

    /// <summary>
    /// A row's label/value pairs, told apart by their values rather than by their labels — the
    /// labels are localised and the shapes are not. A value made only of stars is a stat; the one
    /// that is not is the weakness.
    /// </summary>
    private static (string Weakness, List<Stat> Stats) ReadPairs(AtkComponentBase* row)
    {
        var stats = new List<Stat>();
        var weakness = string.Empty;

        for (uint nodeId = 1; nodeId <= 40; nodeId++)
        {
            var node = row->GetNodeById(nodeId);
            if (node == null || (uint)node->Type < 1000)
                continue;

            var pair = ((AtkComponentNode*)node)->Component;
            if (pair == null)
                continue;

            var label = TextOf(pair, 2);
            var value = TextOf(pair, 3);

            if (label.Length == 0 || value.Length == 0)
                continue;

            var stars = value.Count(c => c == '★');
            if (stars > 0)
                stats.Add(new Stat(label, stars));
            else if (weakness.Length == 0)
                weakness = Clean(value);
        }

        return (weakness, stats);
    }

    private static bool HasRowWith(AtkComponentList* list, uint nodeId)
    {
        for (var i = 0; i < list->ListLength; i++)
        {
            var renderer = list->GetItemRenderer(i);
            if (renderer == null)
                continue;

            if (TextOf(&renderer->AtkComponentButton.AtkComponentBase, nodeId).Length > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returned as plain addresses: an iterator method cannot yield a pointer, and the alternative
    /// is a wrapper type for something the caller immediately casts back anyway.
    /// </summary>
    private static List<nint> Lists(AtkUnitBase* addon)
    {
        var lists = new List<nint>();

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (uint)node->Type < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && component->GetComponentType() == ComponentType.List)
                lists.Add((nint)component);
        }

        return lists;
    }

    private static string TextOf(AtkComponentBase* component, uint nodeId)
    {
        var node = component->GetTextNodeById(nodeId);
        return node == null ? string.Empty : node->NodeText.ToString();
    }

    /// <summary>Start of Unicode's private use area, where the game keeps its element glyphs.</summary>
    private const char FirstPrivateGlyph = (char)0xE000;
    private const char LastPrivateGlyph = (char)0xF8FF;

    private static string Clean(string text) =>
        new string(text.Where(c => c < FirstPrivateGlyph || c > LastPrivateGlyph).ToArray()).Trim();
}
