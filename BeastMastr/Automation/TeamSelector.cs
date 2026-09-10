using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Fills a run's team with the beasts that need the experience, plus the ones chosen to carry them.
///
/// In two phases: **empty the team, then fill it**. The first version worked out the difference
/// between the team as it was and the team it wanted, and toggled only that — which made the result
/// depend on reading the starting team exactly right, and a misread beast stayed where it was.
/// Emptying first means the end state depends on nothing but the plan, and "the roster reads empty"
/// is a check the window can answer before a single beast is added.
///
/// Emptying is the game's own "Remove all", replayed from a recording: right-click the first row,
/// pick the third entry of its menu, confirm. That needs only the team list. Adding toggles tiles in
/// the bestiary the way a click does — <c>[7, slot]</c> — so it needs the bestiary, which it opens
/// with the team list's own button when it is not up yet.
/// </summary>
public sealed unsafe class TeamSelector : IDisposable
{
    /// <summary>Frames between two toggles, so the window can answer one before the next.</summary>
    private const int FramesBetweenToggles = 8;

    /// <summary>
    /// How long a toggle is given to show up in the roster before it counts as refused. One look was
    /// not enough: the two windows do not update in the same frame, and failing on the first glance
    /// reports a refusal that never happened.
    /// </summary>
    private const int FramesToConfirm = 45;

    /// <summary>Frames to let a page turn settle before looking for a beast on it.</summary>
    private const int FramesAfterPageTurn = 12;

    /// <summary>How long a window the game opens in answer — the menu, the confirmation — may take to appear.</summary>
    private const int FramesToAppear = 60;

    private const string ContextMenuAddon = "ContextMenu";
    private const string ConfirmationAddon = "SelectYesno";

    private enum Phase
    {
        Idle,

        /// <summary>"Remove all", then a check that the roster really is empty.</summary>
        Emptying,

        /// <summary>Putting the plan in, carries first, then the least advanced.</summary>
        Filling,
    }

    /// <summary>The recorded steps of "Remove all", one per window the game puts up on the way.</summary>
    private enum EmptyStep
    {
        OpenMenu,
        PickRemoveAll,
        Confirm,
        WaitForEmpty,
    }

    /// <param name="Join">What this toggle is for. Stated, not inferred from the roster at the time —
    /// inferring it turned "remove" into "add" for a beast that had already gone.</param>
    private readonly record struct Step(uint Beast, bool Join);

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly RankWatcher ranks;

    private readonly Queue<Step> pending = new();
    private Phase phase;
    private EmptyStep emptyStep;
    private List<uint> plan = [];
    private int cooldown;
    private Step? waiting;
    private int framesWaited;
    private bool givenUp;
    private bool askedForBestiary;

    /// <summary>Frames waited for the bestiary since asking for it, or -1 before asking.</summary>
    private int bestiaryWait = -1;

    /// <summary>Frames the team list has been gone while a fill is under way.</summary>
    private int framesAway;

    public TeamSelector(Configuration configuration, BeastCatalog catalog, RankWatcher ranks)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.ranks = ranks;

