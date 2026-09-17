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
    // Nothing here may fight a choice made by hand. Filling a team happens on its button and at no
    // other time; the old modes that did it on their own are gone rather than switched off, so a
    // saved "on" from an old config cannot bring them back. The one thing that acts by itself is
    // calling the last fight's familiars, and only once per opening of the fight window.

    /// <summary>
    /// Call the last fight's familiars as the fight window opens — once, and only when nothing is
    /// called yet. After that the window is yours until it closes.
    /// </summary>
    public bool CallLastFamiliarsOnOpen { get; set; } = true;

    /// <summary>
    /// Put the Crucible mode back to the one last used as the board window opens — once. The game
    /// forgets the choice between visits, which is the only reason this exists.
    /// </summary>
    public bool RememberCrucibleMode { get; set; } = true;

    /// <summary>
    /// The Crucible mode last seen set, as a position in the window's own list rather than the name
    /// it shows: the names are localised and a position is not.
    /// </summary>
    public int LastCrucibleMode { get; set; } = -1;

    /// <summary>
    /// Which board is being played, which decides how many beasts the team holds. Only a fallback:
    /// the team list states its own size, and that is used whenever it does.
    /// </summary>
    public int BoardTier { get; set; }

    /// <summary>
    /// Progression rank per beast, learned by watching the bestiary rather than asked for. There is
    /// no bulk source, so this fills in as you browse and is only ever as complete as what has been
    /// seen — which is why leveling says how many it knows before it acts.
    /// </summary>
    public Dictionary<uint, int> KnownRanks { get; set; } = [];

    /// <summary>
    /// Bumped whenever the way ranks are read changes in a way that invalidates what was stored.
    /// Ranks learned before this are thrown away on load rather than kept: a wrong rank is worse
    /// than a missing one, because a missing one says so and a wrong one quietly picks a team.
    /// </summary>
    public int KnownRanksVersion { get; set; }

    /// <summary>
    /// The reading before this was stale — it took a hidden panel's leftovers, which put fifteen
    /// beasts on the same rank in contiguous blocks.
    /// </summary>
    public const int CurrentRanksVersion = 1;

    /// <summary>
    /// Beasts that go into every levelled team regardless of rank, to carry the rest. At most
    /// <see cref="TeamPlanner.MaxCarries"/> of them — beyond that there is nothing left to level.
    /// </summary>
    public List<uint> CarryBeasts { get; set; } = [];

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

    /// <summary>
    /// Put the plugin's actions into the game's own windows as buttons. On by default: a
    /// button that is only offered where it does something is not in the way.
    /// </summary>
    public bool ShowActionButtons { get; set; } = true;

    /// <summary>
    /// Show the next room's briefing in a window of the game's own, during a run.
    /// </summary>
    public bool ShowNextRoom { get; set; } = true;

    /// <summary>
    /// The last whole board the room list showed.
    ///
    /// One board, not one per place. The first version keyed boards by territory, and the whole
    /// board is only ever listed at the entrance — while the run itself is a different territory —
    /// so inside the run it looked up an empty entry and the panel never had anything to show. The
    /// board you last planned is the board you are playing.
    ///
    /// Rewritten whenever the whole board is listed, so a wrong entry corrects itself the moment the
    /// real board is looked at.
    /// </summary>
    public SavedBoard LastBoard { get; set; } = new();

    /// <summary>
    /// Show the Data tab — the sheet explorer, the window inspector and the board captures. Off for
    /// anyone installing the plugin: it is the tooling the mapping was done with, not something to
    /// use. A config that already has it on keeps it on.
    /// </summary>
    public bool ShowDataTab { get; set; } = false;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}

/// <summary>
/// A board as the room list described it. Plain properties rather than a record: this is written to
/// disk, and a shape that deserialises without a constructor is one less thing to get wrong.
/// </summary>
[Serializable]
public class SavedBoard
{
    public List<SavedRoom> Rooms { get; set; } = [];
}

[Serializable]
public class SavedRoom
{
    public int Index { get; set; }
    public int Move { get; set; }
    public int Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
