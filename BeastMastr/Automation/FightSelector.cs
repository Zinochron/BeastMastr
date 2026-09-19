using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Calls the same familiars into a fight as last time: once on its own as the fight window opens,
/// and again whenever its button is pressed.
///
/// The first automatic version checked every frame, and whenever nothing was called it called the
/// last fight's familiars again — so taking them out by hand put them straight back, and a choice of
/// your own had to be made against it. This one acts **once per opening of the window** and then
/// leaves it alone until the window closes. Whatever you change after that stays changed.
///
/// It sends the window the same callback a real click sends — recorded from an actual click rather
/// than guessed — and nothing is ever judged by a return value: after each pick the window is read
/// back, and a pick that did not take stops the run rather than pressing on.
/// </summary>
public sealed unsafe class FightSelector : IDisposable
{
    /// <summary>Frames between two picks. The window has to be given time to answer one before the next.</summary>
    private const int FramesBetweenPicks = 6;

    /// <summary>
    /// Frames after the fight window appears before the automatic call. Long enough for the game to
    /// put in anything it pre-fills itself, which the automatic call then leaves alone.
    /// </summary>
    private const int FramesBeforeAutomaticCall = 10;

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;

    private readonly Queue<uint> pending = new();
    private int cooldown;
    private uint waitingFor;
    private bool requested;

    /// <summary>Whether the request came from the window opening rather than the button.</summary>
    private bool automatic;

    /// <summary>Whether the team list was in fight mode last frame — how an opening is noticed.</summary>
    private bool wasFightWindow;

    /// <summary>Frames left before the automatic call, or -1 when none is due.</summary>
    private int automaticCountdown = -1;

    /// <summary>The team list's mode last frame, so each change is logged once.</summary>
    private uint? lastMode;

    public FightSelector(Configuration configuration, BeastCatalog catalog)
    {
        this.configuration = configuration;
        this.catalog = catalog;

        Services.Framework.Update += OnUpdate;
    }

    /// <summary>What the last attempt did, for the Settings tab to show. Never a silent failure.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>The button. Calls whatever of last time is missing, whatever is already called.</summary>
    public void RequestRepeat()
    {
        Reset();
        requested = true;
        automatic = false;
    }

    private bool Busy => pending.Count > 0 || waitingFor != 0;

    /// <summary>
    /// Whether familiars are being called, or are about to be: a pick outstanding, a press not yet
    /// handled, or the automatic call still counting down after the fight window opened.
    /// </summary>
    public bool IsCalling => Busy || requested || automaticCountdown >= 0;

    private void OnUpdate(IFramework framework)
    {
        if (!PetPartyReader.IsOpen)
        {
            Reset();
            wasFightWindow = false;
            automaticCountdown = -1;
            lastMode = null;
            return;
        }

        // The team list does several jobs in one window — building a team, calling a fight's
        // familiars, picking one to feed at a shop, picking who rests at a campsite — and its own
        // mode number says which. Only the fight is this one's: the first version took "not building
        // a team" to mean a fight, and called familiars at shops and campsites too.
        var mode = PetPartyReader.Mode();
        if (mode != lastMode)
        {
            lastMode = mode;
            Services.Log.Information($"Team list mode {mode}: {PetPartyReader.Describe()}");
        }

        // An opening is the moment the window turns into the fight window — exactly once per fight.
        var fightWindow = mode == XbmColumns.PetParty.FightMode;
        if (fightWindow && !wasFightWindow && configuration.CallLastFamiliarsOnOpen)
            automaticCountdown = FramesBeforeAutomaticCall;

        wasFightWindow = fightWindow;

        if (automaticCountdown >= 0 && --automaticCountdown < 0 && !requested && !Busy)
        {
            requested = true;
            automatic = true;
        }

        var slots = PetPartyReader.Read(catalog);
        if (slots.Count == 0)
            return;

        // While a call is running, what is called is what this is doing, not a choice of yours —
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
    /// Remembers what you call, in call order. Reading only — this is how "last time" is known.
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
        // On its own it only ever fills an empty choice. Anything already called — pre-filled by the
        // game, or picked in the few frames it waited — is a choice it does not second-guess.
        if (automatic && slots.Any(slot => slot.IsCalled))
        {
            Status = "The fight window opened with familiars already called; left as it was.";
            return;
        }

        // The carries come first, as long as they can fight; the rest of the call is last time's.
        var carries = configuration.CallCarriesFirst ? configuration.CarryBeasts : [];
        if (configuration.LastFightBeasts.Count == 0 && carries.Count == 0)
        {
            Say("Nothing remembered yet — call familiars by hand once, and the next fight repeats them.");
            return;
        }

        // An incapacitated familiar cannot be called, and trying stopped the whole call. Its place goes
        // to the healthiest familiar that is not already wanted.
        var able = slots.Where(slot => slot.Beast != null && !slot.IsDown).ToList();
        var candidates = able.Select(slot => new TeamPlanner.Candidate(slot.Beast!.Number, slot.Name, slot.Rank)).ToList();
        var wanted = TeamPlanner.ForFight(candidates, carries, configuration.LastFightBeasts).ToList();
        var count = Math.Min(TeamPlanner.FightSlots,
                             Math.Max(configuration.LastFightBeasts.Count,
                                      carries.Count(carry => slots.Any(slot => slot.Beast?.Number == carry))));

        var down = slots.Where(slot => slot.IsDown && slot.Beast != null &&
                                       (configuration.LastFightBeasts.Contains(slot.Beast.Number) ||
                                        carries.Contains(slot.Beast.Number)))
                        .Select(slot => slot.Name)
                        .ToList();

        foreach (var spare in able.Where(slot => !wanted.Contains(slot.Beast!.Number))
                                  .OrderByDescending(slot => slot.IsCalled)
                                  .ThenByDescending(slot => slot.HealthShare)
                                  .ThenByDescending(slot => slot.Rank))
        {
            if (wanted.Count >= count)
                break;

            wanted.Add(spare.Beast!.Number);
            Services.Log.Information($"Calling {spare.Name} in place of {(down.Count > 0 ? string.Join(", ", down) : "a familiar that is not here")}.");
        }

        if (wanted.Count == 0)
        {
            Say("None of the familiars from the last fight are in this team.");
            return;
        }

        // The callback toggles, so one already called is left alone rather than sent again.
        var missing = wanted.Where(beast => !slots.Any(slot => slot.Beast?.Number == beast && slot.IsCalled))
                            .ToList();

        if (missing.Count == 0)
        {
            Say("Already calling the same familiars as last time.");
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
            Say($"Called {slots.Count(slot => slot.IsCalled)} familiar(s) as last time.");
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

    private static bool Select(int index) => TeamListCommands.ToggleRow(index);

    private static string Name(IEnumerable<PetPartyReader.Slot> slots, uint beastNumber) =>
        slots.FirstOrDefault(slot => slot.Beast?.Number == beastNumber)?.Name ?? $"beast {beastNumber}";

    /// <summary>
    /// An answer to a press goes to chat, where you are looking. The automatic call only records it:
    /// once per fight, a line in chat saying that what always happens has happened is noise.
    /// </summary>
    private void Say(string message)
    {
        Status = message;
        Services.Log.Information(message);

        if (!automatic)
            Services.Chat.Print($"[BeastMastr] {message}");
    }

    /// <summary>Failures are said in chat either way — a call that did not happen is worth knowing about.</summary>
    private void Stop(string why)
    {
        Reset();

        Status = $"{why} Press the button to retry.";
        Services.Log.Warning(Status);
        Services.Chat.Print($"[BeastMastr] {Status}");
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
