using System;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Ipc;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace BeastMastr.Automation.Run;

/// <summary>
/// Starts the next board from the entrance after one has ended, the way it was done by hand: talk to
/// Lauda, pick the board, challenge it, commence the duty, wait until the board has begun. Every step
/// is one recorded twice — see <see cref="XbmColumns.Entrance"/>. Ticked by the run; says when it is
/// done and why it gave up.
/// </summary>
public sealed unsafe class BoardEntrance
{
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WindowDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TalkRetry = TimeSpan.FromSeconds(4);

    /// <summary>The board window opens on Standard; the Crucible mode is set back before challenging.</summary>
    private static readonly TimeSpan ModeTime = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(90);
    private const int Attempts = 2;

    private static readonly RoomActions.Command PickMenu =
        new("start a board", XbmColumns.Entrance.Menu, [RoomActions.Value.Int(XbmColumns.Entrance.MenuChoice)], true, false);

    private static readonly RoomActions.Command Challenge =
        new("Challenge the board", XbmColumns.StageDetailList.Addon,
            [RoomActions.Value.Int(XbmColumns.Entrance.ChallengeCommand)], true, true);

    private static readonly RoomActions.Command Commence =
        new("Commence the duty", XbmColumns.Entrance.DutyConfirm,
            [RoomActions.Value.Int(XbmColumns.Entrance.CommenceDutyCommand)], true, false);

    private readonly uint boardRow;
    private readonly DateTime created = DateTime.Now;
    private DateTime stageSince = DateTime.Now;
    private DateTime? calmSince;
    private ConfirmedStep? challenge;
    private int stage;
    private int attempts;

    private readonly TeamSelector teamSelector;
    private readonly RunTeam team;
    private readonly bool repair;

    /// <summary>0: the team not asked for yet; 1: being set; 2: set, or left as it is.</summary>
    private int teamStage;

    private DateTime teamSetAt;

    /// <param name="boardRow">The board to play again, as <c>XBMStageList</c> numbers it.</param>
    /// <param name="team">The team to set in the board window before challenging.</param>
    /// <param name="repair">Whether worn gear is repaired before the next board is started.</param>
    public BoardEntrance(uint boardRow, TeamSelector teamSelector, RunTeam team, bool repair)
    {
        this.boardRow = boardRow;
        this.teamSelector = teamSelector;
        this.team = team;
        this.repair = repair;
        teamStage = team == RunTeam.Keep ? 2 : 0;
    }

    public bool Done { get; private set; }

    /// <summary>Why the next board could not be started, or null while it can.</summary>
    public string? Failure { get; private set; }

    public string Status { get; private set; } = "Waiting to be back at the entrance.";

