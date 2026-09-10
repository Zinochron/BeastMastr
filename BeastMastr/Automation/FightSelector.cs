using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Calls the same familiars into a fight as last time — when its button is pressed, and at no other
/// time. The first version did it on its own the moment the window opened, which meant the window
/// acted before you did and a choice of your own had to be made against it.
///
/// It sends the window the same callback a real click sends — recorded from an actual click rather
/// than guessed — and nothing is ever judged by a return value: after each pick the window is read
/// back, and a pick that did not take stops the run rather than pressing on.
/// </summary>
public sealed unsafe class FightSelector : IDisposable
{
    /// <summary>Frames between two picks. The window has to be given time to answer one before the next.</summary>
    private const int FramesBetweenPicks = 6;

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;

    private readonly Queue<uint> pending = new();
    private int cooldown;
    private uint waitingFor;
    private bool requested;

    public FightSelector(Configuration configuration, BeastCatalog catalog)
    {
        this.configuration = configuration;
        this.catalog = catalog;

        Services.Framework.Update += OnUpdate;
    }

    /// <summary>What the last press did, for the Settings tab to show. Never a silent failure.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>The button. The only way anything here starts.</summary>
    public void RequestRepeat()
    {
        Reset();
        requested = true;
    }

    private bool Busy => pending.Count > 0 || waitingFor != 0;

    private void OnUpdate(IFramework framework)
    {
        if (!PetPartyReader.IsOpen)
        {
            Reset();
            return;
        }

        var slots = PetPartyReader.Read(catalog);
        if (slots.Count == 0)
            return;

        // While a repeat is running, what is called is what this is doing, not a choice of yours —
        // remembering it then would store half a selection if a pick fails partway.
        if (!Busy)
            Learn(slots);

        if (requested)
        {
            requested = false;
            Start(slots);
            return;
        }

        if (Busy)
            Continue(slots);
    }

    /// <summary>
    /// Remembers what you call by hand, in call order. Reading only — this is how the button knows
    /// what "last time" was.
    /// </summary>
    private void Learn(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        // Replacing one familiar reuses the freed slot rather than shifting the others up, so the
        // slot number is the order and the list order is not.
        var chosen = slots.Where(slot => slot.IsCalled && slot.Beast != null)
                          .OrderBy(slot => slot.CallSlot)
                          .Select(slot => slot.Beast!.Number)
                          .ToList();

        if (chosen.Count == 0 || configuration.LastFightBeasts.SequenceEqual(chosen))
            return;

        configuration.LastFightBeasts = chosen;
        configuration.Save();
    }

    private void Start(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        if (configuration.LastFightBeasts.Count == 0)
        {
            Tell("Nothing remembered yet — call familiars by hand once, and the next press repeats them.");
            return;
        }

        var wanted = TeamPlanner.RepeatLast(
            slots.Where(slot => slot.Beast != null)
                 .Select(slot => new TeamPlanner.Candidate(slot.Beast!.Number, slot.Name, slot.Rank)),
            configuration.LastFightBeasts,
            configuration.LastFightBeasts.Count);

        if (wanted.Count == 0)
        {
            Tell("None of the familiars from the last fight are in this team.");
            return;
        }

        // The callback toggles, so one already called is left alone rather than sent again.
        var missing = wanted.Where(beast => !slots.Any(slot => slot.Beast?.Number == beast && slot.IsCalled))
                            .ToList();

        if (missing.Count == 0)
        {
            Tell("Already calling the same familiars as last time.");
            return;
        }

        foreach (var beast in missing)
            pending.Enqueue(beast);

        Status = $"Calling {missing.Count} familiar(s) as last time.";
        Services.Log.Information(Status);
    }

    private void Continue(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        if (cooldown-- > 0)
            return;

        // Confirm the previous pick took before asking for the next one.
        if (waitingFor != 0)
        {
            if (!slots.Any(slot => slot.Beast?.Number == waitingFor && slot.IsCalled))
            {
                Stop($"The window did not take {Name(slots, waitingFor)}; stopping and leaving the rest to you.");
                return;
            }

            waitingFor = 0;
        }

        if (pending.Count == 0)
        {
            Tell($"Called {slots.Count(slot => slot.IsCalled)} familiar(s) as last time.");
            return;
        }

        var next = pending.Dequeue();
        var slot = slots.FirstOrDefault(candidate => candidate.Beast?.Number == next);

        if (slot == null)
        {
            Stop("A familiar left the team mid-selection; stopping.");
            return;
        }

        if (slot.IsCalled)
        {
            cooldown = 1;
            return;
        }

        if (!Select(slot.Index))
        {
            Stop("The team list went away mid-selection; stopping.");
            return;
        }

        waitingFor = next;
        cooldown = FramesBetweenPicks;
    }

    /// <summary>Command the window's own click sends. Recorded, not guessed.</summary>
    private const int ToggleCommand = 1;

    /// <summary>
    /// Sends what clicking that row sends. A real click on the first familiar fires
    /// <c>FireCallback</c> with <c>[1, 0]</c> — command then row — and selecting and deselecting
    /// fire exactly the same thing, so it is a toggle rather than a set.
    ///
    /// **The types matter.** The recording reads <c>[0] Int=1 [1] UInt=0</c>: the command is an Int
    /// and the row is a UInt. Sending the row as an Int too was silently ignored — nothing errored,
    /// the window simply never took the familiar, which is what the read-back kept reporting.
    /// </summary>
    private static bool Select(int index)
    {
        if (index < 0 || !AddonReader.TryGet(XbmColumns.PetParty.Addon, out var addon))
            return false;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(ToggleCommand);
        values[1].SetUInt((uint)index);

        addon->FireCallback(2, values);
        return true;
    }

    private static string Name(IEnumerable<PetPartyReader.Slot> slots, uint beastNumber) =>
        slots.FirstOrDefault(slot => slot.Beast?.Number == beastNumber)?.Name ?? $"beast {beastNumber}";

    /// <summary>In chat as well: the button is in the game's window, so its answer belongs there.</summary>
    private void Tell(string message)
    {
        Status = message;
        Services.Log.Information(message);
        Services.Chat.Print($"[BeastMastr] {message}");
    }

    private void Stop(string why)
    {
        Reset();
        Tell($"{why} Press the button again to retry.");
    }

    private void Reset()
    {
        pending.Clear();
        waitingFor = 0;
        cooldown = 0;
        requested = false;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
