using System;

namespace BeastMastr.Ipc;

/// <summary>Whether another plugin is loaded, asked the way Sortr asks for AutoRetainer.</summary>
public static class PluginPresence
{
    public static bool IsLoaded(string internalName) => Version(internalName).Length > 0;

    /// <summary>The loaded version, or an empty string when it is not loaded.</summary>
    public static string Version(string internalName)
    {
        foreach (var plugin in Services.PluginInterface.InstalledPlugins)
        {
            if (plugin.IsLoaded && plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase))
                return plugin.Version.ToString();
        }

        return string.Empty;
    }
}