    public void Tick()
    {
        if (Done || Failure != null)
            return;

        var now = DateTime.Now;
        var since = now - stageSince;

        switch (stage)
        {
            // Back at the entrance, loaded and free to act.
            case 0:
                if (Services.ClientState.TerritoryType != XbmColumns.Entrance.Territory || !Calm())
                {
                    calmSince = null;
                    if (now - created > ArrivalTimeout)
                        Fail("The entrance in Central Shroud was never reached.");

                    return;
                }

                calmSince ??= now;
                if (now - calmSince.Value < SettleTime)
                    return;

                if (!Repaired(now))
                    return;

                Next(1, "Talking to Lauda.");
                return;

            // Lauda.
            case 1:
                if (AddonReader.IsOpen(XbmColumns.Entrance.Menu))
                {
                    Next(2, "Choosing to start a board.");
                    return;
                }

                if (!NearLauda())
                {
                    stageSince = now;
                    return;
                }

                if (since < TalkRetry && attempts > 0)
                    return;

                if (++attempts > Attempts)
                {
                    Fail("Talking to Lauda opened nothing.");
                    return;
                }

                if (!TalkToLauda())
                    return;

                stageSince = now;
                return;

            // Her menu: the first choice, as recorded both times.
            case 2:
                if (since < WindowDelay)
                    return;

                if (!AddonReader.IsOpen(XbmColumns.Entrance.Menu))
                {
                    if (since > WindowTimeout)
                        Fail("Lauda's menu closed before it was answered.");

                    return;
                }

                Services.Log.Information("Entrance: Lauda offers " +
                                         string.Join(" | ", AddonReader.Values(XbmColumns.Entrance.Menu)
                                                                       .Where(value => value.Type.Contains("String") &&
                                                                                       value.Text.Length > 0)
                                                                       .Select(value => value.Text)));
                if (RoomActions.Send(PickMenu))
                    Next(3, "Picking the board.");

                return;

            // The board list.
            case 3:
                if (!AddonReader.IsOpen(XbmColumns.Entrance.BoardList) || since < WindowDelay)
                {
                    if (since > WindowTimeout)
                        Fail("The board list did not open.");

                    return;
                }

                if (FindBoard() is not { } name)
                {
                    if (since > WindowTimeout)
                        Fail($"Board {boardRow} is not in the board list.");

                    return;
                }

                if (RoomActions.Send(new RoomActions.Command(
                        $"pick {name}", XbmColumns.Entrance.BoardList,
                        [RoomActions.Value.Int(XbmColumns.Entrance.PickBoardCommand), RoomActions.Value.Int((int)boardRow)],
                        true, false)))
                    Next(4, $"Opening {name}.");

                return;

            // The board window, once it is really up and the Crucible mode has had its turn. The window
            // reports itself open long after use, so its mode block showing is what counts.
            case 4:
                // Queued already: the challenge went through, whatever the window still says.
                if (AddonReader.IsOpen(XbmColumns.Entrance.DutyConfirm) ||
                    Services.Condition[ConditionFlag.InDutyQueue] ||
                    Services.Condition[ConditionFlag.WaitingForDutyFinder])
                {
                    Next(5, "Waiting for the duty to be ready.");
                    return;
                }

                if (challenge == null)
                {
                    // The team first, leveling or farming. The fill opens the bestiary beside the team
                    // list, so the board window may look closed until it is done.
                    if (teamStage == 1)
                    {
                        if (teamSelector.Busy)
                        {
                            Status = $"Setting the team: {teamSelector.Status}";
                            stageSince = now;
                            return;
                        }

                        if (teamSelector.GaveUp)
                        {
                            Fail($"The team could not be set: {teamSelector.Status}");
                            return;
                        }

                        teamStage = 2;
                        teamSetAt = now;
                        Services.Log.Information($"Entrance: team set for {team}: {teamSelector.Status}");
                    }

                    if (CrucibleModeReader.Read() is not { Index: >= 0 } ||
                        PetPartyReader.Mode() != XbmColumns.PetParty.TeamCompositionMode)
                    {
                        if (since > WindowTimeout)
                            Fail("The board window did not open.");

                        return;
                    }

                    if (teamStage == 0)
                    {
                        teamStage = 1;
                        teamSelector.RequestFill(team == RunTeam.Farming);
                        Status = team == RunTeam.Farming ? "Setting the team to the carries." : "Filling the team for leveling.";
                        Services.Log.Information($"Entrance: {Status}");
                        return;
                    }

                    if (since < ModeTime || now - teamSetAt < ModeTime)
                        return;

                    challenge = new ConfirmedStep(Challenge);
                }

                challenge.Tick();

                if (challenge.Failure != null)
                {
                    Fail(challenge.Failure);
                    return;
                }

                if (challenge.Done)
                    Next(5, "Waiting for the duty to be ready.");

                return;

            // The duty finder's confirmation.
            case 5:
                if (!AddonReader.IsOpen(XbmColumns.Entrance.DutyConfirm) || since < WindowDelay)
                {
                    if (since > QueueTimeout)
                        Fail("The board was never offered to commence.");

                    return;
                }

                if (RoomActions.Send(Commence))
                    Next(6, "Loading the board.");

                return;

            // On the board, past the opening cutscene.
            case 6:
                if (!BoardModel.IsRunTerritory(Services.ClientState.TerritoryType) || !Calm() ||
                    !AddonReader.IsOpen(XbmColumns.ContentsMainHUD.Addon))
                {
                    calmSince = null;
                    if (since > LoadTimeout)
                        Fail("The board did not load.");

                    return;
                }

                calmSince ??= now;
                if (now - calmSince.Value < SettleTime)
                    return;

                Done = true;
                Status = "The next board has begun.";
                return;
        }
    }

