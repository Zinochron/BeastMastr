using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Automation.Combat;
using BeastMastr.Data;
using BeastMastr.Ipc;
using BeastMastr.Rules;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace BeastMastr.Automation.Run;

/// <summary>
/// Plays a board: pick the next room, walk there, do what the room asks, repeat until the boss is done.
///
/// It is started by <c>/beastmastr run</c> and by nothing else. It works out where it stands every step
/// rather than trusting where it thinks it is, so a pause — yours or its own — can be picked up from.
/// Walking and fighting let go of the character whenever you take over, and carry on after.
///
/// **Nothing is guessed.** A room step whose button has not been recorded yet (see
/// <see cref="RoomActions"/>) is handed to you: the run says what to press, in chat and in the Run tab,
/// and carries on the moment the game shows it was done. If a room does something the run does not
/// recognise, it says so and waits — the Continue button tells it the room is finished.
/// </summary>
public sealed class BoardRunner : IDisposable
{
    public enum Phase
    {
        Idle,
        Preflight,
        Scanning,
        Deciding,
        Walking,
        Entering,
        CallingFamiliars,
        Commencing,
        Fighting,
        AfterFight,
        Campsite,
        Shop,
        Treasure,
        Settling,
        Finishing,
        Done,
        Failed,
    }

    private static readonly TimeSpan EnterTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FamiliarWait = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CommenceTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TreasureWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BackOnBoardWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FightEndGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LongestFight = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SpoilsWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ResultWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PlacementWait = TimeSpan.FromSeconds(10);
    private const float FightSearchRange = 45f;

    /// <summary>How long a revive is waited for after going down.</summary>
    private static readonly TimeSpan ReviveWait = TimeSpan.FromSeconds(10);

    /// <summary>How long a room's leftover "In Event" is waited out before walking on anyway.</summary>
    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(15);

    private DateTime? downSince;
    private const int LogLength = 30;

    private readonly Configuration configuration;
    private readonly BoardModel board;
    private readonly BoardTerrain terrain;
    private readonly RouteKeeper route;
    private readonly BoardWalker walker;
    private readonly CombatDriver combat;
    private readonly FightSelector fightSelector;
    private readonly HealthSelector healthSelector;
    private readonly BeastCatalog catalog;

    private readonly List<string> log = [];

    /// <summary>How often the same room has been gone back into after it seemed done.</summary>
    private int reentries;

    private DateTime phaseSince;
    private DateTime lastInCombat;
    private bool acted;
    private bool paused;
    private bool continueRequested;

    /// <summary>The recorded command the current phase is carrying through, if any.</summary>
    private ConfirmedStep? step;

    private int treasureChoice;

    /// <summary>Whether the fight in hand has been seen in combat, and away from the board.</summary>
    private bool sawCombat;

    private bool leftBoard;

    public BoardRunner(Configuration configuration, BoardModel board, BoardTerrain terrain, RouteKeeper route,
                       BoardWalker walker, CombatDriver combat, FightSelector fightSelector,
                       HealthSelector healthSelector, BeastCatalog catalog)
    {
        this.catalog = catalog;
        this.configuration = configuration;
        this.board = board;
        this.terrain = terrain;
        this.route = route;
        this.walker = walker;
        this.combat = combat;
        this.fightSelector = fightSelector;
        this.healthSelector = healthSelector;
        Services.Framework.Update += OnUpdate;
    }

    public Phase State { get; private set; } = Phase.Idle;

    public string Status { get; private set; } = "Not running.";

    /// <summary>What the player is asked to do right now, or empty.</summary>
    public string HandOff { get; private set; } = string.Empty;

    public bool Running => State is not (Phase.Idle or Phase.Done or Phase.Failed);

    public bool Paused => paused;

    /// <summary>The room the run has finished last. -1 until it knows.</summary>
    public int DoneEvent { get; private set; } = -1;

    /// <summary>The room being gone to or played, or -1.</summary>
    public int Target { get; private set; } = -1;

    public int RunsWanted { get; private set; }

    public int RunsDone { get; private set; }

    /// <summary>What happened, newest first.</summary>
    public IReadOnlyList<string> Log => log;

