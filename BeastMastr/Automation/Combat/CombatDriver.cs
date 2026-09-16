using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Automation.Run;
using BeastMastr.Data;
using BeastMastr.Ipc;
using BeastMastr.Rules;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BeastMastr.Automation.Combat;

/// <summary>
/// Plays a Beastmaster's fight: picks a target, keeps in reach of it, and presses what
/// <see cref="BstRotation"/> says — one action at a time, only when the game says it can be used.
///
/// It only runs while switched on — by the run, or by <c>/beastmastr combat</c> — and it only pulls
/// something itself when the run has started the fight; switched on by hand it waits for a fight to
/// be under way. Your own input pauses it like it pauses walking, and BossMod is handed the moving
/// (and the combo, if that is the setting) for as long as the fight lasts.
/// </summary>
public sealed unsafe class CombatDriver : IDisposable
{
    /// <summary>Between two attempts to use something, so a refused action is not hammered every frame.</summary>
    private static readonly TimeSpan AttemptInterval = TimeSpan.FromMilliseconds(150);

    private static readonly TimeSpan RangeInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long out of combat before the fight counts as over and BossMod is handed back.</summary>
    private static readonly TimeSpan CombatEndGrace = TimeSpan.FromSeconds(3);

    private const float TargetSearchRange = 25f;
    private const float MeleeReach = 2.5f;

    private const byte EnemySubKind = (byte)BattleNpcSubKind.Combatant;

    private readonly Configuration configuration;
    private readonly BeastmasterJob job;
    private readonly ManualInputGuard input;
    private readonly BossModBridge bossMod;

    private DateTime nextAttempt;
    private DateTime nextRangeCheck;
    private DateTime lastInCombat;
    private bool approaching;
    private bool pausedBossMod;

    public CombatDriver(Configuration configuration, BeastmasterJob job, ManualInputGuard input, BossModBridge bossMod)
    {
        this.configuration = configuration;
        this.job = job;
        this.input = input;
        this.bossMod = bossMod;
        Services.Framework.Update += OnUpdate;
    }

    public bool Enabled { get; private set; }

    /// <summary>Whether it may pick a fight itself. Only the run grants that, once it has started one.</summary>
    public bool MayPull { get; set; }

    public string Status { get; private set; } = "Off.";

    public string LastDecision { get; private set; } = string.Empty;

    /// <summary>The last thing pressed, and when.</summary>
    public string LastUsed { get; private set; } = string.Empty;

    public void Toggle()
    {
        if (Enabled)
            Stop("Switched off.");
        else
            Start();
    }

    public void Start()
    {
        Enabled = true;
        input.Reset();
        Status = "On — waiting for a fight.";
    }

