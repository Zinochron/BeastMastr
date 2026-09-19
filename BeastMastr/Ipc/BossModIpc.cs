using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastMastr.Ipc;

/// <summary>
/// BossMod, over its IPC. The signatures were read off the installed plugin (7.5.6.5) with reflection;
/// every one of these returns a value, including the ones that only change something.
///
/// Like <see cref="NavmeshIpc"/>, nothing here throws: a failed call returns the empty value and leaves
/// the reason in <see cref="LastError"/>.
/// </summary>
public static class BossModIpc
{
    public const string InternalName = "BossMod";
    private const string Prefix = "BossMod.";

    public static string LastError { get; private set; } = string.Empty;

    public static bool IsLoaded => PluginPresence.IsLoaded(InternalName);

    public static string Version => PluginPresence.Version(InternalName);

    /// <summary>A preset as BossMod stores it, or null when there is none by that name.</summary>
    public static string? GetPreset(string name) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<string, string?>(Prefix + "Presets.Get").InvokeFunc(name),
             "Presets.Get", null);

    public static bool CreatePreset(string json, bool overwrite) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<string, bool, bool>(Prefix + "Presets.Create")
                           .InvokeFunc(json, overwrite), "Presets.Create", false);

    /// <summary>The presets active right now, or null when that could not be asked.</summary>
    public static List<string>? GetActiveList() =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<List<string>>(Prefix + "Presets.GetActiveList")
                           .InvokeFunc(), "Presets.GetActiveList", null);

    public static bool SetActiveList(List<string> names) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<List<string>, bool>(Prefix + "Presets.SetActiveList")
                           .InvokeFunc(names), "Presets.SetActiveList", false);

    /// <summary>
    /// Builds a map of what can be stood on around a point, for BossMod's own pathfinding. The Crucible
    /// has none shipped, and without one BossMod may walk off a platform.
    /// </summary>
    public static bool GenerateObstacleMap(Vector3 centre, float radius) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<Vector3, float, bool, bool>(Prefix + "ObstacleMap.Generate")
                           .InvokeFunc(centre, radius, false), "ObstacleMap.Generate", false);

    /// <summary>Sets one track of a module in a preset for now, without saving it to the preset.</summary>
    public static bool AddTransientStrategy(string preset, string module, string track, string value) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<string, string, string, string, bool>(Prefix + "Presets.AddTransientStrategy")
                           .InvokeFunc(preset, module, track, value), "Presets.AddTransientStrategy", false);

    /// <summary>Takes back what <see cref="AddTransientStrategy"/> set.</summary>
    public static bool ClearTransientStrategy(string preset, string module, string track) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<string, string, string, bool>(Prefix + "Presets.ClearTransientStrategy")
                           .InvokeFunc(preset, module, track), "Presets.ClearTransientStrategy", false);

    /// <summary>Whether BossMod has actions of its own queued.</summary>
    public static bool HasQueuedActions() =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<bool>(Prefix + "Rotation.ActionQueue.HasEntries")
                           .InvokeFunc(), "Rotation.ActionQueue.HasEntries", false);

    private static T Call<T>(Func<T> call, string name, T fallback)
    {
        try
        {
            var result = call();
            LastError = string.Empty;
            return result;
        }
        catch (Exception ex)
        {
            var reason = IsLoaded ? $"{name} failed: {ex.GetType().Name} {ex.Message}" : "BossMod is not loaded";
            if (reason != LastError)
                Services.Log.Warning($"BossMod: {reason}");

            LastError = reason;
            return fallback;
        }
    }
}