        Services.Framework.Update += OnUpdate;
    }

    public string Status { get; private set; } = string.Empty;

    /// <summary>Set by the button. Runs once, whatever the mode says, and clears the give-up flag.</summary>
    private bool requested;

    /// <summary>
    /// Fill the team now, asked for rather than triggered. A button is a better place for this than
    /// a mode that fires on its own the moment a window opens — you press it when you mean it, and
    /// pressing it again after a failure is how you retry.
    /// </summary>
    public void RequestFill()
    {
        Reset();
        requested = true;
        givenUp = false;
    }

    private static bool BestiaryOpen => AddonReader.IsOpen(XbmColumns.MonsterNotebook.Addon);

    private void OnUpdate(IFramework framework)
    {
        if (givenUp)
            return;

        if (phase == Phase.Idle && !requested && configuration.TeamSelection != TeamMode.Leveling)
            return;

        // The team list is the screen. Closing it, or the window switching to calling a fight's
        // familiars, ends whatever was under way — but not at the first blink: opening the bestiary
        // closes the team list and brings it back beside it, and a fill that ended on that gap would
        // end every time it opened the bestiary itself.
        if (!PetPartyReader.IsTeamComposition)
        {
            if (phase != Phase.Idle && ++framesAway < FramesToAppear * 2)
                return;

            Reset();
            return;
        }

        framesAway = 0;

        switch (phase)
        {
            case Phase.Idle:
                // The automatic mode waits for the bestiary as well: emptying a team the moment the
                // screen opens, before anyone has asked, is not something to spring on a player.
                if (requested || BestiaryOpen)
                    Start();
                break;

            case Phase.Emptying:
                Empty();
                break;

            case Phase.Filling:
                Fill();
                break;
        }
    }

    private void Start()
    {
        requested = false;

        var current = TeamNow();

        var known = catalog.Beasts
                           .Select(beast => (beast, rank: ranks.RankOf(beast.Number)))
                           .Where(pair => pair.rank is not null)
                           .Select(pair => new TeamPlanner.Candidate(pair.beast.Number, pair.beast.Name, pair.rank!.Value))
                           .ToList();

        if (known.Count == 0)
        {
            Tell("No ranks known yet, so there is nothing to sort on. Browse the bestiary once.");
            return;
        }

        // The window says how many this board takes. The tier setting is only the fallback for when
        // it does not — a setting is what someone said, the window is what the board does.
        var size = PetPartyReader.Count()?.Capacity ?? TeamPlanner.TeamSizeFor(configuration.BoardTier);

        // In the order it is to be added: carries first, then the least advanced. If the board takes
        // fewer than the setting says, what is left off is the end of this list, not the carries.
        plan = TeamPlanner.ForLeveling(known, size, configuration.CarryBeasts, current)
                          .Select(candidate => candidate.BeastNumber)
                          .ToList();

        if (plan.ToHashSet().SetEquals(current))
        {
            Tell($"Team already matches ({plan.Count} beasts).");
            return;
        }

        Status = $"Emptying the team ({current.Count}), then adding {plan.Count} of the {size} it takes — " +
                 $"{known.Count} of {catalog.Beasts.Count} ranks known.";
        Services.Log.Information(Status);

        phase = Phase.Emptying;
        emptyStep = EmptyStep.OpenMenu;
        framesWaited = 0;
    }

    // ---- Emptying --------------------------------------------------------

    /// <summary>
    /// "Remove all", step by step, each one waiting for the window the last one made the game open.
    ///
    /// The confirmation is the safety check as much as a step: the menu entry is picked by its
    /// position, and if the entry at that position were ever something other than "Remove all", the
    /// odds that it also asks "are you sure" are slim. No confirmation means stop, and say what the
    /// menu offered.
    /// </summary>
    private void Empty()
    {
        if (cooldown-- > 0)
            return;

        switch (emptyStep)
        {
            case EmptyStep.OpenMenu:
                if (TeamNow().Count == 0)
                {
                    BeginFilling();
                    return;
                }

                if (!Fire(XbmColumns.PetParty.Addon, false,
                          (AtkValueType.Int, XbmColumns.PetParty.OpenMenuCommand), (AtkValueType.Int, 0)))
                {
                    Stop("The team list went away before its menu could be opened.");
                    return;
                }

                Next(EmptyStep.PickRemoveAll);
                return;

            case EmptyStep.PickRemoveAll:
                if (!AddonReader.IsOpen(ContextMenuAddon))
                {
                    if (++framesWaited > FramesToAppear)
                        Stop("Right-clicking the first beast in the team list did not open its menu.");

                    return;
                }

                offered = Strings(ContextMenuAddon);
                Services.Log.Information($"Team list menu offers: {string.Join(" | ", offered)}");

                Fire(ContextMenuAddon, true,
                     (AtkValueType.Int, 0), (AtkValueType.Int, XbmColumns.PetParty.RemoveAllMenuEntry),
                     (AtkValueType.UInt, 0), (AtkValueType.Undefined, 0), (AtkValueType.Undefined, 0));

                Next(EmptyStep.Confirm);
                return;

            case EmptyStep.Confirm:
                if (!AddonReader.IsOpen(ConfirmationAddon))
                {
                    if (++framesWaited > FramesToAppear)
                    {
                        Stop($"Picked entry {XbmColumns.PetParty.RemoveAllMenuEntry + 1} of the menu but no " +
                             $"confirmation followed, so it may not have been \"Remove all\". " +
                             $"The menu offered: {string.Join(" | ", offered)}");
                    }

                    return;
                }

                Services.Log.Information($"Confirming: {string.Join(" ", Strings(ConfirmationAddon).Take(1))}");
                Fire(ConfirmationAddon, true, (AtkValueType.Int, 0));

                Next(EmptyStep.WaitForEmpty);
                return;

            case EmptyStep.WaitForEmpty:
                var left = TeamNow();
                if (left.Count == 0)
                {
                    BeginFilling();
                    return;
                }

                if (++framesWaited > FramesToConfirm * 2)
                    Stop($"Confirmed \"Remove all\", but the team list still shows {string.Join(", ", left.Select(Name))}.");

                return;
        }
    }

    /// <summary>What the menu said it offered, kept for the message if the pick goes wrong.</summary>
    private List<string> offered = [];

    private void Next(EmptyStep step)
    {
        emptyStep = step;
        framesWaited = 0;
        cooldown = 2;
    }

    private void BeginFilling()
    {
        phase = Phase.Filling;
        pending.Clear();

        foreach (var beast in plan)
            pending.Enqueue(new Step(beast, Join: true));

        Status = $"Team empty. Adding {plan.Count}…";
    }

    // ---- Filling ---------------------------------------------------------

    private void Fill()
    {
        // Adding goes through the bestiary's tiles, so it has to be open.
        if (!BestiaryOpen)
        {
            OpenBestiary();
            return;
        }

        if (cooldown-- > 0)
            return;

        if (waiting is { } step && !Confirmed(step))
            return;

        if (pending.Count == 0)
        {
            Tell($"Team set: {TeamNow().Count} beasts.");
            Reset();
            return;
        }

        var next = pending.Dequeue();

        // Already the way it is meant to be — put in by hand meanwhile.
        if (InTeam(next.Beast) == next.Join)
            return;

        var slot = SlotOf(next.Beast);

        if (slot < 0)
        {
            TurnTo(next);
            return;
        }

        if (!Send(XbmColumns.MonsterNotebook.ToggleTeamCommand, slot))
        {
            Stop("The bestiary went away mid-selection.");
            return;
        }

        waiting = next;
        framesWaited = 0;
        cooldown = FramesBetweenToggles;
    }

    /// <summary>
    /// Presses the team list's own "open the bestiary" button, recorded as <c>[5]</c> with the window
    /// closing, and waits for the bestiary to appear. If it does not, the fill does not give up: it
    /// says so and carries on the moment the bestiary is opened by hand.
    /// </summary>
    private void OpenBestiary()
    {
        if (bestiaryWait < 0)
        {
            if (Fire(XbmColumns.PetParty.Addon, true, (AtkValueType.Int, XbmColumns.PetParty.OpenBestiaryCommand)))
            {
                bestiaryWait = 0;
                Status = "Team empty. Opening the bestiary…";
                return;
            }
        }
        else if (++bestiaryWait < FramesToAppear * 2)
        {
            return;
        }

        if (!askedForBestiary)
        {
            askedForBestiary = true;
            Tell($"Team emptied, but the bestiary did not open. Open it and the {plan.Count} beasts go in by themselves.");
        }
    }

    /// <summary>
    /// Whether the last toggle has shown up in the roster yet. False while it is still being waited
    /// for, and also when it has been given up on — <see cref="Stop"/> or <see cref="Full"/> has
    /// then already said why.
    /// </summary>
    private bool Confirmed(Step step)
    {
        if (InTeam(step.Beast) == step.Join)
        {
            waiting = null;
            return true;
        }

        if (++framesWaited < FramesToConfirm)
        {
            cooldown = 1;
            return false;
        }

        // A refused addition into a team that already holds something is the team being full: the
        // board takes fewer than the setting says. The plan is ordered, so what made it in is the
        // right team for that size, and this is a finish rather than a failure.
        if (step.Join && TeamNow().Count > 0)
        {
            Full(step.Beast);
            return false;
        }

        Stop($"The bestiary did not {(step.Join ? "add" : "remove")} {Name(step.Beast)} " +
             $"within {FramesToConfirm} frames.");
        return false;
    }

    /// <summary>
    /// The beast is on the other page. Turning it is a recorded click too, so this is not a dead
    /// end — the step goes back to the front of the queue and runs once the page has settled.
    /// </summary>
    private void TurnTo(Step step)
    {
        var page = XbmColumns.MonsterNotebook.PageOf(step.Beast);
        var showing = CurrentPage();

        if (showing < 0)
        {
            Stop($"The bestiary is open but says nothing about which page it is on, " +
                 $"so {Name(step.Beast)} cannot be found.");
            return;
        }

        if (page == showing)
        {
            Stop($"{Name(step.Beast)} should be on page {page + 1}, which the bestiary is already " +
                 $"showing, but none of its tiles carries icon {IconOf(step.Beast)}. " +
                 $"It is showing: {DescribeSlots()}");
            return;
        }

        if (!Send(XbmColumns.MonsterNotebook.TurnPageCommand, page))
        {
            Stop($"The bestiary would not turn to page {page + 1} for {Name(step.Beast)}.");
            return;
        }

        var requeued = new List<Step> { step };
        requeued.AddRange(pending);
        pending.Clear();
        foreach (var queued in requeued)
            pending.Enqueue(queued);

        cooldown = FramesAfterPageTurn;
    }

    // ---- Reading ---------------------------------------------------------

    private HashSet<uint> TeamNow() =>
        PetPartyReader.Read(catalog)
                      .Where(slot => slot.Beast != null)
                      .Select(slot => slot.Beast!.Number)
                      .ToHashSet();

    private bool InTeam(uint beastNumber) => TeamNow().Contains(beastNumber);

    /// <summary>Every string a window holds — for a menu, its entries; for a confirmation, its question.</summary>
    private static List<string> Strings(string addon) =>
        AddonReader.Values(addon)
                   .Where(value => value.Type.Contains("String", StringComparison.Ordinal) && value.Text.Length > 0)
                   .Select(value => value.Text)
                   .ToList();

    /// <summary>
    /// Which tile of the twenty-five currently shown holds this beast, or -1 when it is on another
    /// page. The window hands out an icon per slot and the icon is what identifies the beast.
    /// </summary>
    private int SlotOf(uint beastNumber)
    {
        var beast = catalog.Beasts.FirstOrDefault(b => b.Number == beastNumber);
        if (beast == null)
            return -1;

        var addon = AddonReader.Find(XbmColumns.MonsterNotebook.Addon);
        if (addon.IsNull)
            return -1;

        var values = addon.AtkValues.ToList();

        for (var slot = 0; slot < XbmColumns.MonsterNotebook.TileCount; slot++)
        {
            var index = XbmColumns.MonsterNotebook.SlotIconValue(slot);
            if (index >= values.Count)
                break;

            if (values[index].TryGet<uint>(out var icon) && icon == beast.IconId)
                return slot;
        }

        return -1;
    }

    /// <summary>
    /// Which page the bestiary is showing, read from the number in its first tile rather than
    /// remembered — the player can turn it too.
    /// </summary>
    private static int CurrentPage()
    {
        var addon = AddonReader.Find(XbmColumns.MonsterNotebook.Addon);
        if (addon.IsNull)
            return -1;

        var values = addon.AtkValues.ToList();
        var index = XbmColumns.MonsterNotebook.SlotNumberValue(0);

        if (index >= values.Count || !values[index].TryGet<uint>(out var number) || number == 0)
            return -1;

        return XbmColumns.MonsterNotebook.PageOf(number);
    }

    // ---- Sending ---------------------------------------------------------

    /// <summary>
    /// Sends one of the bestiary's own two-value commands. Both were recorded from real clicks:
    /// <c>[7, slot]</c> puts a beast in or out of the team, <c>[3, page]</c> turns the page.
    ///
    /// **The types matter.** The recordings read <c>[0] Int=n [1] UInt=n</c>: the command is an Int
    /// and its argument a UInt. Sending the argument as an Int is silently ignored.
    /// </summary>
    private static bool Send(int command, int argument) =>
        Fire(XbmColumns.MonsterNotebook.Addon, false,
             (AtkValueType.Int, command), (AtkValueType.UInt, argument));

    /// <summary>
    /// A callback with exactly the types a recording showed. Every window here is picky about them —
    /// the bestiary ignores an Int where it expects a UInt — so they are spelled out per value rather
    /// than assumed.
    ///
    /// <paramref name="close"/> is set for the menu and the confirmation: both go away once answered,
    /// and a menu left open after its entry was picked would sit over the team list.
    /// </summary>
    private static bool Fire(string addonName, bool close, params (AtkValueType Type, int Value)[] arguments)
    {
        if (!AddonReader.TryGet(addonName, out var addon))
            return false;

        var values = stackalloc AtkValue[arguments.Length];

        for (var i = 0; i < arguments.Length; i++)
        {
            values[i] = default;

            switch (arguments[i].Type)
            {
                case AtkValueType.Int:
                    values[i].SetInt(arguments[i].Value);
                    break;

                case AtkValueType.UInt:
                    values[i].SetUInt((uint)arguments[i].Value);
                    break;
            }
        }

        addon->FireCallback((uint)arguments.Length, values, close);
        return true;
    }

    // ---- Reporting -------------------------------------------------------

    /// <summary>
    /// What the bestiary's tiles actually hold. Written into the failure rather than left to be
    /// guessed at: "the icon is not there" and "the icons are not where I am looking" produce the
    /// same message otherwise, and they need different fixes.
    /// </summary>
    private static string DescribeSlots()
    {
        var addon = AddonReader.Find(XbmColumns.MonsterNotebook.Addon);
        if (addon.IsNull)
            return "(the window went away)";

        var values = addon.AtkValues.ToList();
        var seen = new List<string>();

        for (var slot = 0; slot < XbmColumns.MonsterNotebook.TileCount; slot++)
        {
            var index = XbmColumns.MonsterNotebook.SlotIconValue(slot);
            if (index >= values.Count)
            {
                seen.Add($"[{slot}] past the end of {values.Count} values");
                break;
            }

            seen.Add(values[index].TryGet<uint>(out var icon)
                         ? icon.ToString()
                         : $"[{slot}] {values[index].ValueType}");
        }

        return string.Join(", ", seen);
    }

    private uint IconOf(uint beastNumber) =>
        catalog.Beasts.FirstOrDefault(beast => beast.Number == beastNumber)?.IconId ?? 0;

    private string Name(uint beastNumber) =>
        catalog.Beasts.FirstOrDefault(beast => beast.Number == beastNumber)?.Name ?? $"beast {beastNumber}";

    /// <summary>
    /// Said in chat as well as in the Settings tab. The button is in the game's own window, so the
    /// answer to pressing it belongs where you are looking, not in a tab you would have to open.
    /// </summary>
    private void Tell(string message)
    {
        Status = message;
        Services.Log.Information(message);
        Services.Chat.Print($"[BeastMastr] {message}");
    }

    private void Full(uint refused)
    {
        var count = TeamNow().Count;
        Tell($"Team full at {count} — it would not take {Name(refused)}.");
        Reset();
    }

    private void Stop(string why)
    {
        Reset();
        givenUp = true;
        Tell($"{why} Press the button again to retry.");
    }

    private void Reset()
    {
        pending.Clear();
        phase = Phase.Idle;
        emptyStep = EmptyStep.OpenMenu;
        waiting = null;
        framesWaited = 0;
        cooldown = 0;
        askedForBestiary = false;
        bestiaryWait = -1;
        framesAway = 0;
        requested = false;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
