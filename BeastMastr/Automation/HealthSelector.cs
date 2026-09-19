using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using Dalamud.Plugin.Services;

namespace BeastMastr.Automation;

/// <summary>
/// Picks the most hurt familiars in the team list's other jobs — who to feed at a shop, who rests at
/// a campsite — on its button, and at no other time.
///
/// Those windows allow different numbers of picks, and the limit is not something this needs to
/// know: it picks the most hurt first, one at a time, until the window refuses the next. What made
/// it in is then the right set for that window.
///
/// Two are never picked. One at full HP, because there is nothing to gain from it. And one at **zero
/// HP**, because it is not hurt, it is out: it cannot be rested or fed, and picking it would spend
/// one of the window's few picks on nothing.
///
/// "Most hurt" is the lowest share of HP left, and between equal shares the one missing more. A
/// share rather than a raw number, because a familiar with a large pool can have more HP left and
/// still be closer to going down.
/// </summary>
public sealed class HealthSelector : IDisposable
{
    /// <summary>Frames between two picks, so the window can answer one before the next.</summary>
    private const int FramesBetweenPicks = 6;

    /// <summary>How long a pick is given to show up before it counts as refused.</summary>
    private const int FramesToConfirm = 45;

    private readonly BeastCatalog catalog;

    private readonly Queue<uint> pending = new();
    private uint waitingFor;
    private int cooldown;
    private int framesWaited;
    private int picked;
    private bool requested;
    private bool overhealAware;

    public HealthSelector(BeastCatalog catalog)
    {
        this.catalog = catalog;
        Services.Framework.Update += OnUpdate;
    }

    public string Status { get; private set; } = string.Empty;

    /// <summary>Whether a pick is asked for or still under way.</summary>
    public bool Busy => requested || pending.Count > 0 || waitingFor != 0;

    /// <summary>The button.</summary>
    /// <param name="avoidOverheal">
    /// At a campsite, pick only as many as heal the most in total: the healing is shared, and what a
    /// share heals past full is lost (<see cref="Rules.CampsiteRest"/>).
    /// </param>
    public void RequestPick(bool avoidOverheal = false)
    {
        Reset();
        requested = true;
        overhealAware = avoidOverheal;
    }

    /// <summary>"You and 2 familiars can recover HP at this campsite." — the 2; 2 when the prompt does not say.</summary>
    private static int CampsiteLimit()
    {
        var match = System.Text.RegularExpressions.Regex.Match(PetPartyReader.Prompt(), @"(\d+) familiar");
        return match.Success && int.TryParse(match.Groups[1].Value, out var limit) ? limit : 2;
    }

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

        if (requested)
        {
            requested = false;
            Start(slots);
            return;
        }

        if (pending.Count > 0 || waitingFor != 0)
            Continue(slots);
    }

    private void Start(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        // Down, not hurt: a familiar at zero HP cannot be rested or fed, so it is left out of the
        // ordering entirely rather than sorted to the front of it.
        var down = slots.Count(slot => slot.MaxHp > 0 && slot.Hp <= 0);

        var hurt = slots.Where(slot => slot.Beast != null && slot.MaxHp > 0
                                       && slot.Hp > 0 && slot.Hp < slot.MaxHp && !slot.IsChosen)
                        .OrderBy(slot => slot.HealthShare)
                        .ThenByDescending(slot => slot.MaxHp - slot.Hp)
                        .ToList();

        if (hurt.Count == 0)
        {
            if (!slots.Any(slot => slot.MaxHp > 0))
                Tell("The team list shows no HP for anyone, so there is nothing to sort on.");
            else if (down > 0)
                Tell($"Nothing to pick: {down} familiar(s) are at 0 HP, which cannot be picked, and the rest are unhurt.");
            else
                Tell("Every familiar is at full HP — nothing to pick.");

            return;
        }

        if (overhealAware && PetPartyReader.Mode() == XbmColumns.PetParty.CampsiteMode)
        {
            var player = Services.Objects.LocalPlayer;
            var playerMissing = player is { MaxHp: > 0 } ? 1f - ((float)player.CurrentHp / player.MaxHp) : 0f;
            var count = Rules.CampsiteRest.HowMany(playerMissing, hurt.Select(slot => 1f - slot.HealthShare).ToList(),
                                                   CampsiteLimit());
            Services.Log.Information($"Campsite: resting with {count} familiar(s) heals the most — " +
                                     $"you are missing {playerMissing:P0}, the most hurt " +
                                     string.Join(", ", hurt.Take(3).Select(slot => $"{slot.Name} {1f - slot.HealthShare:P0}")) + ".");

            if (count == 0)
            {
                Tell("Resting alone heals the most: the familiars would mostly be overhealed.");
                return;
            }

            hurt = hurt.Take(count).ToList();
        }

        foreach (var slot in hurt)
            pending.Enqueue(slot.Beast!.Number);

        picked = 0;
        Status = down > 0
                     ? $"Picking the most hurt of {hurt.Count}, leaving out {down} at 0 HP…"
                     : $"Picking the most hurt of {hurt.Count}…";
        Services.Log.Information($"{Status} In order: " +
                                 string.Join(", ", hurt.Select(slot => $"{slot.Name} {slot.Hp}/{slot.MaxHp}")));
    }

    private void Continue(IReadOnlyList<PetPartyReader.Slot> slots)
    {
        if (cooldown-- > 0)
            return;

        if (waitingFor != 0)
        {
            if (slots.Any(slot => slot.Beast?.Number == waitingFor && slot.IsChosen))
            {
                picked++;
                waitingFor = 0;
            }
            else if (++framesWaited < FramesToConfirm)
            {
                cooldown = 1;
                return;
            }
            else
            {
                // A refusal after at least one pick is the window's limit, which is the expected way
                // for this to end. A refusal of the very first pick is something else, and says so.
                var refused = Name(slots, waitingFor);
                Finish(picked > 0
                           ? $"Picked the {picked} most hurt — the window would not take {refused} as well."
                           : $"The window did not take {refused}. If it was picked anyway, this window shows " +
                             "its picks somewhere other than where a fight's are — a capture of it would say where.");
                return;
            }
        }

        if (pending.Count == 0)
        {
            Finish($"Picked the {picked} most hurt.");
            return;
        }

        var next = pending.Dequeue();
        var row = slots.FirstOrDefault(slot => slot.Beast?.Number == next);

        if (row == null || row.IsChosen)
        {
            cooldown = 1;
            return;
        }

        if (!TeamListCommands.ToggleRow(row.Index))
        {
            Finish("The team list went away mid-selection.");
            return;
        }

        waitingFor = next;
        framesWaited = 0;
        cooldown = FramesBetweenPicks;
    }

    private static string Name(IEnumerable<PetPartyReader.Slot> slots, uint beastNumber) =>
        slots.FirstOrDefault(slot => slot.Beast?.Number == beastNumber)?.Name ?? $"beast {beastNumber}";

    private void Finish(string message)
    {
        Reset();
        Tell(message);
    }

    /// <summary>In chat: the button is in the game's window, so its answer belongs there.</summary>
    private void Tell(string message)
    {
        Status = message;
        Services.Log.Information(message);
        Services.Chat.Print($"[BeastMastr] {message}");
    }

    private void Reset()
    {
        pending.Clear();
        waitingFor = 0;
        cooldown = 0;
        framesWaited = 0;
        requested = false;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
