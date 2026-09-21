using System;
using System.Collections.Generic;
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
    /// <summary>
    /// How often Lauda is talked to before the step goes to the player. Two was not enough: one user's
    /// run gave up on 2026-09-20 with "Talking to Lauda opened nothing", and an interaction is lost
    /// whenever the game is still busy with the load out of the last board.
    /// </summary>
    private const int Attempts = 4;

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

    /// <summary>A Crucible mode was picked on the Run tab, so its absence is worth a word.</summary>
    private readonly bool modeWanted;

    /// <summary>When the board window was first seen up, for the moment the mode block is given.</summary>
    private DateTime? windowUpAt;

    private bool toldNoMode;

    /// <summary>0: the team not asked for yet; 1: being set; 2: set, or left as it is.</summary>
    private int teamStage;

    private DateTime teamSetAt;

    /// <param name="boardRow">The board to play again, as <c>XBMStageList</c> numbers it.</param>
    /// <param name="team">The team to set in the board window before challenging.</param>
    /// <param name="repair">Whether worn gear is repaired before the next board is started.</param>
    /// <param name="modeWanted">Whether a Crucible mode was picked for the board.</param>
    public BoardEntrance(uint boardRow, TeamSelector teamSelector, RunTeam team, bool repair, bool modeWanted)
    {
        this.boardRow = boardRow;
        this.teamSelector = teamSelector;
        this.team = team;
        this.repair = repair;
        this.modeWanted = modeWanted;
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

                // The menu she opens while she still has a quest to give is a different window.
                if (AddonReader.IsOpen(XbmColumns.Entrance.IconMenu))
                {
                    Next(2, "Reading her menu.");
                    return;
                }

                // Her menu can be gone before it is seen — TextAdvance and the like answer it — and the
                // board list is what it leads to, so that counts as talked to just as well.
                if (AddonReader.IsOpen(XbmColumns.Entrance.BoardList))
                {
                    Next(3, "Her list of boards is open.");
                    return;
                }

                // A greeting to click through, or the game busy with an event: waiting is right, pressing
                // again is not.
                if (AddonReader.IsOpen(TalkWindow) || Busy())
                {
                    Status = "Lauda is talking.";
                    stageSince = now;
                    return;
                }

                // A detail panel from the board outlives it and sits in front of everything out here.
                if (CloseLeftovers())
                    return;

                if (!NearLauda())
                {
                    stageSince = now;
                    return;
                }

                // Mounted, the interaction opens nothing at all, and nothing says why.
                if (Services.Condition[ConditionFlag.Mounted] || Services.Condition[ConditionFlag.RidingPillion])
                {
                    Dismount(now);
                    return;
                }

                // Still being carried along: an interaction sent mid-step is swallowed, and it would
                // cost a try.
                if (Services.Condition[ConditionFlag.Jumping] || NavmeshIpc.IsRunning())
                {
                    Status = "Waiting to come to a stop.";
                    return;
                }

                if (since < TalkRetry && attempts > 0)
                    return;

                if (attempts++ < Attempts)
                {
                    if (TalkToLauda())
                        stageSince = now;

                    return;
                }

                // Pressed enough times with nothing to show: the player takes over, and the run carries
                // on the moment her window is up. Only after long enough does it give up for good.
                if (attempts == Attempts + 1)
                    Report($"Talking to Lauda opened nothing. {WhyNothingOpened()}" +
                           (offers.Count > 0 ? $" Her menu last offered: {string.Join(" | ", offers)}." : string.Empty));

                Ask("Talk to Lauda yourself — the run carries on the moment her window is open.");
                if (now - stageSince > HandOffPatience)
                    Fail($"Talking to Lauda opened nothing. {WhyNothingOpened()}");

                return;

            // Her menu: the first choice, as recorded both times.
            case 2:
                // The quest menu first, when there is one: nothing there was ever recorded, so the only
                // choice taken is the one that is the Crucible by name. Anything else is the player's.
                if (AddonReader.IsOpen(XbmColumns.Entrance.IconMenu))
                {
                    menuSeen = true;
                    var quest = IconEntries();
                    if (quest.Count == 0)
                    {
                        if (since > WindowTimeout)
                            Fail("Her quest menu opened but never filled in.");

                        return;
                    }

                    offers = quest;
                    if (Pick(quest) is { } pick && menuHops < MostMenuHops)
                    {
                        menuHops++;
                        menuSeen = false;
                        stageSince = now;
                        Status = "Opening her Crucible menu.";
                        Note($"picked \"{quest[pick]}\" from her quest menu");
                        return;
                    }

                    Ask($"Pick the Crucible in Lauda's menu yourself — the run only takes the entry that is " +
                        $"the Crucible by name, and hers offers: {string.Join(" | ", quest)}.");
                    return;
                }

                if (AddonReader.IsOpen(XbmColumns.Entrance.Menu))
                {
                    menuSeen = true;

                    // Answered as soon as it is loaded and has its entries rather than after a fixed
                    // wait: a window left sitting is a window something else can answer first. Before it
                    // is loaded nothing can be sent to it, and the trail filled up with the same menu
                    // five times over while it loaded (2026-09-21).
                    var entries = AddonReader.TryGet(XbmColumns.Entrance.Menu, out _) ? MenuEntries() : [];
                    if (entries.Count == 0)
                    {
                        if (since > WindowTimeout)
                            Fail("Lauda's menu opened but never filled in.");

                        return;
                    }

                    if (!offers.SequenceEqual(entries))
                        Note($"her menu offered {string.Join(" | ", entries)}");

                    offers = entries;

                    // With the questline unfinished she opens with a menu of her own: the quest first,
                    // the Crucible second. Picking the Crucible opens the menu that was recorded, so
                    // this stage runs twice (the user, 2026-09-20).
                    if (CrucibleEntry() is { } entry && menuHops < MostMenuHops)
                    {
                        if (RoomActions.Send(new RoomActions.Command(
                                $"open {CrucibleName()}", XbmColumns.Entrance.Menu,
                                [RoomActions.Value.Int(entry)], true, false)))
                        {
                            menuHops++;
                            menuSeen = false;
                            stageSince = now;
                            Status = "Opening her Crucible menu.";
                            Note($"picked choice {entry}, the Crucible itself");
                        }

                        return;
                    }

                    if (RoomActions.Send(PickMenu))
                    {
                        Note($"picked choice {XbmColumns.Entrance.MenuChoice}, to challenge a board");
                        Next(3, "Picking the board.");
                    }

                    return;
                }

                if (AddonReader.IsOpen(XbmColumns.Entrance.BoardList))
                {
                    Next(3, "Her list of boards is open.");
                    return;
                }

                // It was there and went again with nothing sent to it. Something else answered it:
                // there are plugins that answer menus for a living.
                if (menuSeen)
                {
                    menuSeen = false;
                    Note("her menu closed on its own");
                    Report("Lauda's menu closed again before it could be answered. Another plugin - " +
                           "YesAlready, TextAdvance or Pandora's Box - is most likely answering it. " +
                           "Switch that off for her.");
                    Next(1, "Talking to Lauda again.");
                    return;
                }

                if (since > WindowTimeout)
                    Fail("Lauda's menu closed before it was answered.");

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

                    // Up means the board window on screen with the team list beside it in team mode —
                    // mode 0 is only ever shown together with the board window out here. The Crucible mode
                    // block was the sign before, and it only exists once every board has been cleared: a
                    // player still in the questline waited for it forever (2026-09-21).
                    if (!AddonReader.IsOpen(XbmColumns.StageDetailList.Addon) ||
                        PetPartyReader.Mode() != XbmColumns.PetParty.TeamCompositionMode)
                    {
                        if (since > WindowTimeout)
                            Fail(!AddonReader.IsOpen(XbmColumns.StageDetailList.Addon)
                                     ? "The board window did not open."
                                     : $"The board window is open, but the team list beside it is not in team mode " +
                                       $"(mode {PetPartyReader.Mode()}).");

                        return;
                    }

                    windowUpAt ??= now;

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

                    // Without the mode block the board is played on Standard: the choice only appears once
                    // every board has been cleared. It is given a moment to be drawn first.
                    if (CrucibleModeReader.Read() is not { Index: >= 0 })
                    {
                        if (now - windowUpAt < ModeTime)
                            return;

                        if (modeWanted && !toldNoMode)
                        {
                            toldNoMode = true;
                            Services.Chat.Print("[BeastMastr] This board offers no Crucible mode yet — the choice " +
                                                "appears once every board has been cleared. Playing it on Standard.");
                        }

                        Note("no Crucible mode to set, so Standard");
                    }

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
    /// <summary>Her greeting, when she has one to click through.</summary>
    private const string TalkWindow = "Talk";

    /// <summary>
    /// Windows from inside a board that can outlive it: the item and the enemy detail panels. A player's
    /// run stopped at 2026-09-21 00:06 with both of them open and Lauda unreachable behind them.
    /// </summary>
    private static readonly string[] Leftovers =
        [XbmColumns.RunWindows.ItemDetail, XbmColumns.BattleMonsterDetail.Addon];

    /// <summary>How often a leftover panel is closed before it is left alone and named instead.</summary>
    private const int MostCloses = 10;

    private int closes;

    /// <summary>Closes those panels. True when one was in the way, so the talk is tried again after.</summary>
    private bool CloseLeftovers()
    {
        if (closes >= MostCloses)
            return false;

        var closed = false;
        foreach (var window in Leftovers)
        {
            // Visible, not merely loaded: these two stay loaded for the rest of the session, and
            // closing what is already gone did nothing but fill the trail.
            if (!AddonReader.IsOpen(window) || !AddonReader.TryGet(window, out var addon))
                continue;

            addon->Close(true);
            closed = true;
            closes++;
            Note($"closed {window}, left over from the board");
        }

        if (closed)
            Status = "Closing what the board left open.";

        return closed;
    }

    /// <summary>The last few things this step did, so a message can say how it got there.</summary>
    private readonly List<string> trail = [];

    private const int TrailLength = 8;

    /// <summary>Notes a step for the trail and the log alike.</summary>
    private void Note(string what)
    {
        trail.Add(what);
        if (trail.Count > TrailLength)
            trail.RemoveAt(0);

        Services.Log.Information($"Entrance: {what}");
    }

    /// <summary>Her menu was open in this stage, so its going again says something.</summary>
    private bool menuSeen;

    /// <summary>What her menu last offered, for the message when the run gets no further.</summary>
    private List<string> offers = [];

    /// <summary>The quest menu's choices, read and pressed the way ECommons works that window.</summary>
    private static List<string> IconEntries()
    {
        var entries = new List<string>();
        if (!AddonReader.TryGet(XbmColumns.Entrance.IconMenu, out var addon))
            return entries;

        try
        {
            foreach (var entry in new AddonMaster.SelectIconString((nint)addon).Entries)
                entries.Add(entry.Text);
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Entrance: her quest menu could not be read.");
        }

        return entries;
    }

    /// <summary>Presses the entry that is the Crucible by name, and says which it was, or null.</summary>
    private static int? Pick(List<string> entries)
    {
        var name = CrucibleName();
        if (name.Length == 0 || !AddonReader.TryGet(XbmColumns.Entrance.IconMenu, out var addon))
            return null;

        try
        {
            var menu = new AddonMaster.SelectIconString((nint)addon);
            for (var choice = 0; choice < entries.Count && choice < menu.Entries.Length; choice++)
            {
                if (!Plain(entries[choice]).Equals(Plain(name), StringComparison.OrdinalIgnoreCase))
                    continue;

                menu.Entries[choice].Select();
                return choice;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Entrance: her quest menu could not be answered.");
        }

        return null;
    }

    /// <summary>Her menu's choices, in order: choice 0 is value 7 in both recordings.</summary>
    private static List<string> MenuEntries()
    {
        var entries = new List<string>();
        var values = AddonReader.Values(XbmColumns.Entrance.Menu);
        for (var choice = 0; XbmColumns.Entrance.MenuFirstEntry + choice < values.Count; choice++)
        {
            var value = values[XbmColumns.Entrance.MenuFirstEntry + choice];
            if (!value.Type.Contains("String"))
                break;

            entries.Add(value.Text);
        }

        return entries;
    }

    /// <summary>How many menus of hers are stepped through before the recorded one is expected.</summary>
    private const int MostMenuHops = 2;

    private int menuHops;

    /// <summary>What the game calls the Crucible, in the client's own language.</summary>
    private static string CrucibleName() =>
        Services.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>()
                .GetRowOrDefault(XbmColumns.Entrance.CruciblePlaceName)?.Name.ExtractText() ?? string.Empty;

    /// <summary>
    /// The choice in Lauda's open menu that is the Crucible itself, or null when the menu is the
    /// recorded one. Her first menu names it plainly — the recorded menu's entries all say more than
    /// that ("Challenge the Crucible of the Unbroken."), so only the plain name is taken for it.
    /// </summary>
    private static int? CrucibleEntry()
    {
        var name = CrucibleName();
        if (name.Length == 0)
            return null;

        var entries = MenuEntries();
        for (var choice = 0; choice < entries.Count; choice++)
        {
            if (Plain(entries[choice]).Equals(Plain(name), StringComparison.OrdinalIgnoreCase))
                return choice;
        }

        return null;
    }

    /// <summary>Start of Unicode's private use area, where the game keeps its own glyphs.</summary>
    private const char FirstPrivateGlyph = (char)0xE000;
    private const char LastPrivateGlyph = (char)0xF8FF;

    /// <summary>
    /// An entry as it reads without the game's own icons. A menu entry can carry a quest or content
    /// glyph in front of its text — private use characters, which render as nothing outside the game's
    /// font and survive every string operation until something drops them on purpose — and a comparison
    /// against the plain name would miss because of one.
    /// </summary>
    private static string Plain(string text) =>
        new string(text.Where(c => (c < FirstPrivateGlyph || c > LastPrivateGlyph) && !char.IsWhiteSpace(c))
                       .ToArray());

    /// <summary>The Dismount general action, as the <c>GeneralAction</c> sheet numbers it.</summary>
    private const uint DismountAction = 23;

    private static readonly TimeSpan DismountRetry = TimeSpan.FromSeconds(2);

    /// <summary>How long the player is given to open Lauda's window before the run gives up.</summary>
    private static readonly TimeSpan HandOffPatience = TimeSpan.FromMinutes(3);

    private DateTime lastDismount = DateTime.MinValue;

    /// <summary>The game is in the middle of something of its own, and an interaction would be lost.</summary>
    private static bool Busy() =>
        Services.Condition[ConditionFlag.OccupiedInQuestEvent] || Services.Condition[ConditionFlag.OccupiedInEvent] ||
        Services.Condition[ConditionFlag.Occupied33] || Services.Condition[ConditionFlag.Occupied38] ||
        Services.Condition[ConditionFlag.WatchingCutscene] || Services.Condition[ConditionFlag.WatchingCutscene78] ||
        Services.Condition[ConditionFlag.BetweenAreas] || Services.Condition[ConditionFlag.Casting] ||
        Services.Condition[ConditionFlag.Jumping];

    /// <summary>Gets off the mount, which is the usual reason talking to her opens nothing.</summary>
    private void Dismount(DateTime now)
    {
        Status = "Getting off the mount.";
        if (now - lastDismount < DismountRetry)
            return;

        lastDismount = now;
        var manager = ActionManager.Instance();
        if (manager != null)
            manager->UseAction(ActionType.GeneralAction, DismountAction);

        Services.Log.Information("Entrance: mounted, so dismounting before talking to Lauda.");
    }

    /// <summary>Hands a step to the player: said once in chat, and shown for as long as it stands.</summary>
    private void Ask(string what)
    {
        Status = "Waiting for you.";
        if (HandOff == what)
            return;

        HandOff = what;
        Services.Chat.Print($"[BeastMastr] {what}");
    }

    /// <summary>
    /// Why talking to her opened nothing, in a sentence a player can act on or pass on. It goes to the
    /// chat, not only to the log: a player who hits this is not reading `dalamud.log`.
    /// </summary>
    private string WhyNothingOpened()
    {
        var player = Services.Objects.LocalPlayer;
        var lauda = Services.Objects.FirstOrDefault(obj => obj.ObjectKind == ObjectKind.EventNpc &&
                                                           obj.BaseId == XbmColumns.Entrance.Npc);

        if (lauda == null)
            return "Lauda is not where the run looked for her, by the Crucible entrance in Central Shroud.";

        if (player != null && Vector3.Distance(player.Position, lauda.Position) is var distance and > XbmColumns.Entrance.TalkRange)
            return $"She is {distance:0.0} yalms away, which is too far to talk to her.";

        if (!lauda.IsTargetable)
            return "She cannot be targeted at the moment.";

        if (Services.Condition[ConditionFlag.Mounted] || Services.Condition[ConditionFlag.RidingPillion])
            return "You are still mounted, and the run could not get you off.";

        if (Services.Condition[ConditionFlag.InCombat])
            return "You are in combat, and she will not talk during one.";

        if (Busy())
            return "The game is in the middle of something else — an event, a cutscene or a load.";

        // The lifecycle knows what a frame-by-frame look cannot: whether her menu was up at all. One
        // that came and went without this run answering it was answered by something else.
        if (AddonReader.IsOpen(XbmColumns.Entrance.IconMenu))
            return "Her menu with the quest in it is open, and the run found no entry in it that is the " +
                   "Crucible by name.";

        if (MenuWatch.Instance is { } watch && watch.LastSetupAt > talkedAt)
        {
            var offered = watch.LastEntries.Count > 0
                              ? $" It offered: {string.Join(" | ", watch.LastEntries)}."
                              : string.Empty;

            return $"Her menu ({watch.LastMenu}) opened and was answered before the run could read it. " +
                   "Another plugin — YesAlready, TextAdvance or Pandora's Box — is doing that. " +
                   "Switch it off for her." + offered;
        }

        var windows = AddonReader.OpenAddonNames(visibleOnly: true).ToList();
        if (windows.Count > 0)
            return $"A window is in the way and would not close: {string.Join(", ", windows)}. " +
                   "Close it, and the run carries on.";

        return "Her menu never opened, and nothing on screen is in the way. " +
               $"The game answered {lastAnswer} to the interaction.";
    }

    /// <summary>
    /// Says something once, to the chat and the log alike, with the trail of what the step did to get
    /// there — that trail is what a player can pass on without opening a log.
    /// </summary>
    private void Report(string message)
    {
        if (reported == message)
            return;

        reported = message;
        var full = trail.Count > 0 ? $"{message} What it did: {string.Join(" > ", trail)}." : message;
        Services.Chat.PrintError($"[BeastMastr] {full}");
        Services.Log.Warning($"Entrance: {full}");
    }

    private string reported = string.Empty;

    /// <summary>
    /// Lauda, the nearest one of her if the object table holds more than one: the run has to walk to,
    /// and then talk to, the same one.
    /// </summary>
    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? Lauda()
    {
        var player = Services.Objects.LocalPlayer;
        Dalamud.Game.ClientState.Objects.Types.IGameObject? nearest = null;
        var best = float.MaxValue;

        foreach (var obj in Services.Objects)
        {
            if (obj.ObjectKind != ObjectKind.EventNpc || obj.BaseId != XbmColumns.Entrance.Npc)
                continue;

            var distance = player == null ? 0f : Vector3.Distance(player.Position, obj.Position);
            if (distance >= best)
                continue;

            best = distance;
            nearest = obj;
        }

        return nearest;
    }

    private bool NearLauda()
    {
        if (Services.Objects.LocalPlayer is not { } player)
            return false;

        var lauda = Lauda();
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

    /// <summary>When the run last reached for her, so a menu seen after that is the one it asked for.</summary>
    private DateTime talkedAt = DateTime.MinValue;

    private DateTime? walkingSince;
    private DateTime lastWalkOrder;
    private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan WalkReorder = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Reaches for her with the game's own ways of reaching for an object, a different one each try:
    /// the plain interaction, then the same without the line-of-sight check, then the object
    /// interaction the game opens for a target. One player's four tries all opened nothing and said
    /// nothing about why (2026-09-21), so what the game answers is written down as well.
    /// </summary>
    private bool TalkToLauda()
    {
        var player = Services.Objects.LocalPlayer;
        var lauda = Lauda();
        if (player == null || lauda == null)
        {
            Fail("Lauda is not here.");
            return false;
        }

        var targets = TargetSystem.Instance();
        if (targets == null)
            return false;

        Services.Targets.Target = lauda;
        talkedAt = DateTime.Now;

        var obj = (GameObjectStruct*)lauda.Address;
        switch (attempts)
        {
            case 1:
                lastAnswer = targets->InteractWithObject(obj);
                Note($"talked to Lauda (the game said {lastAnswer})");
                break;

            case 2:
                lastAnswer = targets->InteractWithObject(obj, false);
                Note($"talked to Lauda without the line-of-sight check (the game said {lastAnswer})");
                break;

            default:
                targets->OpenObjectInteraction(obj);
                Note("opened her interaction directly");
                break;
        }

        return true;
    }

    /// <summary>What the game answered to the last interaction. 0 is the answer a working one gives.</summary>
    private ulong lastAnswer;

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

    /// <summary>
    /// Ends the step, with what the game was showing at the time. The run prints the reason to the chat,
    /// so the windows that were open belong in it rather than in the log alone.
    /// </summary>
    private void Fail(string reason)
    {
        var windows = AddonReader.OpenAddonNames(visibleOnly: true).ToList();
        Failure = windows.Count > 0
                      ? $"{reason} Open at the time: {string.Join(", ", windows)}."
                      : $"{reason} No window was open at the time.";

        if (trail.Count > 0)
            Failure += $" What it did: {string.Join(" > ", trail)}.";

        Status = Failure;
        Services.Log.Warning($"Entrance: {Failure}");
    }
}
