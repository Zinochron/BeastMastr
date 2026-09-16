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
    /// <summary>
    /// The classification's display name, in the <c>Addon</c> sheet at 17740 plus the value of
    /// <see cref="XbmPet.KinClass"/>. Verified for all eight: Beastkin, Vilekin, Cloudkin, Seedkin,
    /// Wavekin, Scalekin, Soulkin, Ashkin, each cross-checked against a beast known to be one.
    /// Being in <c>Addon</c> means it arrives already in the player's language.
    /// </summary>
    public static uint ClassificationAddonRow(int classification) => (uint)(17740 + classification);

    /// <summary>
    /// The Borrow action, which belongs to the classification rather than the beast: every Beastkin
    /// borrows Beastskin, every Soulkin Soul Crush. <c>Action</c> row 44895 plus the classification.
    ///
    /// This is arithmetic, and arithmetic is what the <c>Pet</c> link exists to avoid — but here
    /// there is no link to use, and all eight land on a name that matches its classification:
    /// Vilekin on Vileskin, Cloudkin on Cloud Skim, Ashkin on Scouring Ash. Re-check after a patch.
    /// </summary>
    public static uint BorrowActionId(int classification) => (uint)(44895 + classification);

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
        /// There is no Borrow column, and there does not need to be: **Borrow is per
        /// Classification, not per beast.** Every Beastkin borrows Beastskin, every Soulkin borrows
        /// Soul Crush — golem and coblyn are both Soulkin and both show Soul Crush. So it follows
        /// from <see cref="KinClass"/>.
        /// </summary>
        public const int BorrowIsPerClassification = -1;

        /// <summary>
        /// The action *names* are in none of these columns either — the sheet carries descriptions
        /// only. They are in the <c>Action</c> sheet, reachable by arithmetic on the row id:
        /// see <see cref="TrickActionId"/>.
        /// </summary>
        public const int ActionNamesAreElsewhere = -1;

        /// <summary>
        /// The action ids are not in this sheet at all — they are in <c>Pet</c>, which
        /// <see cref="Pet"/> reaches. Go through <see cref="XbmColumns.Pet"/> rather than doing
        /// arithmetic on row ids: an id computed from a row number breaks the moment a patch
        /// inserts a beast, and a link does not.
        /// </summary>
        public const int ActionIdsAreInThePetSheet = -1;

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
    /// The <c>Pet</c> sheet, which <see cref="XbmPet.Pet"/> points at. 104 rows, 20 columns; the
    /// fifty Beastmaster beasts are the rows whose <see cref="XbmPetRow"/> points back.
    ///
    /// This is where a beast's actions actually live. Reaching them through here beats computing
    /// them: the ids also happen to sit at <c>44933 + 2 × XBMPet row</c> for all fifty beasts
    /// today, but that is a coincidence of the current ordering and a patch inserting a beast would
    /// silently shift every one of them.
    /// </summary>
    public static class Pet
    {
        public const string Sheet = "Pet";
        public const int ColumnCount = 20;

        public const int Name = 0;

        /// <summary>The beast's Trick, as an <c>Action</c> row id.</summary>
        public const int TrickAction = 1;

        /// <summary>The beast's Tempered Release.</summary>
        public const int TemperedReleaseAction = 2;

        /// <summary>"Aetheric Burst" for every beast — shared, so not the per-beast Borrow.</summary>
        public const int SharedActionA = 3;

        /// <summary>"Threaten" for every beast.</summary>
        public const int SharedActionB = 4;

        /// <summary>
        /// Three numbers that scale together — 70/100/120 for most beasts, 49/70/84 for goobbue,
        /// which is exactly seven tenths of them. Unidentified.
        /// </summary>
        public const int FirstScalingNumber = 9;
        public const int ScalingNumberCount = 3;

        /// <summary>
        /// Points back at the <c>XBMPet</c> row. Verified for all fifty, so it is the reliable way
        /// to walk from a <c>Pet</c> row to its Beastmaster data.
        /// </summary>
        public const int XbmPetRow = 19;
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

        /// <summary>
        /// Beasts per page. Fifty across two pages of a five by five grid.
        /// </summary>
        public const int PageSize = TileCount;

        /// <summary>Command its own click sends to change page, with the page number after it, from zero.</summary>
        public const int TurnPageCommand = 3;

        /// <summary>Command its own click sends to put a beast in or out of the team, with the slot after it.</summary>
        public const int ToggleTeamCommand = 7;

        /// <summary>
        /// What the window is told when the cursor enters a tile, with the slot after it. It repaints
        /// the detail page and changes nothing else, which is what makes it safe to send on purpose.
        /// </summary>
        public const int HoverCommand = 5;

        /// <summary>The bestiary number shown in a slot, which is how the current page is read back.</summary>
        public static int SlotNumberValue(int slot) => FirstSlotValue + (slot * SlotValueStride);

        public static int PageOf(uint beastNumber) => (int)(beastNumber - 1) / PageSize;

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
    /// One enemy's detail panel, <c>XBMBattleMonsterDetail</c>. **This is the whole payload the
    /// room cards need**, and it only exists while the cursor rests on an enemy — so it has to be
    /// read on hover and cached, not fetched on demand.
    ///
    /// Its AtkValues hold two numbers; everything is in the node tree, as text.
    /// </summary>
    public static class BattleMonsterDetail
    {
        public const string Addon = "XBMBattleMonsterDetail";

        public const int NameNodeId = 21;

        /// <summary>Its damage type weakness, e.g. "Wind", prefixed with an icon glyph.</summary>
        public const int WeaknessNodeId = 23;

        /// <summary>Label only; the vulnerabilities themselves are the icon nodes 13..18.</summary>
        public const int VulnerabilitiesLabelNodeId = 24;

        /// <summary>
        /// Five label/value pairs shown as star counts, one component each: 27 Strength,
        /// 28 Phys. Resistance, 29 Constitution, 30 Intelligence, 31 Mag. Resistance. Within a
        /// component, node 2 is the label and node 3 the stars.
        /// </summary>
        public const int FirstStatNodeId = 27;
        public const int StatCount = 5;
        public const int StatLabelChildNodeId = 2;
        public const int StatStarsChildNodeId = 3;

        /// <summary>
        /// One component per enemy action, 35 and 39 in the two-action case. Inside each:
        /// 2 name, 4 target, 6 damage type, 8 whether it can be interrupted, 10 AoE shape,
        /// 14 the status it applies, and a hidden 15 reading "Nullification ✓" when the team
        /// already covers that status.
        /// </summary>
        public const int ActionNameChildNodeId = 2;
        public const int ActionTargetChildNodeId = 4;
        public const int ActionDamageTypeChildNodeId = 6;
        public const int ActionInterruptionChildNodeId = 8;
        public const int ActionAreaOfEffectChildNodeId = 10;
        public const int ActionStatusChildNodeId = 14;
        public const int ActionNullificationChildNodeId = 15;
    }

    /// <summary>
    /// The bestiary's detail page, <c>XBMMonsterBookDetail</c> — a separate window from the grid.
    /// Shows no status flags at all; those are in <see cref="PetParty"/>.
    /// </summary>
    public static class MonsterBookDetail
    {
        public const string Addon = "XBMMonsterBookDetail";

        public const int NumberNodeId = 12;
        public const int NameNodeId = 13;
        public const int ClassificationNodeId = 21;

        /// <summary>Auto-attack damage type, prefixed with an icon glyph.</summary>
        public const int AutoAttackNodeId = 23;

        /// <summary>The Borrow action's name — the same for every beast of a classification.</summary>
        public const int BorrowNameNodeId = 24;

        public const int HabitatNodeId = 30;
        public const int DescriptionNodeId = 33;

        /// <summary>
        /// Rank, EXP, HP and Satiety live under node 36, along with five stat components at 47..51.
        /// The panel is hidden while the bestiary is merely being browsed and shown while a team is
        /// being put together — which is why an early capture found every value node empty and the
        /// rank looked unavailable.
        /// </summary>
        public const int RankPanelNodeId = 36;
        public const int RankLabelNodeId = 39;

        /// <summary>The rank itself, e.g. "5".</summary>
        public const int RankValueNodeId = 40;

        /// <summary>Progress within the rank, e.g. "9/100" — the tie-breaker between equal ranks.</summary>
        public const int ExperienceValueNodeId = 42;
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

        /// <summary>
        /// A block starts three values *before* the name, not at it. The icon is what proved it:
        /// aligned on the name the icon read as the next beast's, and aligned here every icon
        /// matches its own beast's <see cref="XbmPet.Icon"/>.
        /// </summary>
        public const int FirstBlock = 6;
        public const int BlockStride = 77;

        /// <summary>
        /// The beast's progression rank, as a string. Not the same as <see cref="XbmPet.Rank"/>:
        /// behemoth reads 5 here against a sheet value of 4. This is the one that answers "least
        /// advanced", and it is the only place found so far that carries it.
        /// </summary>
        public const int RankOffset = 0;

        /// <summary>Matches <see cref="XbmPet.Icon"/>, so it is what identifies the beast.</summary>
        public const int IconOffset = 1;

        public const int NameOffset = 3;

        /// <summary>Five label/value pairs: STR, PHY R, CON, INT, MAG R, as the window orders them.</summary>
        public const int FirstStatPairOffset = 18;
        public const int StatPairCount = 5;

        /// <summary>
        /// Eleven bools, one per status, paired positionally with the eleven labels at
        /// <see cref="FirstStatusLabelOffset"/>. These follow the window's display order, not
        /// <see cref="Rules.BeastStatus"/>'s storage order — read the label rather than assuming.
        /// </summary>
        public const int FirstStatusFlagOffset = 47;

        /// <summary>The eleven status names, in the same positional order as the flags.</summary>
        public const int FirstStatusLabelOffset = 59;

        public const int StatusCount = 11;

        /// <summary>The beast's <c>XBMPet</c> row, which the window also hands out directly.</summary>
        public const int SheetRowOffset = 76;

        /// <summary>
        /// Which of the fight's call slots this beast is in — 0, 1 or 2 — or
        /// <see cref="NotCalled"/> when it is not called at all. Only meaningful while the window is
        /// asking for a fight's familiars; on the run's team screen everything reads
        /// <see cref="NotCalled"/>.
        /// </summary>
        public const int CallSlotOffset = 74;

        public const int NotCalled = 3;

        /// <summary>
        /// The window's own prompt, which is how its two jobs are told apart: "Select a team of
        /// familiars" before a run against "Select familiars to call upon during combat" before a
        /// fight. It is a fixed index past the end of the blocks, which is fragile — but the
        /// alternative is matching those sentences, and they are localised.
        /// </summary>
        public const int PromptValue = 1185;

        /// <summary>
        /// How many beasts the team holds out of how many it can, as the window shows it above the
        /// list: "0/14". **This is the membership count, and the blocks are not.** The window keeps
        /// every row it ever wrote and only draws the first this-many of them as the team — right
        /// after a "Remove all" it read "0/14" with fourteen old rows still sitting in the blocks,
        /// which is how a check that read the blocks saw a full team in an empty one.
        ///
        /// Fifteen blocks end at 1161; this and <see cref="TeamCapacityValue"/> follow them. Read in
        /// three captures: "0/14" and "0/12" in team composition, "10/10" in a fight.
        /// </summary>
        public const int TeamCountValue = 1181;

        /// <summary>
        /// The team's size on this board — 14, 12 or 10 in the captures. The same number as the part
        /// of <see cref="TeamCountValue"/> after the slash, and what the board actually takes, where
        /// the board tier setting is only what someone said it takes.
        /// </summary>
        public const int TeamCapacityValue = 1162;

        /// <summary>
        /// Which of its two jobs the window is doing, as a number rather than a localised sentence:
        /// <see cref="TeamCompositionMode"/> while a run's team is being put together, 2 while a
        /// fight's familiars are being called. Read off three captures — two of team composition, one
        /// of a fight — and it was the only header value that split them.
        /// </summary>
        public const int ModeValue = 2;

        public const uint TeamCompositionMode = 0;

        /// <summary>
        /// Calling a fight's familiars, from the fight capture. The window has more jobs than these
        /// two — picking a familiar to feed at a shop, picking who rests at a campsite — and treating
        /// "not team composition" as "fight" called familiars into both. Their numbers are not
        /// captured yet; every change of mode is logged so that they will be.
        /// </summary>
        public const uint FightMode = 2;

        /// <summary>
        /// Read off the log's mode lines: 1 while the board layout is open over it, 4 at a campsite
        /// ("You and 3 familiars can recover HP at this campsite."). The shop's number is still
        /// missing.
        /// </summary>
        public const uint BoardLayoutMode = 1;

        public const uint CampsiteMode = 4;

        /// <summary>Picking the familiar a bought feed goes to, at a shop ("Feed the grape simular to whom?").</summary>
        public const uint ShopFeedMode = 3;

        /// <summary>
        /// Outside a fight the row click is <c>[1, row]</c> with the row as an **Int** and the window
        /// told it closes — recorded at a campsite and at a shop's feeding. In a fight the row is a UInt
        /// and nothing closes. Same command, different payload per job.
        /// </summary>
        public const int PickedOffset = 75;

        /// <summary>
        /// The window's confirm button outside team composition — "Rest" at a campsite, the feeding at a
        /// shop: <c>[3]</c>, sent with the window closing, answered by a <c>SelectYesno</c>.
        /// </summary>
        public const int ConfirmCommand = 3;

        /// <summary>A beast's HP as the row shows it, "2943/2943". Offset 5 carries the first number alone.</summary>
        public const int HpTextOffset = 4;

        /// <summary>
        /// Right-clicking a row sends <c>[2, row]</c>, both Ints, and the game answers by opening that
        /// row's menu. Recorded from a real right click on row 0.
        /// </summary>
        public const int OpenMenuCommand = 2;

        /// <summary>
        /// "Remove all" is the third entry of that menu, picked with <c>[0, 2, 0u]</c> on the
        /// <c>ContextMenu</c> window and confirmed with <c>[0]</c> on <c>SelectYesno</c>.
        /// </summary>
        public const int RemoveAllMenuEntry = 2;

        /// <summary>
        /// The button that opens the Master's Bestiary from the team list: <c>[5]</c>, one Int, sent
        /// with the window closing. The team list goes away and comes back beside the bestiary, so
        /// anything watching it sees it gone for a moment.
        /// </summary>
        public const int OpenBestiaryCommand = 5;

        public static int Value(int block, int offset) =>
            FirstBlock + (block * BlockStride) + offset;
    }

    /// <summary>
    /// The run's own HUD, <c>XBMContentsMainHUD</c> — the score and progress readout that is up for
    /// the whole of a run and at no other time. Nothing is read from it; it is used as the answer to
    /// "is a run under way", which is what decides whether the next room is worth briefing.
    /// </summary>
    public static class ContentsMainHUD
    {
        public const string Addon = "XBMContentsMainHUD";
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
    /// The board selection, <c>XBMStageList</c> — where a board is chosen before a run, and where
    /// the difficulty choice appears once every board has been cleared. Nothing is read from it yet;
    /// the board report dumps it whole so that one capture of that screen settles what to read.
    /// </summary>
    public static class StageList
    {
        public const string Addon = "XBMStageList";
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

        /// <summary>
        /// The two stepper buttons beside the Crucible mode drop-down, recorded from real clicks:
        /// one Int each, sent with the window closing. Which of them raises the mode is not taken
        /// from these names — <see cref="Automation.DifficultySelector"/> presses one and watches
        /// what happens, because the button that looks like "up" is an assumption and the move is
        /// evidence.
        /// </summary>
        public const int RaiseModeCommand = 5;

        public const int LowerModeCommand = 4;

        /// <summary>
        /// Inside a run the briefing carries "Commence Battle" and "Flee" here (nodes 33 and 32, both
        /// hidden in every capture so far); before a run the first reads "Challenge This Board".
        /// </summary>
        public const int CommenceBattleValue = 40056;

        /// <summary>
        /// What "Commence Battle" sends: <c>[8]</c>, one Int, with the window closing. Recorded on all five
        /// fights of a run. The run then loads into a separate arena in the same zone.
        /// </summary>
        public const int CommenceBattleCommand = 8;

        public const int FleeValue = 40057;

        /// <summary>-1 before a run; in a run the room list index of the room the board marks.</summary>
        public const int CurrentRoomValue = 40032;

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
    /// The board as a graph, <c>XBMContentStageEventMap</c> — a subrow sheet with one row per board
    /// and one subrow per cell the board window draws. Five UInt8 columns, and the live component
    /// (<c>AtkComponentXBMContentStageEventMap.EventMapEntries</c>) carries the same five bytes.
    ///
    /// Read offline against game 2026.09.01: a room is a cell of <see cref="RoomCellType"/>, every
    /// other type is a piece of a link from its event to <see cref="LinkedEventIndex"/>. Y counts
    /// down the screen, so the start (event 0) has the largest Y and the boss the smallest. X is the
    /// column, 6 in the middle of a three column board. A link can take several cells — the fifth
    /// board draws sideways links two cells wide — so edges are deduplicated rather than counted.
    /// </summary>
    public static class StageEventMap
    {
        public const string Sheet = "XBMContentStageEventMap";
        public const int ColumnCount = 5;

        public const int X = 0;
        public const int Y = 1;
        public const int Type = 2;
        public const int EventIndex = 3;
        public const int LinkedEventIndex = 4;

        /// <summary>
        /// A room. Links are 6 (straight up), 4/9 and 5/10 (the two diagonals) and 7/8 (sideways on
        /// the fifth board); only "not a room" matters for the graph.
        /// </summary>
        public const int RoomCellType = 1;
    }

    /// <summary>
    /// What each event on a board is, <c>XBMContentStageEvent</c> — a subrow sheet, one row per board,
    /// subrow index = event index. Columns 0 and 1 settled against the room lists captured at the
    /// entrance, move for move and kind for kind; 2 and 3 are not identified.
    /// </summary>
    public static class StageEvent
    {
        public const string Sheet = "XBMContentStageEvent";
        public const int ColumnCount = 4;

        public const int Move = 0;

        /// <summary>1 is the start; 2 onward is <see cref="RoomKind"/> plus two.</summary>
        public const int EventType = 1;

        public const int StartEventType = 1;
        public const int FirstRoomEventType = 2;

        /// <summary>
        /// Rises with the number of enemy groups on a board — 1..6 for enemies — but the elite rooms
        /// read 3 and 6 while being labelled #1 and #2, so it is not the label's number.
        /// </summary>
        public const int Unknown2 = 2;

        /// <summary>Strictly increasing per board. Some id; not identified.</summary>
        public const int Unknown3 = 3;
    }

    /// <summary>
    /// The Crucible as a place. Numbers read off captures in `captures/`, each one named where it is
    /// used.
    /// </summary>
    public static class Crucible
    {
        /// <summary>Where every board is played, whichever board it is.</summary>
        public const uint RunTerritory = 1339;

        /// <summary>Central Shroud, where the boards are chosen.</summary>
        public const uint EntranceTerritory = 148;

        /// <summary>
        /// An unnamed event object that sits on the room the player is on or has just finished. The
        /// only object in the run besides the player, seen on five different rooms.
        /// </summary>
        public const uint RoomTriggerDataId = 2015483;

        /// <summary>
        /// "In Event": on the player the moment a room starts — together with the condition flag
        /// SufferingStatusAffliction2 — about two seconds before the room's windows open. A room
        /// starts when the player is within 0.4 to 1.8 yalms of its centre (one recorded run, nine
        /// rooms).
        /// </summary>
        public const uint InEventStatus = 1268;

        /// <summary>The event object at the entrance that opens the board selection.</summary>
        public const uint EntranceDataId = 2015511;

        /// <summary>The board's room icons run from 63850; 63853 is not yet seen and assumed to be Random.</summary>
        public const uint FirstRoomIcon = 63850;

        public const uint LastRoomIcon = 63859;
    }

    /// <summary>Windows only seen by name so far. What they hold is for the run recorder to find out.</summary>
    public static class RunWindows
    {
        public const string ItemShop = "XBMContentsItemShop";
        public const string Treasure = "XBMContentsTreasure";

        /// <summary>Appears about ninety seconds after a fight starts, so taken to be the spoils.</summary>
        public const string Booty = "XBMContentsBooty";

        public const string Result = "XBMResult";

        /// <summary>The job's own gauge on the HUD, in two parts: JobHudXBM0 and JobHudXBM1.</summary>
        public const string JobHud = "JobHudXBM";

        /// <summary>
        /// The spoils: <c>[1]</c>, with the window closing, takes everything; a <c>SelectYesno</c>
        /// ("You will receive:") confirms it.
        /// </summary>
        public const int TakeSpoilsCommand = 1;

        /// <summary>
        /// A treasure coffer offers four items. <c>[2, n]</c> — both Ints, n counted from 0, with the
        /// window closing — picks one, and a <c>SelectYesno</c> ("Choose the angel robe?") confirms it;
        /// answering No leaves the choice open. The offers are blocks of five values from
        /// <see cref="TreasureFirstOffer"/>, the <c>XBMItem</c> row at +3.
        /// </summary>
        public const int ChooseTreasureCommand = 2;

        public const int TreasureOffers = 4;
        public const int TreasureFirstOffer = 3;
        public const int TreasureOfferStride = 5;
        public const int TreasureItemOffset = 3;

        /// <summary>
        /// The shop's own close: <c>[0]</c>, with the window closing, then a <c>SelectYesno</c>
        /// ("Conclude purchasing and leave the shop?"). <c>[2, n]</c> buys or feeds item n, and the
        /// window sends itself <c>[8]</c> after every change.
        /// </summary>
        public const int LeaveShopCommand = 0;

        /// <summary>A <c>SelectYesno</c>'s answers: <c>[0]</c> yes, <c>[1]</c> no, both closing it.</summary>
        public const int Yes = 0;
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
