using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// Keeps the enemies of each room as the board window shows them.
///
/// Two sources, because neither is complete on its own.
///
/// The board window has a room list and, beside it, the enemies of whichever room is selected —
/// name, weakness and stats. Clicking through the rooms is what you do to read a board anyway, so
/// that fills in by itself and says **which enemies are in which room**.
///
/// What it does not say is what those enemies *do*. The statuses they inflict and whether their
/// actions can be interrupted are only in the hover panel, so that is read too, keyed by name and
/// merged in. A room hovered as well as selected gets the full picture; one only selected still
/// gets its weakness.
///
/// In memory only. A run's enemies mean nothing after it ends.
/// </summary>
public sealed class EnemyCache : IDisposable
{
    /// <summary>Frames between samples. The panel changes when the cursor moves, not per frame.</summary>
    private const int Interval = 10;

    /// <summary>Room index to the enemies the board listed for it.</summary>
    private readonly Dictionary<int, IReadOnlyList<RoomEnemyReader.Enemy>> byRoom = [];

    /// <summary>Enemy name to what the hover panel said about it, when it has been hovered.</summary>
    private readonly Dictionary<string, BattleMonsterReader.Enemy> hovered = new(StringComparer.Ordinal);

    private int ticks;

    public EnemyCache()
    {
        Services.Framework.Update += OnUpdate;
    }

    public int RoomsKnown => byRoom.Count;

    /// <summary>What the board listed for a room, or empty until that room has been selected once.</summary>
    public IReadOnlyList<RoomEnemyReader.Enemy> InRoom(int roomIndex) =>
        byRoom.TryGetValue(roomIndex, out var enemies) ? enemies : [];

    /// <summary>The hover panel's reading for an enemy, or null if it has never been hovered.</summary>
    public BattleMonsterReader.Enemy? Details(string name) =>
        hovered.TryGetValue(name, out var enemy) ? enemy : null;

    /// <summary>
    /// One line per enemy: its weakness always, and what it inflicts and whether it can be
    /// interrupted once it has been hovered.
    /// </summary>
    public string Describe(RoomEnemyReader.Enemy enemy)
    {
        var details = Details(enemy.Name);
        var summary = details?.Summary ?? enemy.Summary;

        return summary.Length > 0 ? $"{enemy.Name} — {summary}" : enemy.Name;
    }

    /// <summary>
    /// A room's enemies in the shape the briefing rules want: the weakness from the room list, the
    /// actions from the hover panel, joined by name.
    ///
    /// An enemy that has never been hovered still gets a line — its weakness alone is worth having —
    /// but with no actions, so the briefing simply says less about it rather than claiming it does
    /// nothing.
    /// </summary>
    public IReadOnlyList<BriefEnemy> Brief(int roomIndex) =>
        InRoom(roomIndex)
            .Select(enemy => new BriefEnemy(
                        enemy.Name,
                        enemy.Weakness,
                        (Details(enemy.Name)?.Actions ?? [])
                            .Select(action => new BriefAction(action.Name, action.Status,
                                                              action.Interruption, action.Nullified))
                            .ToList()))
            .ToList();

    public void Clear()
    {
        byRoom.Clear();
        hovered.Clear();
    }

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        // Replaced rather than merged: the nullification note reflects the team you have right now.
        if (BattleMonsterReader.Read() is { } detailed)
            hovered[detailed.Name] = detailed;

        if (RoomEnemyReader.Read() is not { } selection || selection.Enemies.Count == 0)
            return;

        // A room with enemies listed is a room whose enemies are now known. Rooms that hold no
        // enemies never fill this in, which is correct — there is nothing to say about a shop.
        byRoom[selection.SelectedRoom] = selection.Enemies;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
