namespace BeastMastr.Data;

/// <summary>
/// The names the game uses for Beastmaster content. Everything Beastmaster is prefixed "XBM"
/// internally, which is the only reliable way to find it — nothing is called Beastmaster or
/// Crucible in the data.
/// </summary>
public static class BeastmasterData
{
    /// <summary>
    /// Excel sheets, with what each one turned out to hold. Nothing here is named in the game data;
    /// these notes are the result of dumping the sheets offline and correlating — see
    /// <c>README-DEV.md</c> for the evidence and <c>Data/XbmColumns.cs</c> for the indices.
    /// </summary>
    public static readonly (string Sheet, string Note)[] Sheets =
    [
        ("XBMPet", "The 51 capturable beasts, 27 columns. Columns 11..21 are the eleven statuses the beast can inflict; 9 and 10 are two action descriptions, and the third action the notebook shows is not in here."),
        ("XBMBattleDetailAction", "What an enemy action does: Action, ActionTarget, ActionEffectType, Status — in that order, which is not the order EXDSchema publishes."),
        ("XBMActionTarget", "Self, Ground, Highest Enmity, Random, Player, Allies."),
        ("XBMActionEffectType", "AoE shapes despite the name: Single Target, Front, Rear, Front/Rear, Lateral, Circle, Ring, Circle/Ring, Universal, Cross."),
        ("XBMElement", "The nine damage types: Fire, Wind, Earth, Lightning, Ice, Water, Blunt, Piercing, Slashing. Each value has a leading space."),
        ("XBMContent", "One row per board, keyed by ContentFinderCondition 1088..1092. Columns 4..36 are the points that board pays for each of XBMScoreBonus's 33 bonuses."),
        ("XBMScoreBonus", "The 33 score bonuses, with name and condition."),
        ("XBMScoreRank", "Apprentice up to Legendary."),
        ("XBMStageEventType", "Nine rows of one UInt8. A remap; the room kinds are not spelled out here."),
        ("XBMEntrance", "Six rows: id, flag, and a UInt32 stepping 71030..71037."),
        ("XBMItem", "The 206 Crucible items, fully readable: name, effect text, icon, price."),
        ("XBMItemType", "Beast Gear, Crucible Item, Feed."),
        ("BNpcResist", "256 rows of eleven bools. XBMPet's status columns follow this indexer."),
        ("Pet", "Linked from XBMPet column 0. The beast's name."),
        ("Action", "Linked from XBMPet column 5, though those rows carry no name."),
        ("Status", "Linked from XBMBattleDetailAction. The status an enemy action applies."),
    ];

    /// <summary>
    /// The Beastmaster windows, as FFXIVClientStructs names them. Present in Dalamud 15.0.3.3.
    /// </summary>
    public static readonly (string Addon, string Note)[] Addons =
    [
        ("XBMMonsterNotebook", "The Master's Bestiary."),
        ("XBMPetParty", "The roster of beasts taken into a run."),
        ("XBMStageMap", "The board. Rooms live in AtkComponentXBMContentStageEventMap.Entries."),
        ("XBMStageList", "Board selection."),
        ("XBMStageDetailList", "Room detail."),
        ("XBMBattleMonster", "Enemies of a room."),
        ("XBMBattleMonsterDetail", "One enemy: weaknesses, statuses it is open to, its actions."),
        ("XBMContentsMainHUD", "The in-run HUD."),
        ("XBMItemDetail", "Item detail inside a run."),
        ("XBMRanking", "Score ranking."),
        ("XBMResult", "End of run result."),
    ];

    /// <summary>What is known about a window, or a note that it is new — which is itself worth
    /// seeing in a capture, because it means the hand written list above needs an entry.</summary>
    public static string NoteFor(string addon)
    {
        foreach (var (known, note) in Addons)
        {
            if (known == addon)
                return note;
        }

        return "Not in the known list — worth naming.";
    }
}
