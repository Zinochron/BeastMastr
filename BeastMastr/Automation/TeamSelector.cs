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
/// Toggles tiles in the bestiary the way a click does — <c>[7, slot]</c>, recorded from a real
/// click, and deliberately not assumed from the fight window, whose own toggle is <c>[1, row]</c>.
/// The two windows do not share a command, which is the argument for recording each rather than
/// generalising from one.
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

    private enum Phase
    {
        Idle,

        /// <summary>Taking every beast out. Ends with a check that the roster really is empty.</summary>
        Emptying,

        /// <summary>Putting the plan in, carries first, then the least advanced.</summary>
        Filling,
    }

    /// <param name="Join">What this toggle is for. Stated, not inferred from the roster at the time —
    /// inferring it turned "remove" into "add" for a beast that had already gone.</param>
    private readonly record struct Step(uint Beast, bool Join);

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly RankWatcher ranks;

    private readonly Queue<Step> pending = new();
    private Phase phase;
    private List<uint> plan = [];
    private int cooldown;
    private Step? waiting;
    private int framesWaited;
    private bool givenUp;

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
        requested = true;
        givenUp = false;
        Reset();
    }

    /// <summary>
    /// Only while the bestiary and the roster are both up. That pairing happens when a team is being
    /// put together and at no other time, which beats matching a localised prompt.
    /// </summary>
    private static bool ComposingTeam =>
        AddonReader.IsOpen(XbmColumns.MonsterNotebook.Addon) && PetPartyReader.IsOpen;

    private void OnUpdate(IFramework framework)
    {
        if (givenUp)
            return;

        if (phase == Phase.Idle && !requested && configuration.TeamSelection != TeamMode.Leveling)
            return;

        if (!ComposingTeam)
        {
            Reset();
            return;
        }

        if (phase == Phase.Idle)
        {
            Start();
            return;
        }

        Continue();
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
            Status = "No ranks known yet, so there is nothing to sort on. Browse the bestiary once.";
            return;
        }

        var size = TeamPlanner.TeamSizeFor(configuration.BoardTier);

        // In the order it is to be added: carries first, then the least advanced. If the board takes
        // fewer than the setting says, what is left off is the end of this list, not the carries.
        plan = TeamPlanner.ForLeveling(known, size, configuration.CarryBeasts, current)
                          .Select(candidate => candidate.BeastNumber)
                          .ToList();

        if (plan.ToHashSet().SetEquals(current))
        {
            Status = $"Team already matches ({plan.Count} beasts).";
            return;
        }

        phase = Phase.Emptying;
        foreach (var beast in current)
            pending.Enqueue(new Step(beast, Join: false));

        Status = $"Emptying the team ({current.Count}), then adding {plan.Count} — " +
                 $"{known.Count} of {catalog.Beasts.Count} ranks known.";
        Services.Log.Information(Status);
    }

    private void Continue()
    {
        if (cooldown-- > 0)
            return;

        if (waiting is { } step && !Confirmed(step))
            return;

        if (pending.Count == 0)
        {
            NextPhase();
            return;
        }

        var next = pending.Dequeue();

        // Already the way it is meant to be — taken out by hand meanwhile, or never there.
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

    private void NextPhase()
    {
        if (phase == Phase.Emptying)
        {
            // The one check that makes emptying worth doing: nothing may be added until the window
            // itself says the team is empty.
            var left = TeamNow();
            if (left.Count > 0)
            {
                Stop($"Emptied the team, but the roster still lists {string.Join(", ", left.Select(Name))}.");
                return;
            }

            phase = Phase.Filling;
            foreach (var beast in plan)
                pending.Enqueue(new Step(beast, Join: true));

            Status = $"Team empty. Adding {plan.Count}…";
            return;
        }

        Status = $"Team set: {TeamNow().Count} beasts.";
        Services.Log.Information(Status);
        Reset();
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

    private HashSet<uint> TeamNow() =>
        PetPartyReader.Read(catalog)
                      .Where(slot => slot.Beast != null)
                      .Select(slot => slot.Beast!.Number)
                      .ToHashSet();

    private bool InTeam(uint beastNumber) => TeamNow().Contains(beastNumber);

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

    /// <summary>
    /// Sends one of the bestiary's own two-value commands. Both were recorded from real clicks:
    /// <c>[7, slot]</c> puts a beast in or out of the team, <c>[3, page]</c> turns the page.
    ///
    /// **The types matter.** The recordings read <c>[0] Int=n [1] UInt=n</c>: the command is an Int
    /// and its argument a UInt. Sending the argument as an Int is silently ignored.
    /// </summary>
    private static bool Send(int command, int argument)
    {
        if (!AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon))
            return false;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(command);
        values[1].SetUInt((uint)argument);

        addon->FireCallback(2, values);
        return true;
    }

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

    private void Full(uint refused)
    {
        var count = TeamNow().Count;
        Status = $"Team full at {count} — it would not take {Name(refused)}. The board takes {count}, " +
                 $"not the {TeamPlanner.TeamSizeFor(configuration.BoardTier)} the board tier setting says.";
        Services.Log.Information(Status);
        Reset();
    }

    private void Stop(string why)
    {
        Reset();
        givenUp = true;
        Status = $"{why} Press the button again to retry.";
        Services.Log.Warning(Status);
    }

    private void Reset()
    {
        pending.Clear();
        phase = Phase.Idle;
        waiting = null;
        framesWaited = 0;
        cooldown = 0;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
