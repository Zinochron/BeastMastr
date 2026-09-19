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

    /// <summary>
    /// Before the pull the arena holds this fight alone, and the run is put about 26 yalms from the
    /// enemy — just out of <see cref="TargetSearchRange"/>, which is why later fights never started.
    /// </summary>
    private const float PullSearchRange = 45f;

    /// <summary>After an enemy cast with a shape ends, BossMod keeps the moving this long before walking in again.</summary>
    private static readonly TimeSpan DodgeSettles = TimeSpan.FromSeconds(1.5);

    /// <summary>A cast is only started once you have stood still this long; moving breaks it.</summary>
    private static readonly TimeSpan StillFor = TimeSpan.FromMilliseconds(300);
    private const float MeleeReach = 2.5f;

    private const byte EnemySubKind = (byte)BattleNpcSubKind.Combatant;

    private readonly Configuration configuration;
    private readonly BeastmasterJob job;
    private readonly ManualInputGuard input;
    private readonly BossModBridge bossMod;
    private readonly ActionWatcher actions;

    /// <summary>When Parting Blow and a Battlehorn last went off — pressed by anyone.</summary>
    private DateTime lastPartingBlow = DateTime.MinValue;

    private DateTime lastBattlehorn = DateTime.MinValue;

    /// <summary>How long a familiar sent off still counts as leaving, if no new one is summoned first.</summary>
    private static readonly TimeSpan LeavingFor = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long after a Battlehorn its familiar counts as there before it shows up: the cast ends about
    /// half a second before the familiar appears, and in that gap the opener would summon again
    /// instead of borrowing.
    /// </summary>
    private static readonly TimeSpan ArrivingFor = TimeSpan.FromSeconds(2.5);

    /// <summary>Battlehorns used since this fight began.</summary>
    private int hornsThisFight;

    private DateTime nextAttempt;
    private DateTime nextRangeCheck;
    private DateTime lastInCombat;
    private bool approaching;
    private bool pausedBossMod;
    private Vector3 lastPosition;
    private DateTime lastMovedAt;
    private DateTime lastDodgeAt;

    /// <summary>How often the dodge is planned: a grid over the arena against every hit under way.</summary>
    private static readonly TimeSpan DodgePlanInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>A new order to walk only when the spot moved this far, or this long after the last.</summary>
    private const float DodgeRegoal = 1f;

    private static readonly TimeSpan DashAfterDodge = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan DodgeReorder = TimeSpan.FromMilliseconds(500);

    /// <summary>Off an arena, BeastMastr's own dodging stays this close to where it started.</summary>
    private const float OffArenaRadius = 25f;

    private DodgePlan? dodge;

    /// <summary>How many hits and patches the last dodge plan saw.</summary>
    private int zonesAround;

    /// <summary>The last plan's zones and arena, for walking round the patches on the way.</summary>
    private List<Zone> lastZones = [];
    private Vector2 lastArenaCentre;
    private float lastArenaRadius;
    private bool lastArenaSquare;
    private DateTime nextDodgePlan;
    private Vector2 dodgeGoal;
    private DateTime nextDodgeOrder;
    private string lastDodgeWhy = string.Empty;

    public CombatDriver(Configuration configuration, BeastmasterJob job, ManualInputGuard input, BossModBridge bossMod,
                        ActionWatcher actions)
    {
        this.configuration = configuration;
        this.job = job;
        this.input = input;
        this.bossMod = bossMod;
        this.actions = actions;
        actions.ActionUsed += OnActionUsed;
        Services.Framework.Update += OnUpdate;
    }

    private void OnActionUsed(ActionWatcher.Use use)
    {
        if (!use.Accepted || use.Type != ActionType.Action)
            return;

        if (use.ActionId == Bst.PartingBlow)
        {
            lastPartingBlow = use.At;
        }
        else if (Array.IndexOf(Bst.Battlehorns, use.ActionId) >= 0 && use.At - lastBattlehorn > TimeSpan.FromSeconds(1))
        {
            // The game reports a cast twice — pressed, then queued — so a second report within the cast is the
            // same horn. It is only counted once its familiar is there: at Borgny on 2026-09-19 16:17 the horn
            // pressed as the opening cutscene ended was cut off after 0.09 s, nobody came, and the opener went
            // on as if one had.
            lastBattlehorn = use.At;
            hornPending = true;
            summonedAtHorn = GaugeReader.Read()?.SummonedBeast ?? 0;
            familiarsAtHorn = Services.Objects.LocalPlayer is { } player ? Familiars(player).Count() : 0;
        }
    }

    /// <summary>How many familiars stood out as the pending horn was pressed.</summary>
    private int familiarsAtHorn;

    /// <summary>Since when enough adds have been on the player, or null.</summary>
    private DateTime? addsSince;

    /// <summary>A Battlehorn pressed whose familiar has not shown yet.</summary>
    private bool hornPending;

    /// <summary>The gauge's summon count as the pending horn was pressed.</summary>
    private int summonedAtHorn;

    /// <summary>The last time a cutscene or event held the character; nothing is pressed until a moment after.</summary>
    private DateTime lastHeld = DateTime.MinValue;

    private static readonly TimeSpan AfterHeld = TimeSpan.FromSeconds(1);

    /// <summary>Counts a horn once its familiar is there, and forgets one that was cut off.</summary>
    private void ConfirmHorn(IPlayerCharacter player)
    {
        if (!hornPending)
            return;

        if ((GaugeReader.Read()?.SummonedBeast ?? 0) != summonedAtHorn || Familiars(player).Count() > familiarsAtHorn)
        {
            hornPending = false;
            hornsThisFight++;
            return;
        }

        if (player.IsCasting || DateTime.Now - lastBattlehorn < ArrivingFor)
            return;

        hornPending = false;
        lastBattlehorn = DateTime.MinValue;
        Services.Log.Information("A Battlehorn summoned nobody (its cast was cut off); it is pressed again.");
    }

    /// <summary>Parting Blow went off after the last summon, and not long ago.</summary>
    private bool FamiliarLeaving =>
        lastPartingBlow > lastBattlehorn && DateTime.Now - lastPartingBlow < LeavingFor;

    public bool Enabled { get; private set; }

    /// <summary>Whether it may pick a fight itself. Only the run grants that, once it has started one.</summary>
    public bool MayPull { get; set; }

    /// <summary>The fight in hand is a board's final one: every useful item may go, once the horns are out.</summary>
    public bool BossFight { get; set; }

    /// <summary>The final fight's items used or tried, each buff once.</summary>
    private readonly HashSet<uint> bossItemsTried = [];

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
        if (!Enabled)
            hornsThisFight = FamiliarOut(Services.Objects.LocalPlayer) ? 1 : 0;

        hornPending = false;
        bossItemsTried.Clear();

        Enabled = true;
        input.Reset();
        Status = "On — waiting for a fight.";
    }

    public void Stop(string reason)
    {
        if (approaching)
            NavmeshIpc.Stop();

        approaching = false;
        dodge = null;
        lastDodgeWhy = string.Empty;
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

        if (Vector3.DistanceSquared(player.Position, lastPosition) > 0.0001f)
        {
            lastPosition = player.Position;
            lastMovedAt = DateTime.Now;
        }

        ConfirmHorn(player);

        // Nothing is pressed during a cutscene or event, nor for a moment after: a Battlehorn pressed the
        // instant Borgny's opening cutscene let go was cut off.
        if (Services.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Services.Condition[ConditionFlag.Occupied33] ||
            Services.Condition[ConditionFlag.WatchingCutscene])
            lastHeld = DateTime.Now;

        if (DateTime.Now - lastHeld < AfterHeld)
        {
            Status = "Waiting for the cutscene to let go.";
            return;
        }

        if (input.Holding)
        {
            Hold(player);
            return;
        }

        // Items are refused during an action's lock: a Fang thrown a tenth of a second after Shieldsplitter
        // was not used.
        var itemsFree = ItemUser.Busy || (!player.IsCasting && ActionManager.Instance() is var locks && locks != null &&
                                          locks->AnimationLock <= 0f);

        // HP does not come back on its own in the Crucible.
        if (configuration.UsePotions && inCombat && itemsFree && ItemUser.Tick(configuration.PotionInFightBelow, true))
        {
            Status = "Drinking.";
            return;
        }

        // Adds on the player — the Treant's Slug Pieces — get an area item thrown at them, once they have been
        // on the player a moment: the rest of the wave is still arriving, and one throw should catch them all.
        var adds = configuration.UseAreaItems && inCombat ? AddsOnPlayer(player) : null;
        if (adds == null)
            addsSince = null;
        else
            addsSince ??= DateTime.Now;

        var addsWaited = DateTime.Now - addsSince >= TimeSpan.FromSeconds(configuration.AreaItemWaitSeconds);
        if (adds != null && itemsFree && (ItemUser.Busy || addsWaited) && ItemUser.TickAttack(adds))
        {
            Status = "Throwing an area item at the adds.";
            return;
        }

        // The final fight: every item worth using — the Beast Potion Kit first — once the horns are out, so
        // nothing pulls before them (the user). Each buff once; the antidote whenever Borgny has poisoned.
        if (BossFight && configuration.UseBossItems && itemsFree && hornsThisFight >= 2 && !hornPending)
        {
            var statuses = player.StatusList.Where(status => status.StatusId != 0)
                                 .Select(status => status.StatusId).ToHashSet();
            var held = ItemUser.HeldItems().Select(item => item.Row).ToList();
            if (BossItems.Next(held, bossItemsTried, statuses) is { } row && ItemUser.TickUse(row))
            {
                if (row != BossItems.Antidote)
                    bossItemsTried.Add(row);

                Status = "Using an item for the final fight.";
                return;
            }

            // The Fangs and Celestial Sand on the boss itself, once the fight is on.
            if (inCombat && Services.Targets.Target is IBattleChara { IsDead: false } boss && Hostile(boss) &&
                ItemUser.TickAttack(boss))
            {
                Status = "Throwing an area item at the boss.";
                return;
            }
        }

        if (pausedBossMod)
        {
            pausedBossMod = false;
            bossMod.Engage(player.Position);
        }

        if (inCombat)
        {
            bossMod.Engage(player.Position);
        }
        else if (DateTime.Now - lastInCombat > CombatEndGrace && lastInCombat != DateTime.MinValue)
        {
            // A fight has ended: the next one opens again from the first horn.
            if (bossMod.Engaged)
                bossMod.Disengage();

            if (!MayPull)
            {
                hornsThisFight = 0;
                hornPending = false;
                bossItemsTried.Clear();
            }
        }

        var target = Target(player, inCombat);
        if (target == null)
        {
            // Borgny cannot be targeted while it charges with Cauterize, and on 2026-09-17 19:30 nothing
            // else was up: the charge went undodged, straight through the player. It is dodged anyway.
            if (inCombat && OwnDodging)
            {
                if (DateTime.Now >= nextDodgePlan)
                {
                    nextDodgePlan = DateTime.Now + DodgePlanInterval;
                    dodge = PlanDodge(player, null);
                }

                if (dodge != null)
                {
                    lastDodgeAt = DateTime.Now;
                    FollowDodge(player, dodge);
                    Status = "In combat with nothing to hit; dodging.";
                    return;
                }
            }

            StopApproaching();
            Status = inCombat ? "In combat, but nothing to hit is in reach." : "On — waiting for a fight.";
            return;
        }

        var distance = MathF.Max(0f, Vector3.Distance(player.Position, target.Position) - target.HitboxRadius -
                                     player.HitboxRadius);

        var manager = ActionManager.Instance();
        if (manager == null)
            return;

        var state = Snapshot(manager, player, target, distance, inCombat);
        var decision = BstRotation.Next(state, Options());
        LastDecision = $"{Name(decision.Gcd)} / {Name(decision.Ogcd)} — {decision.Why}";
        Status = $"Fighting {target.Name.TextValue} at {distance:0.0} y. TP {state.PlayerTp}, familiar TP {state.FamiliarTp}." +
                 (decision.Engage ? string.Empty : " Setting up the opener.");

        // Moving breaks a cast, and walking in before the opener is set up starts the fight early.
        // BossMod never closes in for a Beastmaster — it takes the job for a ranged one — so while no
        // enemy casts anything to dodge, its moving is held back and vnavmesh walks in; the moment a
        // cast with a shape starts, BossMod is let go again.
        //
        // With BossMod off, BeastMastr dodges itself — see Rules/Dodger.cs — and walks in only once
        // nothing is being dodged.
        var ownDodging = OwnDodging;
        if (ownDodging && DateTime.Now >= nextDodgePlan)
        {
            nextDodgePlan = DateTime.Now + DodgePlanInterval;
            // Before the pull, with the opener not done, nothing is walked in to: at the Treant the Sludge made a
            // "closing in" plan the moment the arena loaded, and the Treant was pulled before a single horn.
            dodge = PlanDodge(player, decision.Engage ? target : null);
        }
        else if (!ownDodging)
        {
            dodge = null;
        }

        if (dodge != null || (!ownDodging && EnemyCasts.Dodging(player)))
            lastDodgeAt = DateTime.Now;

        if (dodge != null)
        {
            FollowDodge(player, dodge);
        }
        else
        {
            var casting = player.IsCasting || job.IsCast(decision.Ogcd);
            var walkIn = configuration.KeepRangeWithNavmesh && decision.Engage && !casting &&
                         distance > MeleeReach && DateTime.Now - lastDodgeAt > DodgeSettles;
            bossMod.HoldMovement(walkIn);

            if (walkIn && !bossMod.Moves)
                KeepInReach(target, distance);
            else
                StopApproaching();
        }

        if (manager->AnimationLock > 0f || player.IsCasting || DateTime.Now < nextAttempt)
            return;

        if (decision.Ogcd != 0 && Use(manager, player, target, decision.Ogcd))
        {
            // The familiars' comings and goings are logged too, so an opener gone wrong can be read back
            // without a recording.
            if (decision.Ogcd is Bst.Snarl or Bst.Challenge or Bst.Borrow or Bst.PartingBlow ||
                Array.IndexOf(Bst.Battlehorns, decision.Ogcd) >= 0)
                Services.Log.Information($"Pressed {Name(decision.Ogcd)}: {decision.Why}" +
                                         $"{(MayPull && !inCombat ? " (before the pull)" : string.Empty)}.");

            return;
        }

        if (decision.Gcd != 0)
            Use(manager, player, target, decision.Gcd);
    }

    /// <summary>You have taken over: nothing is pressed and nothing is moved until you let go.</summary>
    private void Hold(IGameObject player)
    {
        StopApproaching();
        bossMod.HoldMovement(false);

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
                              UseShieldCharge: configuration.UseShieldCharge,
                              PartingBlowHornWithin: configuration.PartingBlowHornWithin,
                              PartingBlowFinisherShare: configuration.PartingBlowFinisherShare,
                              DutyActions: configuration.UseDutyActions,
                              Tank: configuration.DutyTank,
                              PlayerLowShare: configuration.SnarlBelowPlayerHp,
                              FamiliarLowShare: configuration.ChallengeBelowFamiliarHp);
    }

    private BstState Snapshot(ActionManager* manager, IPlayerCharacter player, IBattleChara target, float distance,
                              bool inCombat)
    {
        var gauge = GaugeReader.Read();
        var statuses = player.StatusList.Where(status => status.StatusId != 0).Select(status => status.StatusId)
                             .ToHashSet();
        var familiars = Familiars(player).ToList();
        var summoned = familiars.FirstOrDefault();

        return new BstState(
            Level: player.Level,
            ComboAction: manager->Combo.Action,
            ComboTimer: manager->Combo.Timer,
            PlayerTp: gauge?.PlayerTp ?? 0,
            FamiliarTp: gauge?.PetTp ?? 0,
            Statuses: statuses,
            Ready: id => Ready(manager, player, target, id),
            InCombat: inCombat || MayPull,
            HasTarget: true,
            TargetDistance: distance,
            TargetCasting: target.IsCasting && target.IsCastInterruptible,
            FamiliarOut: FamiliarOut(player) || FamiliarArriving,
            FamiliarLeaving: FamiliarLeaving,
            PrePull: MayPull && !inCombat,
            HornsThisFight: hornsThisFight,
            OtherHornReadyIn: OtherHornReadyIn(manager, player),
            TargetHpShare: Share(target),
            PlayerHpShare: Share(player),
            FamiliarHpShare: summoned == null ? 1f : Share(summoned),
            TargetOnFamiliar: familiars.Any(familiar => familiar.GameObjectId == target.TargetObjectId),
            UnavoidableHit: configuration.UseDutyActions ? EnemyCasts.Unavoidable(player) : null,
            // Not straight after a dodge either: Toxic Vomit's cast ends 2.3 s before it lands, and Shield
            // Charge in between carried the player back to Borgny.
            MayDash: dodge == null && zonesAround == 0 && DateTime.Now - lastDodgeAt > DashAfterDodge,
            KeepLastPartingBlow: target is IBattleChara { BaseId: Bosses.Borgny } &&
                                 hornsThisFight >= Bosses.BorgnyKeepsBlowFromHorn);
    }

    private static float Share(IBattleChara chara) => chara.MaxHp > 0 ? (float)chara.CurrentHp / chara.MaxHp : 1f;

    private bool FamiliarArriving =>
        lastBattlehorn > lastPartingBlow && DateTime.Now - lastBattlehorn < ArrivingFor;

    /// <summary>
    /// Seconds until a Battlehorn other than the one whose familiar is out can be used. A horn's recast
    /// only starts once its familiar has retreated, so one that is neither usable nor counting down is
    /// not coming back soon.
    /// </summary>
    private float OtherHornReadyIn(ActionManager* manager, IPlayerCharacter player)
    {
        var summoned = GaugeReader.Read()?.SummonedBeast ?? 0;
        var best = float.PositiveInfinity;

        for (var slot = 0; slot < Bst.Battlehorns.Length; slot++)
        {
            var horn = Bst.Battlehorns[slot];
            if (slot + 1 == summoned || player.Level < Bst.Levels[horn])
                continue;

            if (manager->GetActionStatus(ActionType.Action, horn, player.GameObjectId, true, false) == 0)
                return 0f;

            if (manager->IsRecastTimerActive(ActionType.Action, horn))
            {
                var left = manager->GetRecastTime(ActionType.Action, horn) -
                           manager->GetRecastTimeElapsed(ActionType.Action, horn);
                best = MathF.Min(best, MathF.Max(0f, left));
            }
        }

        return best;
    }

    /// <summary>
    /// Whether an action could be used — ignoring a cast under way, so that what comes after a
    /// Battlehorn's cast is already decided during it. <see cref="Use"/> still waits for the cast.
    /// </summary>
    private bool Ready(ActionManager* manager, IGameObject player, IGameObject target, uint id)
    {
        var adjusted = manager->GetAdjustedActionId(id);
        return manager->GetActionStatus(ActionType.Action, adjusted, TargetFor(adjusted, player, target), true,
                                        false) == 0;
    }

    /// <summary>
    /// One of the adds attacking the player or a familiar within reach, when there are at least as many as
    /// the setting asks; null otherwise. The strongest enemy — the boss — is not an add.
    /// </summary>
    private IBattleChara? AddsOnPlayer(IPlayerCharacter player)
    {
        var enemies = Services.Objects.OfType<IBattleChara>().Where(Hostile).ToList();
        if (enemies.Count < 2)
            return null;

        var boss = enemies.MaxBy(enemy => enemy.MaxHp);
        var ours = Familiars(player).Select(familiar => familiar.GameObjectId).Append(player.GameObjectId).ToHashSet();
        var adds = enemies.Where(enemy => enemy != boss && ours.Contains(enemy.TargetObjectId) &&
                                          Vector3.Distance(enemy.Position, player.Position) <= AddsWithin)
                          .ToList();

        return adds.Count >= configuration.AreaItemAtAdds ? adds[0] : null;
    }

    /// <summary>How close the adds have to be: the Fangs hit 12 yalms around their target.</summary>
    private const float AddsWithin = 8f;

    private bool OwnDodging => bossMod.Role == BossModRole.Off && configuration.DodgeWithBeastMastr;

    private DodgePlan? PlanDodge(IPlayerCharacter player, IGameObject? target)
    {
        var here = new Vector2(player.Position.X, player.Position.Z);
        var arena = CrucibleArena.CentreNear(here);
        var zones = EnemyCasts.Zones(player);
        zonesAround = zones.Count;
        lastZones = zones;
        if (arena == null && zones.Count == 0)
            return null;

        var square = arena is { } middle && CrucibleArena.IsSquare(middle);
        lastArenaCentre = arena ?? here;
        lastArenaSquare = square;
        lastArenaRadius = arena == null ? OffArenaRadius
                          : square ? CrucibleArena.SquareSafeHalfWidth
                          : Math.Clamp(configuration.ArenaSafeRadius, 5f, 20f);

        return Dodger.Plan(here, target == null ? null : new Vector2(target.Position.X, target.Position.Z),
                           MeleeReach + (target?.HitboxRadius ?? 0f), zones, lastArenaCentre, lastArenaRadius, square);
    }

    /// <summary>Walks to the dodge's spot in a straight line — the arenas are flat — or stands on it.</summary>
    private void FollowDodge(IGameObject player, DodgePlan plan)
    {
        if (plan.Why != lastDodgeWhy)
        {
            lastDodgeWhy = plan.Why;
            Services.Log.Information($"Dodging {plan.Why}: to ({plan.Point.X:0.0}, {plan.Point.Y:0.0})" +
                                     (plan.Safe ? "." : "; nowhere is clear, so the least bad spot."));
        }

        var here = new Vector2(player.Position.X, player.Position.Z);
        if (Vector2.Distance(here, plan.Point) <= 0.5f)
        {
            StopApproaching();
            dodgeGoal = plan.Point;
            return;
        }

        if (approaching && Vector2.Distance(plan.Point, dodgeGoal) < DodgeRegoal && DateTime.Now < nextDodgeOrder)
            return;

        dodgeGoal = plan.Point;
        nextDodgeOrder = DateTime.Now + DodgeReorder;

        // Round the patches on the ground, not through them.
        var route = Dodger.Route(here, plan.Point, lastZones, lastArenaCentre, lastArenaRadius, lastArenaSquare);
        if (NavmeshIpc.MoveTo(route.Select(point => new Vector3(point.X, player.Position.Y, point.Y)).ToList()))
            approaching = true;
    }

    private bool Use(ActionManager* manager, IGameObject player, IGameObject target, uint id)
    {
        nextAttempt = DateTime.Now + AttemptInterval;

        var adjusted = manager->GetAdjustedActionId(id);
        // Moving breaks a cast; so does a dodge that has somewhere to go.
        if (job.IsCast(adjusted) && (DateTime.Now - lastMovedAt < StillFor || dodge is { } plan &&
                                     Vector2.Distance(plan.Point, new Vector2(player.Position.X, player.Position.Z)) > 0.5f))
            return false;

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

        var range = MayPull ? PullSearchRange : TargetSearchRange;
        var nearest = Services.Objects.OfType<IBattleChara>()
                              .Where(Hostile)
                              .Select(enemy => (enemy, distance: Vector3.Distance(enemy.Position, player.Position)))
                              .Where(pair => pair.distance <= range)
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

    /// <summary>
    /// A familiar is a battle NPC of the pet kind — "Opo-opo", "Squirrel", "Cu Sith" in the recording —
    /// and one that has retreated lingers as dead for a moment, so the dead are not counted.
    /// </summary>
    private static bool FamiliarOut(IGameObject? player) => player != null && Familiars(player).Any();

    private static IEnumerable<IBattleChara> Familiars(IGameObject player) =>
        Services.Objects.OfType<IBattleChara>()
                .Where(obj => obj.ObjectKind == ObjectKind.BattleNpc && obj.SubKind == (byte)BattleNpcSubKind.Pet &&
                              obj.OwnerId == player.EntityId && !obj.IsDead);

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

        // To the edge of the target's hitbox, not its middle: the Treant keeps Sludge on the ground 8
        // yalms around its middle, and walking to 2.5 of it stood in that.
        // And never into a patch that lies under the target: the Treant's Sludge reaches past 8.5 yalms.
        var stopAt = MathF.Max(target.HitboxRadius + MeleeReach - 0.5f, EnemyCasts.HazardAround(target.Position) + 0.7f);
        if (NavmeshIpc.PathfindAndMoveCloseTo(target.Position, stopAt))
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
        actions.ActionUsed -= OnActionUsed;
        Services.Framework.Update -= OnUpdate;
        Stop("Unloaded.");
    }
}