    public void Stop(string reason)
    {
        if (approaching)
            NavmeshIpc.Stop();

        approaching = false;
        Enabled = false;
        MayPull = false;
        pausedBossMod = false;
        bossMod.Disengage();
        Status = reason;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!Enabled)
            return;

        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Fighting failed.");
            Stop($"Fighting failed: {ex.Message}");
        }
    }

    private void Tick()
    {
        if (Services.Objects.LocalPlayer is not { } player || !GaugeReader.IsBeastmaster)
        {
            Status = "Waiting: not on Beastmaster.";
            return;
        }

        if (Services.Condition[ConditionFlag.BetweenAreas] || Services.Condition[ConditionFlag.Unconscious])
        {
            Status = "Waiting: between areas, or down.";
            return;
        }

        var inCombat = Services.Condition[ConditionFlag.InCombat];
        if (inCombat)
            lastInCombat = DateTime.Now;

        if (input.Holding)
        {
            Hold(player);
            return;
        }

        if (pausedBossMod)
        {
            pausedBossMod = false;
            bossMod.Engage(player.Position);
        }

        if (inCombat)
            bossMod.Engage(player.Position);
        else if (bossMod.Engaged && DateTime.Now - lastInCombat > CombatEndGrace)
            bossMod.Disengage();

        var target = Target(player, inCombat);
        if (target == null)
        {
            StopApproaching();
            Status = inCombat ? "In combat, but nothing to hit is in reach." : "On — waiting for a fight.";
            return;
        }

        var distance = MathF.Max(0f, Vector3.Distance(player.Position, target.Position) - target.HitboxRadius -
                                     player.HitboxRadius);

        if (!bossMod.Moves && configuration.KeepRangeWithNavmesh)
            KeepInReach(target, distance);

        var manager = ActionManager.Instance();
        if (manager == null)
            return;

        var state = Snapshot(manager, player, target, distance, inCombat);
        var decision = BstRotation.Next(state, Options());
        LastDecision = $"{Name(decision.Gcd)} / {Name(decision.Ogcd)} — {decision.Why}";
        Status = $"Fighting {target.Name.TextValue} at {distance:0.0} y. TP {state.PlayerTp}, familiar TP {state.FamiliarTp}.";

        if (manager->AnimationLock > 0f || player.IsCasting || DateTime.Now < nextAttempt)
            return;

        if (decision.Ogcd != 0 && Use(manager, player, target, decision.Ogcd))
            return;

        if (decision.Gcd != 0)
            Use(manager, player, target, decision.Gcd);
    }

    /// <summary>You have taken over: nothing is pressed and nothing is moved until you let go.</summary>
    private void Hold(IGameObject player)
    {
        StopApproaching();

        if (configuration.AbortOnManualInput)
        {
            Stop($"You took over ({input.LastInput}); fighting is off.");
            return;
        }

        if (!configuration.KeepBossModWhilePaused && bossMod.Engaged)
        {
            bossMod.Disengage();
            pausedBossMod = true;
        }

        Status = $"Paused — you took over ({input.LastInput}).";
    }

    private BstOptions Options()
    {
        var bossModCombo = bossMod.PlaysCombo;
        return new BstOptions(Combo: !bossModCombo,
                              Resources: !bossModCombo || configuration.BeastMastrHandlesResources,
                              SpendTpAt: configuration.SpendTpAt,
                              UseBattlehorns: configuration.UseBattlehorns,
                              UsePartingBlow: configuration.UsePartingBlow,
                              UseShieldCharge: configuration.UseShieldCharge);
    }

    private BstState Snapshot(ActionManager* manager, IPlayerCharacter player, IBattleChara target, float distance,
                              bool inCombat)
    {
        var gauge = GaugeReader.Read();
        var statuses = player.StatusList.Where(status => status.StatusId != 0).Select(status => status.StatusId)
                             .ToHashSet();

        return new BstState(
            Level: player.Level,
            ComboAction: manager->Combo.Action,
            ComboTimer: manager->Combo.Timer,
            PlayerTp: gauge?.PlayerTp ?? 0,
            FamiliarTp: gauge?.PetTp ?? 0,
            Statuses: statuses,
            Ready: id => Ready(manager, player, target, id),
            InCombat: inCombat,
            HasTarget: true,
            TargetDistance: distance,
            TargetCasting: target.IsCasting && target.IsCastInterruptible,
            FamiliarOut: FamiliarOut(player));
    }

    private bool Ready(ActionManager* manager, IGameObject player, IGameObject target, uint id)
    {
        var adjusted = manager->GetAdjustedActionId(id);
        return manager->GetActionStatus(ActionType.Action, adjusted, TargetFor(adjusted, player, target)) == 0;
    }

    private bool Use(ActionManager* manager, IGameObject player, IGameObject target, uint id)
    {
        nextAttempt = DateTime.Now + AttemptInterval;

        var adjusted = manager->GetAdjustedActionId(id);
        var targetId = TargetFor(adjusted, player, target);
        if (manager->GetActionStatus(ActionType.Action, adjusted, targetId) != 0)
            return false;

        var used = manager->UseAction(ActionType.Action, adjusted, targetId);
        if (used)
            LastUsed = $"{DateTime.Now:HH:mm:ss} {Name(adjusted)}";

        return used;
    }

    private ulong TargetFor(uint action, IGameObject player, IGameObject target) =>
        job.TargetsEnemy(action) ? target.GameObjectId : player.GameObjectId;

    private string Name(uint id) => id == 0 ? "-" : job.Name(id);

    /// <summary>
    /// The target to fight: the current one if it is a living enemy, else the nearest living enemy — but
    /// a new one only while a fight is on or the run has started one.
    /// </summary>
    private IBattleChara? Target(IGameObject player, bool inCombat)
    {
        if (Services.Targets.Target is IBattleChara current && Hostile(current))
            return current;

        if (!inCombat && !MayPull)
            return null;

        var nearest = Services.Objects.OfType<IBattleChara>()
                              .Where(Hostile)
                              .Select(enemy => (enemy, distance: Vector3.Distance(enemy.Position, player.Position)))
                              .Where(pair => pair.distance <= TargetSearchRange)
                              .OrderBy(pair => pair.distance)
                              .Select(pair => pair.enemy)
                              .FirstOrDefault();

        if (nearest != null)
            Services.Targets.Target = nearest;

        return nearest;
    }

    private static bool Hostile(IBattleChara chara) =>
        chara.ObjectKind == ObjectKind.BattleNpc && chara.SubKind == EnemySubKind && chara.IsTargetable &&
        !chara.IsDead && chara.CurrentHp > 0;

    private static bool FamiliarOut(IGameObject player) =>
        Services.Objects.Any(obj => obj.ObjectKind == ObjectKind.BattleNpc && obj.OwnerId == player.EntityId &&
                                    !obj.IsDead);

    private void KeepInReach(IGameObject target, float distance)
    {
        if (DateTime.Now < nextRangeCheck)
            return;

        nextRangeCheck = DateTime.Now + RangeInterval;

        if (distance <= MeleeReach)
        {
            StopApproaching();
            return;
        }

        if (NavmeshIpc.PathfindAndMoveCloseTo(target.Position, MeleeReach))
            approaching = true;
    }

    private void StopApproaching()
    {
        if (!approaching)
            return;

        NavmeshIpc.Stop();
        approaching = false;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Stop("Unloaded.");
    }
}
