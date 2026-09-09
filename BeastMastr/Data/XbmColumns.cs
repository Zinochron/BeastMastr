namespace BeastMastr.Data;

/// <summary>
/// Every raw column index this plugin reads, in one place.
///
/// The Beastmaster sheets have no column names in the game data, so all of these were derived by
/// dumping the sheets and correlating — see <c>README-DEV.md</c> for how each one was pinned down,
/// and <c>Harness/</c> for the dumper that did it. A patch that inserts a column silently shifts
/// every index below it, which is why <see cref="XbmPet.ColumnCount"/> exists: check it before
/// trusting anything else here.
/// </summary>
public static class XbmColumns
{
    /// <summary>The capturable beasts. 51 rows, 27 columns as of game 2026.09.01.</summary>
    public static class XbmPet
    {
        public const string Sheet = "XBMPet";
        public const int ColumnCount = 27;

        /// <summary>Row id in the <c>Pet</c> sheet, which holds the beast's name.</summary>
        public const int Pet = 0;

        /// <summary>Kin class, 1..8 (beastkin, wavekin, cloudkin, …). Display names still unknown.</summary>
        public const int KinClass = 1;

        /// <summary>Where it is caught. Read against <see cref="LocationKey"/>, not on its own.</summary>
        public const int Location = 2;

        /// <summary>1..5. Believed to be the rank shown as stars; unconfirmed.</summary>
        public const int Rank = 3;

        public const int Icon = 4;

        /// <summary>An <c>Action</c> row id, but the rows it points at are unnamed. Unconfirmed.</summary>
        public const int Action = 5;

        /// <summary>Switches how <see cref="Location"/> is read: PlaceName vs ContentFinderCondition.</summary>
        public const int LocationKey = 6;

        /// <summary>Level, or a content id. Mostly 30/42/54 with one outlier of 350. Unconfirmed.</summary>
        public const int Unknown7 = 7;

        /// <summary>Flavour text.</summary>
        public const int Description = 8;

        /// <summary>
        /// The two action descriptions, e.g. "Deals unaspected damage that paralyzes enemy."
        /// The live notebook shows three actions per beast; only two are here, and where the third
        /// lives is still open.
        /// </summary>
        public const int FirstActionText = 9;
        public const int SecondActionText = 10;

        /// <summary>
        /// Start of eleven bools saying which status the beast can inflict, in
        /// <see cref="BNpcResist"/>'s order — so column 11 + n is slot n.
        /// </summary>
        public const int FirstStatus = 11;
        public const int StatusCount = 11;

        /// <summary>
        /// Five percentages, 76..100. Not damage type resistances — there are nine damage types and
        /// only five columns. Believed to be a stat spread; unconfirmed and unused for now.
        /// </summary>
        public const int FirstStat = 22;
        public const int StatCount = 5;
    }

    /// <summary>
    /// What an enemy action does. 181 rows, 4 columns.
    /// The column order here is NOT the one EXDSchema publishes — that lists Action, Status,
    /// ActionTarget, ActionEffectType, and the real sheet has Status last. Types settle it: the two
    /// middle columns are UInt8 and index sheets of 7 and 11 rows, while status ids are UInt32.
    /// </summary>
    public static class XbmBattleDetailAction
    {
        public const string Sheet = "XBMBattleDetailAction";
        public const int ColumnCount = 4;

        public const int Action = 0;

        /// <summary>Row id in <c>XBMActionTarget</c>: self, ground, highest enmity, random, player, allies.</summary>
        public const int ActionTarget = 1;

        /// <summary>Row id in <c>XBMActionEffectType</c>, which is the AoE shape, not an effect category.</summary>
        public const int ActionEffectType = 2;

        /// <summary>Row id in <c>Status</c>, or 0 when the action applies none.</summary>
        public const int Status = 3;
    }

    /// <summary>
    /// A board. 6 rows, 37 columns. Columns 4..36 are 33 wide and line up one for one with
    /// <c>XBMScoreBonus</c>'s 33 rows: the points that board pays for each bonus, 0 when it does not
    /// offer it.
    /// </summary>
    public static class XbmContent
    {
        public const string Sheet = "XBMContent";
        public const int ColumnCount = 37;

        public const int ContentFinderCondition = 0;
        public const int FirstScoreBonus = 4;
        public const int ScoreBonusCount = 33;
    }
}
