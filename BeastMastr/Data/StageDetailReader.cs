using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Rules;

namespace BeastMastr.Data;

/// <summary>
/// What is in each room of the board, out of <c>XBMStageDetailList</c>.
///
/// One block of forty AtkValues per room, listed from the last move backwards. The window is the
/// only place this exists — the board itself carries no text — and it is open whenever the board
/// is, so the cards do not have to cache anything.
/// </summary>
public static unsafe class StageDetailReader
{
    /// <summary>
    /// One room. <paramref name="Label"/> and <paramref name="Detail"/> are the two halves of the
    /// game's own sentence, split at the colon: "Elite Enemy #2" and "Combat 3 types of beast."
    /// Splitting rather than writing our own labels keeps it in the player's language for free.
    /// </summary>
    public sealed record Room(int Index, int Move, XbmColumns.RoomKind Kind, string Label, string Detail);

    public static bool IsOpen => AddonReader.IsOpen(XbmColumns.StageDetailList.Addon);

    /// <summary>
    /// Every room the window describes, in the order it lists them — which is also the order the
    /// board's own indices are expected to use, so a room's position in this list is its index.
    /// </summary>
    public static List<Room> Read()
    {
        var rooms = new List<Room>();

        var addon = AddonReader.Find(XbmColumns.StageDetailList.Addon);
        if (addon.IsNull)
            return rooms;

        // AtkValues comes back as a lazy sequence; the reader indexes it repeatedly, so it is
        // materialised once rather than walked from the start for every lookup.
        var values = addon.AtkValues.ToList();

        for (var block = 0; ; block++)
        {
            var descriptionIndex = XbmColumns.StageDetailList.Value(block, XbmColumns.StageDetailList.DescriptionOffset);
            if (descriptionIndex >= values.Count)
                break;

            var description = Text(values, descriptionIndex);
            if (string.IsNullOrWhiteSpace(description))
                break;

            var move = Int(values, XbmColumns.StageDetailList.Value(block, XbmColumns.StageDetailList.MoveOffset));
            var kind = Int(values, XbmColumns.StageDetailList.Value(block, XbmColumns.StageDetailList.KindOffset));

            var (label, detail) = Split(description);
            rooms.Add(new Room(block, move ?? 0, (XbmColumns.RoomKind)(kind ?? 0), label, detail));
        }

        return rooms;
    }

    /// <summary>The game writes "Label: detail." Anything without a colon is kept whole.</summary>
    private static (string Label, string Detail) Split(string description)
    {
        var colon = description.IndexOf(':');
        return colon < 0
                   ? (description.Trim(), string.Empty)
                   : (description[..colon].Trim(), description[(colon + 1)..].Trim());
    }

    private static string Text(IReadOnlyList<Dalamud.Game.NativeWrapper.AtkValuePtr> values, int index)
    {
        if (index < 0 || index >= values.Count)
            return string.Empty;

        return values[index].GetValue()?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Numbers here arrive as UInt, and asking for an Int gets a refusal rather than a conversion —
    /// which read every room as move 0 of kind Enemy without failing anywhere. Both are accepted.
    /// </summary>
    private static int? Int(IReadOnlyList<Dalamud.Game.NativeWrapper.AtkValuePtr> values, int index)
    {
        if (index < 0 || index >= values.Count)
            return null;

        if (values[index].TryGet<int>(out var signed))
            return signed;

        if (values[index].TryGet<uint>(out var unsigned))
            return (int)unsigned;

        return null;
    }
}
