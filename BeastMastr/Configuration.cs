using System;
using Dalamud.Configuration;

namespace BeastMastr;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    // ---- Data explorer ----------------------------------------------------
    // Phase 0 tooling. The Beastmaster sheets are almost entirely unnamed upstream, so the
    // explorer is how column meanings get pinned down; these remember where you left off.

    /// <summary>Sheet the explorer opens on.</summary>
    public string LastSheet { get; set; } = "XBMPet";

    /// <summary>Addon the inspector opens on.</summary>
    public string LastAddon { get; set; } = "XBMMonsterNotebook";

    /// <summary>Rows the sheet dumper shows at once. Some XBM sheets are long.</summary>
    public int SheetPageSize { get; set; } = 50;

    /// <summary>Show the Data tab. Off once the mapping work is done and the plugin is just used.</summary>
    public bool ShowDataTab { get; set; } = true;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
