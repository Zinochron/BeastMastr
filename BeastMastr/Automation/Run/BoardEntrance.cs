using System;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
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

    /// <param name="boardRow">The board to play again, as <c>XBMStageList</c> numbers it.</param>
    public BoardEntrance(uint boardRow) => this.boardRow = boardRow;

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

                Next(1, "Talking to Lauda.");
                return;

            // Lauda.
            case 1:
                if (AddonReader.IsOpen(XbmColumns.Entrance.Menu))
                {
                    Next(2, "Choosing to start a board.");
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
                    if (CrucibleModeReader.Read() is not { Index: >= 0 } ||
                        PetPartyReader.Mode() != XbmColumns.PetParty.TeamCompositionMode)
                    {
                        if (since > WindowTimeout)
                            Fail("The board window did not open.");

                        return;
                    }

                    if (since < ModeTime)
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

        var distance = Vector3.Distance(player.Position, lauda.Position);
        if (distance > XbmColumns.Entrance.TalkRange)
        {
            Fail($"Lauda is {distance:0.0} yalms away; stand next to her.");
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