    private static bool Calm() =>
        Services.Objects.LocalPlayer != null && !RunSafety.Waiting() &&
        !Services.Condition[ConditionFlag.OccupiedInQuestEvent];

    /// <summary>What the player has to do before the run can carry on, or empty.</summary>
    public string HandOff { get; private set; } = string.Empty;

    /// <summary>The window the game's own Repair action opens.</summary>
    private const string RepairWindow = "Repair";

    /// <summary>The Repair general action, which opens that window.</summary>
    private const uint RepairAction = 6;

    /// <summary>How long the window gets to open before the board is played unrepaired.</summary>
    private static readonly TimeSpan RepairWait = TimeSpan.FromSeconds(6);

    /// <summary>How long the repair itself gets before it is handed to the player.</summary>
    private static readonly TimeSpan RepairPatience = TimeSpan.FromSeconds(30);

    /// <summary>Between presses of "Repair All", so one press is given time to work.</summary>
    private static readonly TimeSpan PressAgain = TimeSpan.FromSeconds(3);

    private const int MostPresses = 4;

    private DateTime? repairAskedAt;
    private DateTime? repairPressedAt;
    private int repairPresses;
    private bool repairDone;

    /// <summary>
    /// Between boards, gear below full is repaired, start to finish: the game's own Repair action
    /// (<c>GeneralAction</c> 6) opens the window, "Repair All" is pressed the way a click presses it
    /// (ECommons' <c>AddonMaster.Repair</c>, which works the button itself rather than guessing what it
    /// sends), the question it asks is answered yes, and the window is closed once everything is whole.
    ///
    /// Nothing here can run away with the run: after <see cref="RepairPatience"/> — no dark matter, or a
    /// crafter level too low to mend these items — the step is handed to the player, and if the window
    /// never opens at all the board is played as it is. True once there is nothing to wait for.
    /// </summary>
    private bool Repaired(DateTime now)
    {
        if (repairDone || !repair)
            return true;

        var lowest = GearDurability.Lowest();
        var open = AddonReader.IsOpen(RepairWindow);

        if (lowest >= 1f)
        {
            // Whole again: close the window the run opened, then carry on next tick.
            if (open)
            {
                if (AddonReader.TryGet(RepairWindow, out var window))
                    window->Close(true);

                return false;
            }

            repairDone = true;
            HandOff = string.Empty;
            if (repairAskedAt != null)
                Services.Log.Information("Repair: the gear is whole again.");

            return true;
        }

        if (repairAskedAt is not { } asked)
        {
            repairAskedAt = now;
            var manager = ActionManager.Instance();
            if (manager == null || !manager->UseAction(ActionType.GeneralAction, RepairAction))
                Services.Log.Information("Repair: the Repair action could not be used here.");

            Status = $"Gear at {lowest:P0}: repairing.";
            Services.Log.Information($"Entrance: {Status}");
            return false;
        }

        if (open)
        {
            Status = $"Gear at {lowest:P0}: repairing.";

            // "Repair all items?" — the same question a click raises, answered the recorded way.
            if (AddonReader.IsOpen(RoomActions.YesnoAddon))
            {
                if (RoomActions.Send(RoomActions.Yes))
                    repairPressedAt = now;

                return false;
            }

            if (now - asked > RepairPatience)
            {
                HandOff = "Repair your gear — BeastMastr could not. Dark matter, most likely, or a crafter " +
                          "level too low for these items. The run carries on once everything is whole.";
                return false;
            }

            if (repairPresses < MostPresses && (repairPressedAt is not { } pressed || now - pressed > PressAgain))
            {
                repairPresses++;
                repairPressedAt = now;
                RepairAll();
            }

            return false;
        }

        if (now - asked < RepairWait)
            return false;

        // No window and nothing repaired: nothing to repair with, most likely. The board is played anyway.
        repairDone = true;
        HandOff = string.Empty;
        Services.Log.Information($"Repair: the gear is still at {lowest:P0}; carrying on regardless.");
        return true;
    }

