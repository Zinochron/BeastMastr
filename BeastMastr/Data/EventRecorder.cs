using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace BeastMastr.Data;

/// <summary>
/// Records what the game sends a Beastmaster window when you click in it.
///
/// Selecting a beast has to be done by sending the window the same thing your click sends, and this
/// plugin's neighbours learned the hard way that hand-built AtkValue payloads are undocumented,
/// version specific and quietly wrong — LootMastr's notes say so outright. So nothing is guessed:
/// the click is recorded first, and only then replayed.
///
/// Watching only, never sending. It changes nothing.
/// </summary>
public sealed class EventRecorder : IDisposable
{
    private const int Capacity = 60;

    /// <summary>The windows worth listening to. All of them are ones a selection happens in.</summary>
    private static readonly string[] Watched =
    [
        XbmColumns.PetParty.Addon,
        XbmColumns.MonsterNotebook.Addon,
        XbmColumns.StageDetailList.Addon,
    ];

    public sealed record Entry(DateTime At, string Addon, string EventType, int EventParam, string Detail);

    private readonly List<Entry> entries = [];

    public bool Recording { get; set; }

    public IReadOnlyList<Entry> Entries => entries;

    public EventRecorder()
    {
        foreach (var addon in Watched)
            Services.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, addon, OnReceiveEvent);
    }

    public void Clear() => entries.Clear();

    private void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!Recording || args is not AddonReceiveEventArgs received)
            return;

        // Newest first: the click you just made is the one you are looking for.
        entries.Insert(0, new Entry(DateTime.Now,
                                    args.AddonName,
                                    received.AtkEventType.ToString(),
                                    received.EventParam,
                                    $"event=0x{received.AtkEvent:X}"));

        if (entries.Count > Capacity)
            entries.RemoveRange(Capacity, entries.Count - Capacity);
    }

    public string Report() =>
        entries.Count == 0
            ? "(nothing recorded)"
            : string.Join(Environment.NewLine,
                          entries.Select(e => $"{e.At:HH:mm:ss.fff}\t{e.Addon}\t{e.EventType}\tparam={e.EventParam}\t{e.Detail}"));

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(OnReceiveEvent);
        entries.Clear();
    }
}
