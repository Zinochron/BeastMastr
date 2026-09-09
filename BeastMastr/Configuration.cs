using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace BeastMastr;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    // ---- Automation -------------------------------------------------------
    // Not yet acting on anything: the modes are recorded here so the shape is settled, and the
    // Settings tab deliberately does not offer them until they do something.

    /// <summary>How a team is filled before a run.</summary>
    public TeamMode TeamSelection { get; set; } = TeamMode.Off;

    /// <summary>How beasts are picked for an individual fight.</summary>
    public FightMode FightSelection { get; set; } = FightMode.Off;

    /// <summary>What was taken into the last fight, so it can be taken into the next one.</summary>
    public List<uint> LastFightBeasts { get; set; } = [];

    // ---- Data explorer ----------------------------------------------------
    // Phase 0 tooling. The Beastmaster sheets are almost entirely unnamed upstream, so the
    // explorer is how column meanings get pinned down; these remember where you left off.

    /// <summary>Sheet the explorer opens on.</summary>
    public string LastSheet { get; set; } = "XBMPet";

    /// <summary>Addon the inspector opens on.</summary>
    public string LastAddon { get; set; } = "XBMMonsterNotebook";

    /// <summary>Rows the sheet dumper shows at once. Some XBM sheets are long.</summary>
    public int SheetPageSize { get; set; } = 50;

    // ---- Features ---------------------------------------------------------

    /// <summary>
    /// Put status tags on the game's own bestiary tiles and dim what the filter excludes.
    /// Off turns the window back to exactly how the game draws it.
    /// </summary>
    public bool DecorateNotebook { get; set; } = true;

    /// <summary>Draw the room cards over the Crucible board.</summary>
    public bool ShowBoardOverlay { get; set; } = true;

    /// <summary>Show the Data tab. Off once the mapping work is done and the plugin is just used.</summary>
    public bool ShowDataTab { get; set; } = true;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}

/// <summary>How the roster is filled before a run starts.</summary>
public enum TeamMode
{
    /// <summary>Pick your own.</summary>
    Off,

    /// <summary>Fill with the least advanced beasts, so the ones that need the experience get it.</summary>
    Leveling,

    /// <summary>Fill with what suits the board. Needs the enemy data that only exists on hover.</summary>
    Recommended,
}

/// <summary>How beasts are picked when a fight asks for them.</summary>
public enum FightMode
{
    Off,

    /// <summary>Whatever went into the last fight. Most fights in a row want the same answer.</summary>
    RepeatLast,

    /// <summary>What beats this particular enemy. Needs the enemy data that only exists on hover.</summary>
    Recommended,
}