    /// <summary>Presses "Repair All" in the open repair window.</summary>
    private static void RepairAll()
    {
        if (!AddonReader.TryGet(RepairWindow, out var window))
            return;

        try
        {
            new AddonMaster.Repair((nint)window).RepairAll();
            Services.Log.Information("Repair: pressed Repair All.");
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Repair: Repair All could not be pressed.");
        }
    }

    /// <summary>
    /// Walks to Lauda with vnavmesh when she is out of reach: a run started anywhere in Central Shroud
    /// begins there. True once she is close enough to talk to.
    /// </summary>
    private bool NearLauda()
    {
        if (Services.Objects.LocalPlayer is not { } player)
            return false;

        var lauda = Services.Objects.FirstOrDefault(obj => obj.ObjectKind == ObjectKind.EventNpc &&
                                                            obj.BaseId == XbmColumns.Entrance.Npc);
        var at = lauda?.Position ?? XbmColumns.Entrance.NpcPosition;
        var distance = Vector3.Distance(player.Position, at);
        if (distance <= XbmColumns.Entrance.TalkRange && lauda != null)
        {
            if (walkingSince != null)
            {
                NavmeshIpc.Stop();
                walkingSince = null;
            }

            return true;
        }

        var now = DateTime.Now;
        if (walkingSince is { } since && now - since > WalkTimeout)
        {
            NavmeshIpc.Stop();
            Fail($"Could not walk to Lauda; {distance:0.0} yalms short.");
            return false;
        }

        if (!NavmeshIpc.IsLoaded)
        {
            Fail($"Lauda is {distance:0.0} yalms away, and vnavmesh is needed to walk there.");
            return false;
        }

        // Asked again now and then, in case the first order was lost or the mesh was still building.
        if (walkingSince == null || now - lastWalkOrder > WalkReorder && !NavmeshIpc.IsRunning() &&
            !NavmeshIpc.PathfindInProgress())
        {
            walkingSince ??= now;
            lastWalkOrder = now;
            NavmeshIpc.PathfindAndMoveCloseTo(at, XbmColumns.Entrance.TalkRange - 1.5f);
            Status = $"Walking to Lauda, {distance:0} yalms.";
            Services.Log.Information($"Entrance: {Status}");
        }

        return false;
    }

    private DateTime? walkingSince;
    private DateTime lastWalkOrder;
    private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan WalkReorder = TimeSpan.FromSeconds(3);

    private bool TalkToLauda()
    {
        var player = Services.Objects.LocalPlayer;
        var lauda = Services.Objects.FirstOrDefault(obj => obj.ObjectKind == ObjectKind.EventNpc &&
                                                            obj.BaseId == XbmColumns.Entrance.Npc);
        if (player == null || lauda == null)
        {
            Fail("Lauda is not here.");
            return false;
        }

        var targets = TargetSystem.Instance();
        if (targets == null)
            return false;

        Services.Targets.Target = lauda;
        targets->InteractWithObject((GameObjectStruct*)lauda.Address);
        Services.Log.Information("Entrance: talked to Lauda.");
        return true;
    }

    /// <summary>The board's name when the list holds it, else null.</summary>
    private string? FindBoard()
    {
        var values = AddonReader.Values(XbmColumns.Entrance.BoardList);
        if (values.Count <= XbmColumns.Entrance.BoardCount ||
            !int.TryParse(values[XbmColumns.Entrance.BoardCount].Text, out var count))
            return null;

        for (var i = 0; i < count; i++)
        {
            var at = XbmColumns.Entrance.FirstBoard + (i * XbmColumns.Entrance.BoardStride);
            if (at + XbmColumns.Entrance.BoardRowOffset >= values.Count)
                break;

            if (uint.TryParse(values[at + XbmColumns.Entrance.BoardRowOffset].Text, out var row) && row == boardRow)
                return values[at].Text;
        }

        return null;
    }

    private void Next(int to, string status)
    {
        stage = to;
        stageSince = DateTime.Now;
        attempts = 0;
        Status = status;
        Services.Log.Information($"Entrance: {status}");
    }

    private void Fail(string reason)
    {
        Failure = reason;
        Status = reason;
    }
}
