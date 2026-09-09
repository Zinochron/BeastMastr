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

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly RankWatcher ranks;

    private readonly Queue<uint> pending = new();
    private int cooldown;
    private uint waitingFor;
    private bool waitingToJoin;
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
        if (givenUp || (!requested && configuration.TeamSelection != TeamMode.Leveling))
            return;

        if (!ComposingTeam)
        {
            Reset();
            return;
        }

        if (pending.Count > 0)
        {
            Continue();
            return;
        }

        Start();
    }

    private void Start()
    {
        var current = PetPartyReader.Read(catalog)
                                    .Where(slot => slot.Beast != null)
                                    .Select(slot => slot.Beast!.Number)
                                    .ToHashSet();

        var known = catalog.Beasts
                           .Select(beast => (beast, rank: ranks.RankOf(beast.Number)))
                           .Where(pair => pair.rank is not null)
                           .Select(pair => new TeamPlanner.Candidate(pair.beast.Number, pair.beast.Name, pair.rank!.Value))
                           .ToList();

        if (known.Count == 0)
        {
            requested = false;
            Status = "No ranks known yet, so there is nothing to sort on. Browse the bestiary once.";
            return;
        }

        // The board tier is a setting, and a setting can be wrong. The roster window lists one row
        // per slot the team has, so it knows the real size — and asking for more than that is how a
        // fill ends with the last few refused and no explanation.
        var slots = PetPartyReader.Read(catalog).Count;
        var configured = TeamPlanner.TeamSizeFor(configuration.BoardTier);
        var size = slots > 0 ? Math.Min(configured, slots) : configured;

        var wanted = TeamPlanner.ForLeveling(known, size, configuration.CarryBeasts, current)
                                .Select(candidate => candidate.BeastNumber)
                                .ToHashSet();

        if (wanted.SetEquals(current))
        {
            requested = false;
            Status = $"Team already matches ({wanted.Count} beasts).";
            return;
        }

        // Removals first: a team at its limit refuses additions, so making room has to come before
        // filling it.
        foreach (var beast in current.Except(wanted).Concat(wanted.Except(current)))
            pending.Enqueue(beast);

        requested = false;

        Status = size < configured
                     ? $"Adjusting {pending.Count} beast(s) for {size} slots — the board offers {slots}, " +
                       $"not the {configured} the setting asks for."
                     : $"Adjusting {pending.Count} beast(s) — {known.Count} of {catalog.Beasts.Count} ranks known.";
        Services.Log.Information(Status);
    }

    private void Continue()
    {
        if (cooldown-- > 0)
            return;

        if (waitingFor != 0)
        {
            if (InTeam(waitingFor) != waitingToJoin)
            {
                if (++framesWaited < FramesToConfirm)
                {
                    cooldown = 1;
                    return;
                }

                Stop($"The bestiary did not {(waitingToJoin ? "add" : "remove")} {Name(waitingFor)} " +
                     $"within {FramesToConfirm} frames.");
                return;
            }

            waitingFor = 0;
            framesWaited = 0;
        }

        if (pending.Count == 0)
        {
            Status = "Team set.";
            return;
        }

        var next = pending.Dequeue();
        var joining = !InTeam(next);
        var slot = SlotOf(next);

        if (slot < 0)
        {
            // On another page. Turning it is a recorded click too, so this is not a dead end.
            var page = XbmColumns.MonsterNotebook.PageOf(next);
            var showing = CurrentPage();

            if (showing < 0)
            {
                Stop($"The bestiary is open but says nothing about which page it is on, " +
                     $"so {Name(next)} cannot be found.");
                return;
            }

            if (page == showing)
            {
                Stop($"{Name(next)} should be on page {page + 1}, which the bestiary is already " +
                     $"showing, but none of its tiles carries icon {IconOf(next)}. " +
                     $"It is showing: {DescribeSlots()}");
                return;
            }

            if (!Send(XbmColumns.MonsterNotebook.TurnPageCommand, page))
            {
                Stop($"The bestiary would not turn to page {page + 1} for {Name(next)}.");
                return;
            }

            // Put it back at the front: the page has to settle before the tile exists to click.
            var requeued = new List<uint> { next };
            requeued.AddRange(pending);
            pending.Clear();
            foreach (var beast in requeued)
                pending.Enqueue(beast);

            cooldown = FramesAfterPageTurn;
            return;
        }

        if (!Send(XbmColumns.MonsterNotebook.ToggleTeamCommand, slot))
        {
            Stop("The bestiary went away mid-selection.");
            return;
        }

        waitingFor = next;
        waitingToJoin = joining;
        cooldown = FramesBetweenToggles;
    }

    private bool InTeam(uint beastNumber) =>
        PetPartyReader.Read(catalog).Any(slot => slot.Beast?.Number == beastNumber);

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

    private void Stop(string why)
    {
        Reset();
        givenUp = true;
        Status = $"{why} Not trying again until the plugin reloads.";
        Services.Log.Warning(Status);
    }

    private void Reset()
    {
        pending.Clear();
        waitingFor = 0;
        framesWaited = 0;
        cooldown = 0;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