    public void Start(int runs)
    {
        if (Running)
        {
            Say("A run is already under way.");
            return;
        }

        if (RunSafety.CannotStart(board) is { } problem)
        {
            Fail(problem);
            return;
        }

        RunsWanted = Math.Max(1, runs);
        RunsDone = 0;
        DoneEvent = -1;
        Target = -1;
        paused = false;
        log.Clear();

        var collisions = RunSafety.LoadedCollisions();
        if (collisions.Count > 0)
            Say($"Also loaded, and able to get in the way: {string.Join(", ", collisions)}.");

        Enter(Phase.Preflight, $"Starting {RunsWanted} run(s) on board {board.BoardRowId}.");
    }

    public void Stop(string reason)
    {
        walker.Stop(null);
        combat.Stop(reason);

        if (!Running)
            return;

        paused = false;
        State = Phase.Idle;
        Status = reason;
        HandOff = string.Empty;
        Note(reason);
        Services.Chat.Print($"[BeastMastr] {reason}");
    }

    /// <summary>Holds everything where it is, or picks it up again.</summary>
    public void TogglePause()
    {
        if (!Running)
            return;

        paused = !paused;

        if (paused)
        {
            walker.Stop(null);
            if (combat.Enabled)
                combat.Stop("Paused with the run.");

            Status = "Paused. Press Pause again, or /beastmastr pause, to carry on.";
            Note("Paused.");
            return;
        }

        Note("Carrying on.");
        switch (State)
        {
            case Phase.Walking:
                Enter(Phase.Deciding, "Planning the walk again.");
                break;

            case Phase.Fighting:
                StartFighting();
                break;
        }
    }

