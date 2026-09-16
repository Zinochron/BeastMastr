using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

namespace BeastMastr;

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>
    /// 2: Parting Blow on by default, since that is how the job was played in the first recording.
    /// 3: treasure takes a random piece of gear by default.
    /// </summary>
    public const int CurrentVersion = 3;

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

    // ---- Board automation -------------------------------------------------

    /// <summary>
    /// The board last seen in a board window, as its <c>XBMContentStageEventMap</c> row. The graph is
    /// read from the sheet by this, so the board is known out in the run where no window is open.
    /// </summary>
    public uint LastBoardRowId { get; set; }

    /// <summary>
    /// Rooms picked on the board, per board row, by event index. A pick binds its move for as long as
    /// the run can still reach it; picking another room on the same move replaces it.
    /// </summary>
    public Dictionary<uint, List<int>> ChosenRooms { get; set; } = [];

    /// <summary>
    /// Room kinds from most to least wanted, as <see cref="Rules.BoardRoomKind"/> numbers. Empty means
    /// the default order. It starts empty on purpose: a list that starts filled has the saved one
    /// appended to it on every load.
    /// </summary>
    public List<int> RouteOrder { get; set; } = [];

    /// <summary>A campsite goes first while the most hurt familiar has less than this share of HP.</summary>
    public float CampsiteBelowHpShare { get; set; } = Rules.RoutePreferences.Default.CampsiteBelowHpShare;

    public bool AvoidElite { get; set; }

    /// <summary>
    /// How close to a room's centre counts as stepping onto it. Walking keeps at least this far from
    /// every room it is not heading for. A guess until a run recording measures it.
    /// </summary>
    public float RoomTriggerRadius { get; set; } = 2f;

    /// <summary>Mark the route on the game's board window, with a pin on every fork to pick a room.</summary>
    public bool ShowRouteOnBoard { get; set; } = true;

    /// <summary>Draw the route's next step on the board itself: the room to go to and the way there.</summary>
    public bool ShowRouteInWorld { get; set; } = true;

    public const int TreasureByHand = -1;
    public const int TreasureRandomGear = -2;

    /// <summary>
    /// Which of a treasure coffer's four offers the run takes: 0..3 by position,
    /// <see cref="TreasureRandomGear"/> for a random piece of gear, <see cref="TreasureByHand"/> to hand
    /// the choice to you.
    /// </summary>
    public int TreasurePick { get; set; } = TreasureRandomGear;

    /// <summary>
    /// At a shop the run buys nothing and leaves. On: it hands the shop to you instead, and carries on
    /// once you have left it.
    /// </summary>
    public bool ShopByHand { get; set; }

    /// <summary>
    /// At a campsite the run picks the most hurt familiars. Off: it rests alone — you recover 90%,
    /// the familiars keep watch.
    /// </summary>
    public bool CampsiteRestFamiliars { get; set; } = true;

    /// <summary>How many boards <c>/beastmastr run</c> plays when no number is given.</summary>
    public int RunCount { get; set; } = 1;

    // ---- When you take over ----------------------------------------------
    // Whatever the automation is doing, your own input wins at once. These decide what happens after.

    /// <summary>Stop the run for good on manual input instead of pausing it.</summary>
    public bool AbortOnManualInput { get; set; }

    /// <summary>How long after your last input a paused run carries on.</summary>
    public float ResumeDelaySeconds { get; set; } = 3f;

    /// <summary>Typing into chat or a text field, and keys ImGui is using, do not count as taking over.</summary>
    public bool IgnoreMenuInput { get; set; } = true;

    public bool CountMovementInput { get; set; } = true;
    public bool CountJumpInput { get; set; } = true;
    public bool CountTargetingInput { get; set; } = true;

    /// <summary>An action pressed on a hotbar. Automated actions never go through the hotbar.</summary>
    public bool CountActionInput { get; set; } = true;

    /// <summary>How far the left stick has to be pushed to count, 0 to 1.</summary>
    public float StickDeadzone { get; set; } = 0.3f;

    // ---- Fighting ---------------------------------------------------------

    /// <summary>What BossMod does in a fight the run plays. Nothing, if it is not loaded.</summary>
    public BossModRole BossModRole { get; set; } = BossModRole.DodgeOnly;

    /// <summary>The preset that only moves: dodging and staying in range.</summary>
    public string BossModDodgePreset { get; set; } = "BeastMastr Dodge";

    /// <summary>The preset that moves and presses the combo.</summary>
    public string BossModFullPreset { get; set; } = "BeastMastr Full";

    /// <summary>
    /// While BossMod plays the combo, BeastMastr still spends TP and cooldowns — BossMod's Beastmaster
    /// module does not.
    /// </summary>
    public bool BeastMastrHandlesResources { get; set; } = true;

    /// <summary>With no Heart to pair an axe with, TP is spent from here on.</summary>
    public int SpendTpAt { get; set; } = 200;

    public bool UseBattlehorns { get; set; } = true;

    /// <summary>
    /// Send the familiar off with Parting Blow when its cooldowns are spent, to summon the next one and
    /// have them reset. Off until a recording shows it pays.
    /// </summary>
    public bool UsePartingBlow { get; set; } = true;

    public bool UseShieldCharge { get; set; } = true;

    /// <summary>Parting Blow only while another Battlehorn is ready within this many seconds.</summary>
    public float PartingBlowHornWithin { get; set; } = 10f;

    /// <summary>…or when the target is down to this share of its HP.</summary>
    public float PartingBlowFinisherShare { get; set; } = 0.1f;

    /// <summary>Walk into melee range with vnavmesh when BossMod is not doing the moving.</summary>
    public bool KeepRangeWithNavmesh { get; set; } = true;

    /// <summary>While you have taken over, BossMod keeps dodging. Off hands it back until you let go.</summary>
    public bool KeepBossModWhilePaused { get; set; } = true;

    /// <summary>A method rather than a property, so the saved config does not carry a copy of it.</summary>
    public Rules.RoutePreferences BuildRoutePreferences() =>
        new(RouteOrder.Count == 0
                ? Rules.RoutePreferences.DefaultOrder
                : [.. RouteOrder.Select(kind => (Rules.BoardRoomKind)kind)],
            CampsiteBelowHpShare, AvoidElite);

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

    /// <summary>Show the Data tab. Off once the mapping work is done and the plugin is just used.</summary>
    public bool ShowDataTab { get; set; } = true;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);

    /// <summary>Brings an older saved config up to date. Returns whether anything changed.</summary>
    public bool Migrate()
    {
        if (Version >= CurrentVersion)
            return false;

        if (Version < 2)
            UsePartingBlow = true;

        if (Version < 3 && TreasurePick == 0)
            TreasurePick = TreasureRandomGear;

        Version = CurrentVersion;
        return true;
    }
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

/// <summary>How much of a fight BossMod plays.</summary>
public enum BossModRole
{
    /// <summary>BossMod stays out of it; BeastMastr presses everything and walks into range itself.</summary>
    Off,

    /// <summary>BossMod moves — dodging and keeping range — and BeastMastr presses everything.</summary>
    DodgeOnly,

    /// <summary>BossMod moves and presses the combo; BeastMastr spends the resources.</summary>
    DodgeAndRotation,
}
