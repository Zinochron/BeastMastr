using System;
using System.Collections.Generic;
using System.Linq;
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
/// In memory only. A run's enemies mean nothing after it ends.
/// </summary>
public sealed class EnemyCache : IDisposable
{
    /// <summary>Frames between samples. The panel changes when the cursor moves, not per frame.</summary>
    private const int Interval = 10;

    private readonly Dictionary<string, BattleMonsterReader.Enemy> byName = new(StringComparer.Ordinal);
    private int ticks;

    public EnemyCache()
    {
        Services.Framework.Update += OnUpdate;
    }

    public int Count => byName.Count;

    public IReadOnlyCollection<BattleMonsterReader.Enemy> All => byName.Values;

    /// <summary>What was last seen for this enemy, or null if it has never been hovered.</summary>
    public BattleMonsterReader.Enemy? Get(string name) =>
        byName.TryGetValue(name, out var enemy) ? enemy : null;

    public void Clear() => byName.Clear();

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
