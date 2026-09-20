using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace BeastMastr.Data;

/// <summary>
/// Watches every <c>SelectString</c> the game puts up, from the frame it is set up.
///
/// A window that is opened and answered by something else can come and go between two framework
/// ticks, and a run polling per frame then swears it never existed. That is the difference between
/// "talking to Lauda did nothing" and "something else answered her for you" — the first is a bug to
/// chase, the second is a plugin to switch off, and only the lifecycle can tell them apart.
///
/// Watching only: it answers nothing and changes nothing.
/// </summary>
public sealed class MenuWatch : IDisposable
{
    /// <summary>The one watcher, so any step can ask what the game did without being handed it.</summary>
    public static MenuWatch? Instance { get; private set; }

    /// <summary>Where a <c>SelectString</c>'s choices start among its values; see <c>XbmColumns.Entrance</c>.</summary>
    private const int FirstEntry = 7;

    public MenuWatch()
    {
        Instance = this;
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, XbmColumns.Entrance.Menu, OnSetup);
    }

    /// <summary>When a menu was last put up, or <see cref="DateTime.MinValue"/> before the first.</summary>
    public DateTime LastSetupAt { get; private set; } = DateTime.MinValue;

    /// <summary>How many have been put up in all, for telling one from the next.</summary>
    public int Setups { get; private set; }

    /// <summary>What the last one offered.</summary>
    public IReadOnlyList<string> LastEntries { get; private set; } = [];

    private void OnSetup(AddonEvent type, AddonArgs args)
    {
        LastSetupAt = DateTime.Now;
        Setups++;
        LastEntries = Entries();
    }

    private static List<string> Entries()
    {
        var entries = new List<string>();
        var values = AddonReader.Values(XbmColumns.Entrance.Menu);
        for (var choice = 0; FirstEntry + choice < values.Count; choice++)
        {
            var value = values[FirstEntry + choice];
            if (!value.Type.Contains("String"))
                break;

            entries.Add(value.Text);
        }

        return entries;
    }

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(OnSetup);
        if (Instance == this)
            Instance = null;
    }
}