    /// <summary>The player says the room in hand is finished. The way out of anything the run does not recognise.</summary>
    public void Continue()
    {
        if (Running)
            continueRequested = true;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!Running || paused)
            return;

        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "The run failed.");
            Fail($"The run failed: {ex.Message}");
        }
    }

    private void Tick()
    {
        if (RunSafety.MustStop(board, State == Phase.Finishing || IsBoss(Target) || IsBoss(DoneEvent)) is { } reason)
        {
            Fail(reason);
            return;
        }

        // Down is not over: the Ring of Sacrifice revived the player three seconds after both deaths
        // on the master board.
        if (Services.Condition[ConditionFlag.Unconscious])
        {
            downSince ??= DateTime.Now;
            if (DateTime.Now - downSince.Value > ReviveWait)
            {
                Fail("You went down.");
                return;
            }

            Status = "Down — waiting to be revived.";
            return;
        }

        if (downSince != null)
        {
            Note("Revived; carrying on.");
            downSince = null;
        }

        if (RunSafety.Waiting())
            return;

        if (Services.Condition[ConditionFlag.InCombat])
            lastInCombat = DateTime.Now;

        switch (State)
        {
            case Phase.Preflight: Preflight(); break;
            case Phase.Scanning: Scanning(); break;
            case Phase.Deciding: Deciding(); break;
            case Phase.Walking: Walking(); break;
            case Phase.Entering: Entering(); break;
            case Phase.CallingFamiliars: CallingFamiliars(); break;
            case Phase.Commencing: Commencing(); break;
            case Phase.Fighting: Fighting(); break;
            case Phase.AfterFight: AfterFight(); break;
            case Phase.Campsite: Campsite(); break;
            case Phase.Shop: Shop(); break;
            case Phase.Treasure: Treasure(); break;
            case Phase.Settling: Settling(); break;
            case Phase.Finishing: Finishing(); break;
        }

        continueRequested = false;
    }

    // ---- Phases -----------------------------------------------------------

    private void Preflight()
    {
        if (!NavmeshIpc.IsReady())
        {
            var progress = NavmeshIpc.BuildProgress();
            Status = progress >= 0 ? $"Waiting for vnavmesh to build the mesh: {progress:P0}." : "Waiting for vnavmesh.";
            return;
        }

        if (board.Join is not { IsComplete: true })
        {
            if (Elapsed > PlacementWait)
                Fail("The rooms could not be placed in the world: " + board.Status);
            else
                Status = "Placing the rooms in the world.";

            return;
        }

        // A room already in hand is finished first, whatever else is true.
        if (RoomOpened() is { } phase)
        {
            Target = board.PositionEvent >= 0 ? board.PositionEvent : board.CurrentEvent;
            Enter(phase, "Picking up the room already under way.");
            return;
        }

        if (board.CurrentEvent < 0)
        {
            Ask("Open the Board Layout once, so the run knows where it stands.");
            return;
        }

        DoneEvent = board.CurrentEvent;
        Note($"Standing at event {DoneEvent}.");

        if (!terrain.IsCurrent)
        {
            terrain.RequestScan();
            Enter(Phase.Scanning, "Scanning the ground first.");
            return;
        }

        Enter(Phase.Deciding, "Ready.");
    }

    private void Scanning()
    {
        if (terrain.Busy)
        {
            Status = terrain.Status;
            return;
        }

        if (!terrain.IsCurrent)
        {
            Fail("The ground could not be scanned: " + terrain.Status);
            return;
        }

        Enter(Phase.Deciding, terrain.Status);
    }

    private void Deciding()
    {
        reentries = 0;
        sawCombat = false;
        leftBoard = false;

        // After a fight the run is still in the arena for a moment before it loads back to the board.
        if (!board.OnBoard)
        {
            if (Elapsed > BackOnBoardWait)
                Ask("The run is not on the board. Go back to it, and the run carries on.");
            else
                Status = "Waiting to be back on the board.";

            return;
        }

        var graph = board.Graph!;
        if (graph.Node(DoneEvent) is { Kind: BoardRoomKind.Boss })
        {
            Enter(Phase.Finishing, "The boss is done.");
            return;
        }

        // Where the player stands is the room last finished — whatever the run thought. Someone may have
        // walked on by hand while it was paused or waiting.
        if (board.PositionEvent is >= 0 and var standing && standing != DoneEvent)
        {
            Note($"Standing on event {standing}, not on {DoneEvent}; going on from there.");
            DoneEvent = standing;
        }

        var plan = route.PlanFrom(DoneEvent);
        if (plan.Next < 0)
        {
            Fail("There is no way on: " + string.Join(" ", plan.Notes));
            return;
        }

        Target = plan.Next;
        if (!walker.Walk(Target, DoneEvent))
        {
            Fail(walker.Status);
            return;
        }

        var node = graph.Node(Target)!;
        Enter(Phase.Walking, $"Going to event {Target}, a {node.Kind} on move {node.Move}" +
                             (plan.ChosenByHand.Contains(Target) ? " (picked)." : "."));
    }

    private void Walking()
    {
        switch (walker.State)
        {
            case BoardWalker.Phase.Arrived:
                Enter(Phase.Entering, walker.Status);
                return;

            case BoardWalker.Phase.Failed:
                Fail(walker.Status);
                return;

            case BoardWalker.Phase.Idle:
                // Stopped from outside — by /beastmastr stop's walker half, or a pause picked up late.
                Enter(Phase.Deciding, "The walk was stopped; planning it again.");
                return;

            default:
                Status = walker.Status;
                return;
        }
    }

    private void Entering()
    {
        if (RoomOpened() is { } phase)
        {
            // The room that opened is the one stood on, even if it is not the one walked to.
            if (board.PositionEvent is >= 0 and var standing && standing != Target)
            {
                Note($"The room that opened is event {standing}, not {Target}.");
                Target = standing;
            }

            Enter(phase, "The room opened.");
            return;
        }

        if (continueRequested)
        {
            RoomDone("You said the room is finished.");
            return;
        }

        if (Elapsed > EnterTimeout)
            Ask("The room did not open anything BeastMastr knows. Do what it asks, then press Continue.");
        else
            Status = "Waiting for the room to open.";
    }

    private void CallingFamiliars()
    {
        if (!acted)
        {
            acted = true;
            if (!configuration.CallLastFamiliarsOnOpen)
                fightSelector.RequestRepeat();
        }

        if (fightSelector.IsCalling)
        {
            Status = "Calling familiars.";
            return;
        }

        if (CalledFamiliars() > 0)
        {
            Enter(Phase.Commencing, $"{CalledFamiliars()} familiar(s) called.");
            return;
        }

        if (Services.Condition[ConditionFlag.InCombat])
        {
            Enter(Phase.Fighting, "The fight began.");
            return;
        }

        if (Elapsed > FamiliarWait)
            Ask("Call the familiars for this fight — the run carries on once they are called.");
    }

    private void Commencing()
    {
        // The enemies are in the arena, not on the board: anything hostile seen from the board is not
        // this fight beginning.
        if (Services.Condition[ConditionFlag.InCombat] || (!board.OnBoard && Hostiles().Any()))
        {
            Enter(Phase.Fighting, "The fight began.");
            return;
        }

        // Commence Battle closes the board window and loads the arena; the enemies are there once it
        // has loaded, and until then there is nothing to do but wait.
        if (step is { Done: true })
        {
            if (Elapsed > CommenceTimeout)
                Ask("The fight has not begun. Start it, and the run carries on.");
            else
                Status = "Loading the arena.";

            return;
        }

        Carry(RoomActions.CommenceBattle, "Press \"Commence Battle\" — the run carries on when the fight begins.");
    }

    private void StartFighting()
    {
        combat.Start();
        combat.MayPull = true;
    }

    private void Fighting()
    {
        if (!acted)
        {
            acted = true;
            StartFighting();
        }

        if (!combat.Enabled)
        {
            if (configuration.AbortOnManualInput)
            {
                Fail($"Fighting stopped: {combat.Status}");
                return;
            }

            StartFighting();
        }

        if (Elapsed > LongestFight)
        {
            Fail("The fight has gone on for ten minutes.");
            return;
        }

        var inCombat = Services.Condition[ConditionFlag.InCombat];
        sawCombat |= inCombat;
        leftBoard |= !board.OnBoard;

        // Over once there has been a fight and it has stopped, once the spoils are up, or once the run
        // is back on the board after the arena. Not merely because nothing is fighting yet: right after
        // the arena loads, nothing is.
        var over = (sawCombat && !inCombat && DateTime.Now - lastInCombat > FightEndGrace && !Hostiles().Any())
                   || AddonReader.IsOpen(XbmColumns.RunWindows.Booty)
                   || (leftBoard && board.OnBoard && !inCombat);

        // Continue before anything has fought means the fight did not start, not that it is over.
        if (continueRequested && !sawCombat && !over && Hostiles().Any())
        {
            Note("Continue: the fight has not started; pulling again.");
            combat.Stop("Pulling again.");
            StartFighting();
            return;
        }

        if (over || continueRequested)
        {
            combat.Stop("The fight is over.");
            Enter(Phase.AfterFight, "The fight is over.");
            return;
        }

        Status = combat.Status;
    }

    private void AfterFight()
    {
        if (AddonReader.IsOpen(XbmColumns.RunWindows.Result))
        {
            RoomDone("The board's result is up.");
            return;
        }

        if (AddonReader.IsOpen(XbmColumns.RunWindows.Booty))
        {
            if (Carry(RoomActions.TakeSpoils, "Take the spoils — the run carries on when the window closes."))
                RoomDone("Took the spoils.");

            return;
        }

        if (step != null || Elapsed > SpoilsWait || continueRequested)
            RoomDone(step != null ? "Took the spoils." : "The fight is done.");
        else
            Status = "Waiting for the spoils.";
    }

    /// <summary>
    /// The most hurt familiars are picked first, then the rest is confirmed. With nobody hurt — or
    /// resting familiars switched off — the confirmation rests the player alone, which the game offers
    /// as recovering 90% while the familiars keep watch.
    /// </summary>
    private void Campsite()
    {
        if (!acted)
        {
            acted = true;
            if (configuration.CampsiteRestFamiliars)
                healthSelector.RequestPick();

            return;
        }

        if (step == null && (!PetPartyReader.IsOpen || continueRequested))
        {
            RoomDone("The campsite is done.");
            return;
        }

        if (healthSelector.Busy)
        {
            Status = "Picking the most hurt familiars.";
            return;
        }

        if (Carry(RoomActions.ConfirmCampsite, "Confirm the campsite — the run carries on when the window closes."))
            RoomDone("Rested at the campsite.");
    }

    private void Shop()
    {
        if (!AddonReader.IsOpen(XbmColumns.RunWindows.ItemShop) || continueRequested)
        {
            RoomDone(step != null ? "Left the shop without buying." : "Left the shop.");
            return;
        }

        if (configuration.ShopByHand)
        {
            Ask("Buy what you want, then leave the shop — the run carries on when it closes.");
            return;
        }

        if (Carry(RoomActions.LeaveShop, "Leave the shop — the run carries on when it closes."))
            RoomDone("Left the shop without buying.");
    }

    private void Treasure()
    {
        if (!AddonReader.IsOpen(XbmColumns.RunWindows.Treasure) || continueRequested)
        {
            RoomDone("The treasure is taken.");
            return;
        }

        if (configuration.TreasurePick == Configuration.TreasureByHand)
        {
            Ask("Pick your treasure — the run carries on when the window closes.");
            return;
        }

        if (step == null)
        {
            var offers = RoomActions.TreasureOffers();
            if (offers.Count == 0)
            {
                if (Elapsed > TreasureWait)
                    Ask("The coffer shows no offers BeastMastr can read. Pick one, and the run carries on.");
                else
                    Status = "Reading the coffer.";

                return;
            }

            // Gear already held is refused by the game, and an offer it refused once is not asked again.
            var open = offers.Where(offer => !offer.Held && !refusedOffers.Contains(offer.Index)).ToList();
            if (open.Count == 0)
            {
                Ask("Every offer of this coffer is gear you already hold. Pick one, and the run carries on.");
                return;
            }

            var chosen = open.FirstOrDefault(offer => offer.Index == configuration.TreasurePick);
            var pick = configuration.TreasurePick == Configuration.TreasureRandomGear || chosen == null
                           ? RandomGear(open)
                           : chosen;
            treasureChoice = pick.Index;
            var held = offers.Where(offer => offer.Held).Select(offer => offer.Name).ToList();
            Note($"Taking treasure offer {pick.Index + 1}: {pick.Name}." +
                 (held.Count > 0 ? $" Already held: {string.Join(", ", held)}." : string.Empty));
        }

        if (Carry(RoomActions.ChooseTreasure(treasureChoice), "Pick your treasure — the run carries on when the window closes."))
        {
            RoomDone("Took the treasure.");
            return;
        }

        if (step is { Refused: true })
        {
            Note($"The game refused treasure offer {treasureChoice + 1}, which is already held; choosing another.");
            refusedOffers.Add(treasureChoice);
            step = null;
        }
    }

    /// <summary>A random piece of gear among the offers; any offer when none is gear.</summary>
    private static RoomActions.Offer RandomGear(IReadOnlyList<RoomActions.Offer> offers)
    {
        var gear = offers.Where(offer => offer.IsGear).ToList();
        var pool = gear.Count > 0 ? gear : offers;
        return pool[Random.Shared.Next(pool.Count)];
    }

    /// <summary>Treasure offers the game refused in this room.</summary>
    private readonly HashSet<int> refusedOffers = [];

    /// <summary>
    /// Carries a recorded command through, and hands it to the player if it cannot be. Returns true
    /// once the command has happened.
    /// </summary>
    private bool Carry(RoomActions.Command command, string handOff)
    {
        step ??= new ConfirmedStep(command);
        step.Tick();

        if (step.Failure != null)
        {
            Ask($"{step.Failure} {handOff}");
            return false;
        }

        if (!step.Done)
            Status = $"Sending \"{step.Label}\".";

        return step.Done;
    }

    private void RoomDone(string how)
    {
        DoneEvent = Target;
        Note($"Event {Target} done: {how}");
        Enter(Phase.Settling, how);
    }

    /// <summary>
    /// A moment for the room's windows to go and the board to take note, before walking on. A window
    /// that opens in that moment still belongs to the room — the spoils after a fight, most often — and
    /// is handled before anything else; twice at most, after which it is handed over.
    /// </summary>
    private void Settling()
    {
        if (Elapsed < SettleTime)
            return;

        // A campsite's rest keeps "In Event" on for a while after its window has closed; walking on
        // before it has gone took the leftover for the next room beginning.
        if (Elapsed < EventWait && Services.Objects.LocalPlayer is { } player &&
            player.StatusList.Any(status => status.StatusId == XbmColumns.Crucible.InEventStatus))
        {
            Status = "Waiting for the room to finish.";
            return;
        }

        if (RoomOpened() is { } phase)
        {
            if (++reentries <= 2)
            {
                Enter(phase, "The room is not finished after all.");
                return;
            }

            if (!continueRequested)
            {
                Ask("The room keeps a window open that the run cannot close. Close it, or press Continue.");
                return;
            }
        }

        reentries = 0;

        if (DoneEvent < 0)
        {
            Enter(Phase.Preflight, "Finding out where the run stands.");
            return;
        }

        if (board.Graph!.Node(DoneEvent) is { Kind: BoardRoomKind.Boss })
            Enter(Phase.Finishing, "The boss is done.");
        else
            Enter(Phase.Deciding, "On to the next room.");
    }

    private void Finishing()
    {
        if (AddonReader.IsOpen(XbmColumns.RunWindows.Result))
        {
            if (RoomActions.CloseResult is { } command)
            {
                if (!acted)
                    acted = RoomActions.Send(command);
            }
            else
            {
                Ask("The board is cleared — close the result window.");
            }

            return;
        }

        if (!board.InRunZone || Elapsed > ResultWait || continueRequested || acted || HandOff.Length > 0)
            RunDone();
        else
            Status = "Waiting for the result.";
    }

    private void RunDone()
    {
        RunsDone++;
        Note($"Board finished ({RunsDone} of {RunsWanted}).");

        if (RunsDone >= RunsWanted)
        {
            Enter(Phase.Done, $"Done: {RunsDone} board(s) played.");
            Services.Chat.Print($"[BeastMastr] {Status}");
            return;
        }

        Enter(Phase.Done, $"Board {RunsDone} of {RunsWanted} is done. Going back in from the entrance is not " +
                          "built yet — start the next board by hand, then /beastmastr run again.");
        Services.Chat.Print($"[BeastMastr] {Status}");
    }

    /// <summary>
    /// The boss ends a run with a cutscene and a load back to the entrance, so leaving the zone after it
    /// is how a board finishes rather than a failure.
    /// </summary>
    private bool IsBoss(int eventIndex) => board.Graph?.Node(eventIndex) is { Kind: BoardRoomKind.Boss };

    // ---- What the room is doing -------------------------------------------

    /// <summary>The phase a room window that is up asks for, or null when none is.</summary>
    private static Phase? RoomOpened()
    {
        if (AddonReader.IsOpen(XbmColumns.RunWindows.Booty))
            return Phase.AfterFight;

        if (AddonReader.IsOpen(XbmColumns.RunWindows.ItemShop))
            return Phase.Shop;

        if (AddonReader.IsOpen(XbmColumns.RunWindows.Treasure))
            return Phase.Treasure;

        switch (PetPartyReader.Mode())
        {
            case XbmColumns.PetParty.FightMode:
                return Phase.CallingFamiliars;

            case XbmColumns.PetParty.CampsiteMode:
                return Phase.Campsite;
        }

        return Services.Condition[ConditionFlag.InCombat] ? Phase.Fighting : null;
    }

    private int CalledFamiliars() =>
        PetPartyReader.IsFight ? PetPartyReader.Read(catalog).Count(slot => slot.IsCalled) : 0;

    private static IEnumerable<IBattleChara> Hostiles()
    {
        var player = Services.Objects.LocalPlayer;
        if (player == null)
            return [];

        return Services.Objects.OfType<IBattleChara>()
                       .Where(chara => chara.ObjectKind == ObjectKind.BattleNpc &&
                                       chara.SubKind == (byte)BattleNpcSubKind.Combatant &&
                                       chara.IsTargetable && !chara.IsDead && chara.CurrentHp > 0 &&
                                       Vector3.Distance(chara.Position, player.Position) <= FightSearchRange)
                       .ToList();
    }

    // ---- Bookkeeping ------------------------------------------------------

    private TimeSpan Elapsed => DateTime.Now - phaseSince;

    private void Enter(Phase phase, string status)
    {
        State = phase;
        phaseSince = DateTime.Now;
        acted = false;
        step = null;

        if (phase == Phase.Treasure)
            refusedOffers.Clear();

        if (phase is Phase.CallingFamiliars or Phase.Commencing)
        {
            sawCombat = false;
            leftBoard = false;
        }
        HandOff = string.Empty;
        Status = status;
        Note($"{phase}: {status}");
    }

    /// <summary>Hands a step to the player: said once in chat, and shown for as long as it stands.</summary>
    private void Ask(string what)
    {
        Status = "Waiting for you.";
        if (HandOff == what)
            return;

        HandOff = what;
        Note($"Asked: {what}");
        Services.Chat.Print($"[BeastMastr] {what}");
    }

    private void Fail(string reason)
    {
        walker.Stop(null);
        combat.Stop(reason);
        paused = false;
        State = Phase.Failed;
        Status = reason;
        HandOff = string.Empty;
        Note($"Failed: {reason}");
        Services.Log.Warning($"Run: {reason}");
        Services.Chat.Print($"[BeastMastr] The run stopped: {reason}");
    }

    private void Say(string message)
    {
        Note(message);
        Services.Chat.Print($"[BeastMastr] {message}");
    }

    private void Note(string line)
    {
        log.Insert(0, $"{DateTime.Now:HH:mm:ss} {line}");
        if (log.Count > LogLength)
            log.RemoveRange(LogLength, log.Count - LogLength);

        Services.Log.Information($"Run: {line}");
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        if (Running)
            Stop("Unloaded.");
    }
}
