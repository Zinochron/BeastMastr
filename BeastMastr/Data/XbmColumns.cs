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
        /// Five numbers, mostly 76..100 with at least one 1. **Not identified, and specifically not
        /// the five stats**, though they were briefly labelled as such here.
        ///
        /// The party window shows a beast's Strength, Intelligence, Phys. Resistance,
        /// Mag. Resistance and Constitution, and they do not match: dullahan reads 97 five times
        /// across these columns while the window shows 109, 67, 80, 80, 100, and diremite reads 100
        /// five times against 134, 83, 83, 83, 137. Twenty-eight of the fifty beasts have all five
        /// equal, which no stat spread would do. Whatever these are, the stats are computed
        /// elsewhere.
        ///
        /// The mistake worth not repeating: a window's column headers prove the window has those
        /// columns, not that a sheet column is one of them.
        /// </summary>
        public const int FirstUnknownPercent = 22;
        public const int UnknownPercentCount = 5;
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
    /// The team roster, <c>XBMPetParty</c>. **This is the window that shows which statuses a beast
    /// inflicts** — the bestiary does not, which is why looking there for them finds nothing.
    ///
    /// One block of 77 AtkValues per roster slot. Blocks exist for empty slots too, so read the
    /// name and skip the block when it is blank.
    /// </summary>
    public static class PetParty
    {
        public const string Addon = "XBMPetParty";

        public const int FirstBlock = 9;
        public const int BlockStride = 77;

        /// <summary>The beast's name, and the start of its block.</summary>
        public const int NameOffset = 0;

        /// <summary>Five label/value pairs: STR, PHY R, CON, INT, MAG R, as the window orders them.</summary>
        public const int FirstStatPairOffset = 15;
        public const int StatPairCount = 5;

        /// <summary>
        /// Eleven bools, one per status, paired positionally with the eleven labels at
        /// <see cref="FirstStatusLabelOffset"/>. **These follow the window's display order, not
        /// <see cref="Rules.BeastStatus"/>'s storage order** — read the label rather than assuming.
        /// </summary>
        public const int FirstStatusFlagOffset = 44;

        /// <summary>The eleven status names, in the same positional order as the flags.</summary>
        public const int FirstStatusLabelOffset = 56;

        public const int StatusCount = 11;

        public static int Value(int block, int offset) =>
            FirstBlock + (block * BlockStride) + offset;
    }

    /// <summary>
    /// The board, <c>XBMStageMap</c>. Its own AtkValues hold almost nothing — the room content
    /// comes from <see cref="StageDetailList"/> — but its node tree is where the rooms sit on
    /// screen, which is what an overlay has to anchor to.
    /// </summary>
    public static class StageMap
    {
        public const string Addon = "XBMStageMap";

        /// <summary>
        /// Room nodes run from here upward inside the <c>XBMContentStageEventMap</c> component,
        /// as 50x50 tiles on a three column grid 60 pixels apart. Not every id is in use; read the
        /// visible ones rather than assuming a count.
        /// </summary>
        public const int FirstRoomNodeId = 30001;

        /// <summary>Straight links between two rooms, 40001 upward.</summary>
        public const int FirstStraightLinkNodeId = 40001;

        /// <summary>The two diagonal link graphics, 50001 and 70001 upward.</summary>
        public const int FirstDiagonalLinkNodeId = 50001;
        public const int SecondDiagonalLinkNodeId = 70001;
    }

    /// <summary>
    /// The board's room list, <c>XBMStageDetailList</c>. One block of forty AtkValues per room,
    /// starting at <see cref="FirstBlock"/>, listed from the last move backwards.
    /// </summary>
    public static class StageDetailList
    {
        public const string Addon = "XBMStageDetailList";

        public const int FirstBlock = 6;
        public const int BlockStride = 40;

        /// <summary>Which move the room is on. Branching moves appear twice, once per option.</summary>
        public const int MoveOffset = 2;

        /// <summary>"Move 12", as shown.</summary>
        public const int MoveLabelOffset = 4;

        /// <summary>Room kind, see <see cref="RoomKind"/>.</summary>
        public const int KindOffset = 5;

        /// <summary>"Elite Enemy #2: Combat 3 types of beast."</summary>
        public const int DescriptionOffset = 7;

        public static int Value(int block, int offset) =>
            FirstBlock + (block * BlockStride) + offset;
    }

    /// <summary>
    /// Room kinds as <see cref="StageDetailList.KindOffset"/> reports them. Read off a full board:
    /// every value carried a description naming its kind.
    /// </summary>
    public enum RoomKind
    {
        Enemy = 0,
        EliteEnemy = 1,
        Boss = 2,
        Shop = 3,
        Campsite = 4,
        Treasure = 5,
        RandomEnemyOrTreasure = 6,
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
