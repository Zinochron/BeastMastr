using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// Keeps what the enemy panel said, because the panel does not stay.
///
/// It exists only while the cursor rests on an enemy, so the information you want on a card is
/// visible exactly when you no longer need a card. Reading it while it is up and keeping it is the
/// whole trick — and hovering enemies is what you do on the board anyway, so the cache fills itself
/// during normal play rather than needing a collection pass.
///
/// Which room an enemy belongs to is not in the data anywhere — the room list carries only the
/// move, the kind and a sentence. But the hovering happens over the board's own rooms, so the room
/// under the cursor when the panel appears *is* the enemy's room. That attribution costs nothing
/// and needs no extra clicking, and without it the cards could say what an enemy does but not where
/// it is.
///
/// In memory only. A run's enemies mean nothing after it ends.
/// </summary>
public sealed class EnemyCache : IDisposable
{
    /// <summary>Frames between samples. The panel changes when the cursor moves, not per frame.</summary>
    private const int Interval = 10;

    private readonly Dictionary<string, BattleMonsterReader.Enemy> byName = new(StringComparer.Ordinal);

    /// <summary>Room index to the enemies seen while hovering it, in the order they were seen.</summary>
    private readonly Dictionary<int, List<string>> byRoom = [];

    private int ticks;

    public EnemyCache()
    {
        Services.Framework.Update += OnUpdate;
    }

    public int Count => byName.Count;

    public IReadOnlyCollection<BattleMonsterReader.Enemy> All => byName.Values;

    /// <summary>What has been seen in a room, in the order it was seen. Empty until it is hovered.</summary>
    public IReadOnlyList<BattleMonsterReader.Enemy> InRoom(int roomIndex) =>
        byRoom.TryGetValue(roomIndex, out var names)
            ? names.Select(Get).Where(enemy => enemy != null).ToList()!
            : [];

    public int RoomsKnown => byRoom.Count;

    /// <summary>
    /// Attributes whatever is hovered right now to the room under the cursor. Called from a draw
    /// pass because that is the only place the mouse position is available.
    /// </summary>
    public void AttributeHover(Vector2 mouse)
    {
        if (BattleMonsterReader.Read() is not { } enemy)
            return;

        byName[enemy.Name] = enemy;

        foreach (var room in StageMapReader.Read())
        {
            if (mouse.X < room.ScreenPosition.X || mouse.Y < room.ScreenPosition.Y
                || mouse.X > room.ScreenPosition.X + room.Size.X
                || mouse.Y > room.ScreenPosition.Y + room.Size.Y)
                continue;

            var seen = byRoom.TryGetValue(room.DetailIndex, out var list) ? list : byRoom[room.DetailIndex] = [];
            if (!seen.Contains(enemy.Name))
                seen.Add(enemy.Name);

            return;
        }
    }

    /// <summary>What was last seen for this enemy, or null if it has never been hovered.</summary>
    public BattleMonsterReader.Enemy? Get(string name) =>
        byName.TryGetValue(name, out var enemy) ? enemy : null;

    public void Clear()
    {
        byName.Clear();
        byRoom.Clear();
    }

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        if (BattleMonsterReader.Read() is not { } enemy)
            return;

        // Replaced rather than kept: the nullification note reflects the team you have right now,
        // so the newest reading is the true one.
        byName[enemy.Name] = enemy;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
