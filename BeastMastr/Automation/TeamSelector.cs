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

    /// <summary>Command the bestiary's own click sends. Recorded, not guessed.</summary>
    private const int ToggleCommand = 7;

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly RankWatcher ranks;

    private readonly Queue<uint> pending = new();
    private int cooldown;
    private uint waitingFor;
    private bool waitingToJoin;
    private bool givenUp;

    public TeamSelector(Configuration configuration, BeastCatalog catalog, RankWatcher ranks)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.ranks = ranks;

        Services.Framework.Update += OnUpdate;
    }

    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Only while the bestiary and the roster are both up. That pairing happens when a team is being
    /// put together and at no other time, which beats matching a localised prompt.
    /// </summary>
    private static bool ComposingTeam =>
        AddonReader.IsOpen(XbmColumns.MonsterNotebook.Addon) && PetPartyReader.IsOpen;

    private void OnUpdate(IFramework framework)
    {
        if (givenUp || configuration.TeamSelection != TeamMode.Leveling)
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
            Status = "No ranks known yet, so there is nothing to sort on. Browse the bestiary once.";
            return;
        }

        var wanted = TeamPlanner.ForLeveling(known, TeamPlanner.TeamSizeFor(configuration.BoardTier),
                                             configuration.CarryBeasts)
                                .Select(candidate => candidate.BeastNumber)
                                .ToHashSet();

        if (wanted.SetEquals(current))
        {
            Status = $"Team already matches ({wanted.Count} beasts).";
            return;
        }

        // Removals first: a team at its limit refuses additions, so making room has to come before
        // filling it.
        foreach (var beast in current.Except(wanted).Concat(wanted.Except(current)))
            pending.Enqueue(beast);

        Status = $"Adjusting {pending.Count} beast(s) — {known.Count} of {catalog.Beasts.Count} ranks known.";
        Services.Log.Information(Status);
    }

    private void Continue()
    {
        if (cooldown-- > 0)
            return;

        if (waitingFor != 0)
        {
            var inTeam = InTeam(waitingFor);
            if (inTeam != waitingToJoin)
            {
                Stop($"The bestiary did not {(waitingToJoin ? "add" : "remove")} {Name(waitingFor)}.");
                return;
            }

            waitingFor = 0;
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
            Stop($"{Name(next)} is not on the page the bestiary is showing. Turn the page and try again.");
            return;
        }

        if (!Toggle(slot))
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

    private static bool Toggle(int slot)
    {
        if (!AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon))
            return false;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(ToggleCommand);
        values[1].SetInt(slot);

        addon->FireCallback(2, values);
        return true;
    }

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
        cooldown = 0;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
