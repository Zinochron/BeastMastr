using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;

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
public sealed unsafe class EventRecorder : IDisposable
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

    private delegate bool FireCallbackDelegate(AtkUnitBase* addon, uint count, AtkValue* values, bool close);

    private readonly Hook<FireCallbackDelegate>? fireCallback;

    public EventRecorder()
    {
        foreach (var addon in Watched)
            Services.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, addon, OnReceiveEvent);

        // The input side alone was not enough: a click's event carries the kind, not the row. What
        // actually performs a selection is what the window then sends its agent, and that is
        // FireCallback — so it is watched too, and its values are what a replay has to reproduce.
        try
        {
            fireCallback = Services.Interop.HookFromAddress<FireCallbackDelegate>(
                AtkUnitBase.Addresses.FireCallback.Value, OnFireCallback);

            fireCallback.Enable();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not watch FireCallback; only input events will be recorded.");
        }
    }

    private bool OnFireCallback(AtkUnitBase* addon, uint count, AtkValue* values, bool close)
    {
        try
        {
            if (Recording && addon != null && addon->NameString.StartsWith("XBM", StringComparison.Ordinal))
                Record(addon->NameString, "FireCallback", (int)count, Describe(count, values));
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Recording a callback threw.");
        }

        return fireCallback!.Original(addon, count, values, close);
    }

    /// <summary>The values as sent, which is exactly what a replay needs to send back.</summary>
    private static string Describe(uint count, AtkValue* values)
    {
        if (values == null)
            return "(none)";

        var parts = new List<string>();
        for (var i = 0; i < count && i < 8; i++)
            parts.Add($"[{i}] {values[i].Type}={values[i].Int}");

        return string.Join(" ", parts);
    }

    public void Clear() => entries.Clear();

    private void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!Recording || args is not AddonReceiveEventArgs received)
            return;

        Record(args.AddonName, received.AtkEventType.ToString(), received.EventParam,
               $"event=0x{received.AtkEvent:X}");
    }

    /// <summary>Newest first: the click you just made is the one you are looking for.</summary>
    private void Record(string addon, string kind, int param, string detail)
    {
        entries.Insert(0, new Entry(DateTime.Now, addon, kind, param, detail));

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
        fireCallback?.Dispose();
        Services.AddonLifecycle.UnregisterListener(OnReceiveEvent);
        entries.Clear();
    }
}
