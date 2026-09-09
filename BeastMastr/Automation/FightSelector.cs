using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Calls the same familiars into a fight as last time.
///
/// The only place in this plugin that changes game state. It sends the window the same callback a
/// real click sends — recorded from an actual click rather than guessed — and nothing is ever
/// judged by a return value: after each pick the window is read back, and a pick that did not take
/// stops the run rather than pressing on. That rule is Sortr's, learned there the hard way.
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

    /// <summary>
    /// Set when a pick did not take, and never cleared while the plugin is loaded. Retrying a
    /// selection that does not work is not harmless — it moves the highlight under your hands every
    /// time the window opens — and one failure is enough to know the mechanism is wrong.
    /// </summary>
    private bool givenUp;

    public FightSelector(Configuration configuration, BeastCatalog catalog)
    {
        this.configuration = configuration;
        this.catalog = catalog;

        Services.Framework.Update += OnUpdate;
    }

    /// <summary>What the last attempt did, for the Settings tab to show. Never a silent failure.</summary>
    public string Status { get; private set; } = string.Empty;

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

        Learn(slots);

        if (pending.Count > 0)
        {
            Continue(slots);
            return;
        }

        Start(slots);
    }

    /// <summary>
    /// Which window is which, learned rather than matched. The prompt differs between choosing a
    /// run's team and choosing a fight's familiars, but it is localised, so hardcoding either
    /// sentence would work in one client and quietly misfire in every other. Instead: the first
    /// time familiars are actually called, whatever the window said at that moment *is* the fight
    /// prompt. Sortr learns its retainer menu entries the same way and for the same reason.
    /// </summary>
    private void Learn(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        var called = slots.Where(slot => slot.IsCalled).OrderBy(slot => slot.CallSlot).ToList();
        if (called.Count == 0)
            return;

        var prompt = PetPartyReader.Prompt();
        if (prompt.Length > 0 && configuration.FightPrompt != prompt)
        {
            configuration.FightPrompt = prompt;
            configuration.Save();
            Services.Log.Information($"Learned the fight prompt: \"{prompt}\"");
        }

        // Remember in call order. Replacing one familiar reuses the freed slot rather than shifting
        // the others up, so the slot number is the order and the list order is not.
        var chosen = called.Where(slot => slot.Beast != null)
                           .Select(slot => slot.Beast!.Number)
                           .ToList();

        if (chosen.Count == 0 || configuration.LastFightBeasts.SequenceEqual(chosen))
            return;

        configuration.LastFightBeasts = chosen;
        configuration.Save();
    }

    private void Start(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        if (givenUp || configuration.FightSelection != FightMode.RepeatLast)
            return;

        // Only ever on the window that asks for a fight's familiars, and only one it recognises.
        if (configuration.FightPrompt.Length == 0 || PetPartyReader.Prompt() != configuration.FightPrompt)
            return;

        // Somebody is already called — either the game pre-filled it or you are mid-choice. Leave it.
        if (slots.Any(slot => slot.IsCalled))
            return;

        var wanted = TeamPlanner.RepeatLast(
            slots.Where(slot => slot.Beast != null)
                 .Select(slot => new TeamPlanner.Candidate(slot.Beast!.Number, slot.Name, slot.Rank)),
            configuration.LastFightBeasts,
            configuration.LastFightBeasts.Count);

        if (wanted.Count == 0)
            return;

        foreach (var beast in wanted)
            pending.Enqueue(beast);

        Status = $"Calling {wanted.Count} familiar(s) as last time.";
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
            Status = "Done.";
            return;
        }

        var next = pending.Dequeue();
        var slot = slots.FirstOrDefault(candidate => candidate.Beast?.Number == next);

        if (slot == null)
        {
            Stop("A familiar left the roster mid-selection; stopping.");
            return;
        }

        // The callback toggles, so sending it for one already called would take it back out.
        if (slot.IsCalled)
        {
            cooldown = 1;
            return;
        }

        if (!Select(slot.Index))
        {
            Stop("The roster window went away mid-selection; stopping.");
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
