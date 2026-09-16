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
    /// <summary>
    /// Deliberately large. Moving the cursor across a grid of tiles produces two events and two
    /// callbacks per tile crossed, so a small buffer fills with mouse movement in under a second and
    /// pushes out the click that was being looked for — which is exactly what happened the first
    /// time this was used on the bestiary.
    /// </summary>
    private const int Capacity = 400;

    /// <summary>
    /// Events that are only ever the cursor passing over something. They are never the thing being
    /// hunted, and they arrive in their hundreds.
    /// </summary>
    private static readonly string[] Noise =
    [
        "MouseOver", "MouseOut", "MouseMove",
        "ListItemRollOver", "ListItemRollOut",
        "ButtonRollOver", "ButtonRollOut",
    ];

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

    /// <summary>
    /// Every entry as it is recorded, for the run recorder to weave into its timeline. Raised only
    /// while <see cref="Recording"/> is on, like the list itself.
    /// </summary>
    public event Action<Entry>? Recorded;

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

    /// <summary>When the cursor last moved over something, so its callbacks can be set aside.</summary>
    private DateTime lastNoiseAt = DateTime.MinValue;

    /// <summary>
    /// The bestiary tile the cursor is on, or -1. Tracked whether or not anything is being recorded,
    /// because a right click has to know which beast it landed on and the window says so nowhere
    /// else — <c>[5, slot]</c> is what it is told when the cursor enters a tile.
    /// </summary>
    public int HoveredNotebookSlot { get; private set; } = -1;

    private void TrackHover(AtkUnitBase* addon, uint count, AtkValue* values)
    {
        if (addon->NameString != XbmColumns.MonsterNotebook.Addon || values == null)
            return;

        if (count == 1 && values[0].Int == 6)
        {
            HoveredNotebookSlot = -1;
            return;
        }

        if (count == 2 && values[0].Int == XbmColumns.MonsterNotebook.HoverCommand)
            HoveredNotebookSlot = values[1].Int;
    }

    private bool OnFireCallback(AtkUnitBase* addon, uint count, AtkValue* values, bool close)
    {
        try
        {
            if (addon != null)
                TrackHover(addon, count, values);

            if (Recording && addon != null && Worth(addon->NameString))
            {
                // A window answers a cursor crossing a tile with a callback of its own. Those are
                // marked rather than dropped: they are noise for finding a click and evidence for
                // anything else.
                var fromHover = (DateTime.Now - lastNoiseAt).TotalMilliseconds < 4;
                Record(addon->NameString, fromHover ? "FireCallback (hover)" : "FireCallback",
                       (int)count, Describe(count, values) + (close ? "  (closes)" : string.Empty));
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Recording a callback threw.");
        }

        return fireCallback!.Original(addon, count, values, close);
    }

    /// <summary>
    /// Everything, while recording is on.
    ///
    /// It used to keep only the Beastmaster windows, and that threw away the one click being hunted
    /// twice over: a right-click menu is a window of its own called <c>ContextMenu</c>, and a
    /// confirmation is <c>SelectYesno</c>. Both had to be added by name after the recording came
    /// back empty. Guessing which windows matter is the mistake; recording is switched on
    /// deliberately, for a few seconds, and the report says which window each line came from.
    /// </summary>
    private static bool Worth(string addon) => addon.Length > 0;

    /// <summary>
    /// The values as sent, which is exactly what a replay needs to send back.
    ///
    /// Each one is printed as what it is. Printing <c>.Int</c> for all of them turned a string into
    /// the low half of its pointer and a bool into whatever the other bytes held, which reads like a
    /// number the game sent and is not one.
    /// </summary>
    private static string Describe(uint count, AtkValue* values)
    {
        if (values == null)
            return "(none)";

        var parts = new List<string>();
        for (var i = 0; i < count && i < 8; i++)
            parts.Add($"[{i}] {values[i].Type}={ValueText(&values[i])}");

        return string.Join(" ", parts);
    }

    private static string ValueText(AtkValue* value)
    {
        try
        {
            return value->Type switch
            {
                AtkValueType.Int => value->Int.ToString(),
                AtkValueType.UInt => value->UInt.ToString(),
                AtkValueType.Bool => (value->Byte != 0).ToString(),
                AtkValueType.Undefined or AtkValueType.Null => "-",
                _ => $"\"{new Dalamud.Game.NativeWrapper.AtkValuePtr((nint)value).GetValue()}\"",
            };
        }
        catch (Exception)
        {
            return "?";
        }
    }

    public void Clear() => entries.Clear();

    private void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!Recording || args is not AddonReceiveEventArgs received)
            return;

        var kind = received.AtkEventType.ToString();
        if (Noise.Contains(kind))
        {
            // Remembered only so the callbacks it drags along can be told from real ones.
            lastNoiseAt = DateTime.Now;
            return;
        }

        Record(args.AddonName, kind, received.EventParam, $"event=0x{received.AtkEvent:X}");
    }

    /// <summary>Newest first: the click you just made is the one you are looking for.</summary>
    private void Record(string addon, string kind, int param, string detail)
    {
        var entry = new Entry(DateTime.Now, addon, kind, param, detail);
        entries.Insert(0, entry);
        Recorded?.Invoke(entry);

        if (entries.Count > Capacity)
            entries.RemoveRange(Capacity, entries.Count - Capacity);
    }

    public string Report() =>
        entries.Count == 0
            ? "(nothing recorded)"
            : string.Join(Environment.NewLine,
                          entries.Select(e => $"{e.At:HH:mm:ss.fff}\t{e.Addon}\t{e.EventType}\tparam={e.EventParam}\t{e.Detail}"));

    /// <summary>Just the callbacks a cursor crossing did not cause — which is where a click will be.</summary>
    public IEnumerable<Entry> WithoutHover() =>
        entries.Where(entry => !entry.EventType.EndsWith("(hover)", StringComparison.Ordinal));

    public void Dispose()
    {
        fireCallback?.Dispose();
        Services.AddonLifecycle.UnregisterListener(OnReceiveEvent);
        entries.Clear();
    }
}
