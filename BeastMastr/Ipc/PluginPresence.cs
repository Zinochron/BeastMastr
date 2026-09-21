using System;
using Dalamud.Interface;

namespace BeastMastr.Ipc;

/// <summary>Whether another plugin is there and running, and the two ways to fix it when it is not.</summary>
public static class PluginPresence
{
    public enum State
    {
        Loaded,

        /// <summary>Installed and switched off — the one case a button can fix outright.</summary>
        Off,

        Outdated,

        /// <summary>Banned, decommissioned or orphaned: installed, but Dalamud will not load it.</summary>
        Unusable,

        Missing,
    }

    /// <param name="Name">The display name, which is what <c>/xlenableplugin</c> takes — not the internal one.</param>
    public sealed record Status(State State, string Name, string Version);

    public static bool IsLoaded(string internalName) => Check(internalName, internalName).State == State.Loaded;

    /// <summary>The loaded version, or an empty string when it is not loaded.</summary>
    public static string Version(string internalName) =>
        Check(internalName, internalName) is { State: State.Loaded } status ? status.Version : string.Empty;

    /// <summary>
    /// Where a plugin stands. A plugin installed twice — a dev copy beside the repository one — counts
    /// as loaded if either is.
    /// </summary>
    public static Status Check(string internalName, string fallbackName)
    {
        Status? found = null;

        foreach (var plugin in Services.PluginInterface.InstalledPlugins)
        {
            if (!plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase))
                continue;

            var state = plugin.IsLoaded ? State.Loaded
                        : plugin.IsBanned || plugin.IsDecommissioned || plugin.IsOrphaned ? State.Unusable
                        : plugin.IsOutdated ? State.Outdated
                        : State.Off;

            var status = new Status(state, plugin.Name, plugin.Version.ToString());
            if (state == State.Loaded)
                return status;

            found ??= status;
        }

        return found ?? new Status(State.Missing, fallbackName, string.Empty);
    }

    /// <summary>
    /// Switches a plugin on through Dalamud's own <c>/xlenableplugin</c>, which keeps it on across
    /// restarts like the installer's toggle does. It loads a moment later; the caller reads the state
    /// again rather than trusting this. False only when Dalamud did not know the command.
    /// </summary>
    public static bool Enable(string name) => Services.Commands.ProcessCommand($"/xlenableplugin \"{name}\"");

    /// <summary>The plugin installer, searched for the plugin — for what a switch cannot fix.</summary>
    public static void ShowInInstaller(string name) =>
        Services.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, name);
}
