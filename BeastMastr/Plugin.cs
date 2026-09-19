using System;
using System.Collections.Concurrent;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

namespace BeastMastr;

/// <summary>
/// The shell Dalamud loads. It makes sure only one copy of BeastMastr is at work, then builds
/// <see cref="PluginCore"/>.
///
/// Dalamud will load two copies side by side — the one from the repository and a dev build, say — and
/// warns only in its log. On 2026-09-19 at 14:37 both called the familiars into a fight: every pick is a
/// toggle, so each copy undid the other's, nobody was called, and the run stopped. So every copy puts
/// itself on a list in Dalamud's data share; exactly one is active — a dev build before an installed one,
/// then the higher version, then the one loaded first — and the others stay idle and say so in chat.
/// When a preferred copy arrives, the active one steps down first and the newcomer takes over after it.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string ShareTag = "BeastMastr.Instances";

    /// <summary>How often the list is looked at for a change of hands.</summary>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ConcurrentDictionary<string, string> instances;
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly string describe;
    private PluginCore? core;
    private DateTime nextCheck;
    private bool saidIdle;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        pluginInterface.Create<Services>();
        ECommonsMain.Init(pluginInterface, this);

        var version = pluginInterface.Manifest.AssemblyVersion?.ToString() ?? "0.0.0.0";
        describe = $"{(pluginInterface.IsDev ? "dev build" : "installed")} v{version}";

        instances = pluginInterface.GetOrCreateData(ShareTag, () => new ConcurrentDictionary<string, string>());
        instances[id] = Entry(pluginInterface.IsDev, version, DateTime.UtcNow.Ticks, false);

        Decide();
        Services.Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        if (DateTime.Now < nextCheck)
            return;

        nextCheck = DateTime.Now + CheckEvery;
        try
        {
            Decide();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Deciding which BeastMastr copy is active failed.");
        }
    }

    /// <summary>Builds the core when this copy should be at work and nobody else is; steps down when not.</summary>
    private void Decide()
    {
        var mine = instances.TryGetValue(id, out var entry) ? entry : null;
        if (mine == null)
            return;

        var winner = instances.OrderByDescending(pair => Rank(pair.Value))
                              .ThenBy(pair => Loaded(pair.Value))
                              .First().Key;

        if (winner != id)
        {
            if (core != null)
            {
                core.Dispose();
                core = null;
                SetActive(false);
                Services.Chat.Print($"[BeastMastr] Another copy of BeastMastr was loaded and takes over; this " +
                                    $"one ({describe}) is now idle. Disable one of them in the plugin installer.");
            }

            if (!saidIdle)
            {
                saidIdle = true;
                Services.Log.Warning($"BeastMastr is loaded more than once; this copy ({describe}) stays idle.");
                Services.Chat.Print($"[BeastMastr] BeastMastr is loaded twice. This copy ({describe}) stays idle " +
                                    "so the two do not undo each other's clicks. Disable one of them in the " +
                                    "plugin installer.");
            }

            return;
        }

        // The winner waits until whoever was at work before has let go of the commands and windows.
        if (core != null || instances.Any(pair => pair.Key != id && IsActive(pair.Value)))
            return;

        core = new PluginCore(pluginInterface);
        SetActive(true);
        if (instances.Count > 1)
            Services.Log.Warning($"BeastMastr is loaded more than once; this copy ({describe}) is the active one.");
    }

    private void SetActive(bool active)
    {
        if (instances.TryGetValue(id, out var entry))
            instances[id] = entry[..entry.LastIndexOf('|')] + (active ? "|1" : "|0");
    }

    /// <summary>"dev|version|loaded ticks|active", in a type every copy's load context shares.</summary>
    private static string Entry(bool dev, string version, long loaded, bool active) =>
        $"{(dev ? 1 : 0)}|{version}|{loaded}|{(active ? 1 : 0)}";

    private static (int Dev, Version Version) Rank(string entry)
    {
        var parts = entry.Split('|');
        return (parts[0] == "1" ? 1 : 0, Version.TryParse(parts[1], out var version) ? version : new Version(0, 0));
    }

    private static long Loaded(string entry) => long.TryParse(entry.Split('|')[2], out var ticks) ? ticks : 0;

    private static bool IsActive(string entry) => entry.EndsWith("|1", StringComparison.Ordinal);

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;

        core?.Dispose();
        core = null;

        instances.TryRemove(id, out _);
        pluginInterface.RelinquishData(ShareTag);

        ECommonsMain.Dispose();
    }
}
