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

        /// <summary>
        /// Classification, 1..8. The detail page calls it that and shows it as a word; 1 is
        /// Beastkin, confirmed against goobbue. The other seven names are not in the data yet.
        /// </summary>
        public const int KinClass = 1;

        /// <summary>Where it is caught. Read against <see cref="LocationKey"/>, not on its own.</summary>
        public const int Location = 2;

        /// <summary>
        /// Beast rank, 1..5, distributed 7/26/12/2/4.
        ///
        /// Inferred, not confirmed. The notebook has a "Beast Rank" column — its header arrives in
        /// AtkValue 15, ahead of the five stat headers that name columns 22..26 — and this is the
        /// only small-valued column left over. But the detail page does not show a rank, so nothing
        /// has yet been seen putting a number against a named beast. The grid's roman numerals are
        /// not it: two beasts both at 3 carry I and II, and one at 3 carries none. Those are
        /// Battlehorn slot assignments, which are player state rather than sheet data.
        /// </summary>
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
        /// The beast's Trick, e.g. "Delivers a blunt physical attack." The detail page groups the
        /// three actions as Trick, Tempered Release and Borrow, each with an unlock level, and
        /// these two columns are the first two of those in that order — confirmed against goobbue,
        /// whose Trick is Beatdown and whose Tempered Release is Moldy Sneeze.
        /// </summary>
        public const int TrickText = 9;

        /// <summary>The beast's Tempered Release. See <see cref="TrickText"/>.</summary>
        public const int TemperedReleaseText = 10;

        /// <summary>
        /// There is no Borrow column. The third action is named on the detail page but is not in
        /// this sheet, and only its description would be here anyway — none of the three action
        /// *names* are. Where they come from is still open.
        /// </summary>
        public const int BorrowText = -1;

        /// <summary>
        /// Start of eleven bools saying which status the beast can inflict, in
        /// <see cref="BNpcResist"/>'s order — so column 11 + n is slot n.
        /// </summary>
        public const int FirstStatus = 11;
        public const int StatusCount = 11;

        /// <summary>
        /// Five percentages, 76..100. Not damage type resistances — the notebook names them itself
        /// in AtkValues 16..20, in this order: Strength, Intelligence, Phys. Resistance,
        /// Mag. Resistance, Constitution.
        /// </summary>
        public const int FirstStat = 22;
        public const int StatCount = 5;

        public const int Strength = 22;
        public const int Intelligence = 23;
        public const int PhysicalResistance = 24;
        public const int MagicResistance = 25;
        public const int Constitution = 26;
    }

    /// <summary>
    /// The Master's Bestiary window, <c>XBMMonsterNotebook</c>.
    ///
    /// Its overview is a fixed five by five grid of tiles for fifty beasts, so a tile is a **slot,
    /// not a beast** — the grid pages, and the same node id shows a different beast on each page.
    /// Anything attached to a tile has to be keyed to whatever is in it at the time.
    /// </summary>
    public static class MonsterNotebook
    {
        public const string Addon = "XBMMonsterNotebook";

        /// <summary>Node id of the first tile. Ids run upward from here, but the grid is laid out
        /// in reverse: id 27 is slot 1 and id 51 is slot 25.</summary>
        public const int FirstTileNodeId = 27;
        public const int TileCount = 25;

        /// <summary>Column headers, which is what names <see cref="XbmPet"/>'s five stat columns.</summary>
        public const int FirstStatHeaderValue = 16;

        /// <summary>
        /// Start of the per-slot AtkValue block, eight values per slot. The icon at
        /// <see cref="SlotIconOffset"/> is the join key: it matches <see cref="XbmPet.Icon"/>
        /// exactly, which is how a tile is resolved back to its beast.
        /// </summary>
        public const int FirstSlotValue = 24;
        public const int SlotValueStride = 8;
        public const int SlotIconOffset = 4;

        public static int SlotIconValue(int slot) =>
            FirstSlotValue + (slot * SlotValueStride) + SlotIconOffset;

        public static int TileNodeId(int slot) => FirstTileNodeId + slot;
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
