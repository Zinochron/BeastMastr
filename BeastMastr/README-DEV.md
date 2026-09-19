# BeastMastr — implementation notes

Written for whoever touches this next, including future me.

## Layout

| Folder | What lives there |
|---|---|
| `Data/` | Reading the game: Excel sheets, addon values and node trees, the beast catalogue, the board model and its ground |
| `Rules/` | Pure calculation: the beast model, trait classification, filtering, the board graph and the route |
| `Native/` | Everything that mutates the game's UI: KamiToolKit node injection |
| `Automation/` | Everything that changes game state: callbacks sent to windows, and later movement and actions |
| `Ipc/` | Other plugins, over their IPC: vnavmesh now, BossMod later |
| `UI/` | ImGui windows and tabs |

`Data` and `Rules` never change game state. `Native` and `Automation` are the only places that write.
`Rules` additionally holds no Dalamud references at all, so it can be exercised from a console
harness the way LootMastr's `Planning` is.

## The one constraint everything follows from

**Beastmaster's game data is unnamed.** Everything Beastmaster is prefixed `XBM` internally —
nothing is called Beastmaster or Crucible anywhere in the data — and EXDSchema's `latest` branch
names almost none of the columns. Worse, where it does name them it is **out of date**: its
`XBMPet` is a 12-column sheet with an 11-wide resistance array, and the real one has 27 columns and
no resistance array. Its `XBMBattleDetailAction` has the columns in the wrong order.

Consequences, and they shape the whole plugin:

- Sheets are read **raw, by column index**. Lumina's generated structs only expose the handful of
  columns somebody upstream has named, so they are useless here.
- Every index lives in `Data/XbmColumns.cs` and nowhere else, so a patch that shifts one breaks in a
  single place.
- Every index is derived, never assumed, and how it was derived is written down below. An index with
  no evidence behind it is a guess and will break silently.
- Record the column count of every sheet used. A patch that inserts a column has to fail loudly
  rather than quietly producing wrong traits.

## What the sheets actually contain

Read out of the installed game files with `Harness/`, against game version **2026.09.01.0000.0000**.
None of it needed a client running. **The EXDSchema definitions are stale and partly wrong** — check
here, not there.

### XBMPet — 51 rows, 27 columns

The capturable beasts. EXDSchema describes a 12-column sheet with an 11-wide resistance array; the
real sheet has 27 columns and no resistance array at all.

| Col | Type | What |
|---|---|---|
| 0 | Int32 | `Pet` row id — the beast's name lives there |
| 1 | UInt8 | Classification, 1..8. 1 is Beastkin; the other names are not in the data |
| 2 | UInt8 | Location id, read against column 6 |
| 3 | UInt8 | Beast rank, 1..5, distributed 7/26/12/2/4. Inferred by elimination, not seen |
| 4 | UInt32 | Icon, 242001 upward |
| 5 | UInt16 | An `Action` row id, but those rows carry no name. Unconfirmed |
| 6 | UInt8 | Location switch: 0 → `PlaceName`, 1 → `ContentFinderCondition` |
| 7 | UInt16 | Mostly 30/42/54, one 350. Level or content id. Unconfirmed |
| 8 | String | Flavour text |
| 9, 10 | String | The Trick and the Tempered Release descriptions, in that order. No names |
| 11..21 | Bool ×11 | **Which status the beast inflicts.** The feature this plugin is built on |
| 22..26 | UInt8 ×5 | Five numbers, 1..100. **Not identified** — see below; they are not the stats |

Columns 11..21 follow `BNpcResist`'s indexer, which is itself 11 bools over 256 rows — so slot n is
column 11 + n. `Rules/BeastStatus.cs` names them.

**This is much better news than the plan assumed.** The bestiary filter — which of my beasts
sleeps, poisons, stuns, **interrupts** — comes straight out of these eleven bools. No hand-curated
table is needed for any of it. Interrupt in particular is slot 3, not something that has to be
parsed out of prose. What is genuinely absent is cleanse and dispel: vulture "dispels one
beneficial status" and bat "removes a status ailment", and neither has a bool. Those two, and only
those two, need the description strings or an override file.

### The eleven status slots

The names are the game's own. The board detail window carries the full legend in its AtkValues,
indices 40019 to 40029:

> Slow · Petrification/Freeze · Paralysis · **Interruption** · Blind · Stun · Sleep · Bind · Heavy ·
> Flat Damage/Death · Poison

Which slot each belongs to was worked out separately, before the legend turned up, by correlation:
list every beast that sets a column beside its action descriptions and read off the common factor —
the only three beasts setting column 13 are the three whose actions paralyse. The two agree, and
between them every slot is now named:

| Slot | Status | Evidence |
|---|---|---|
| 0 | Slow | apkallu, antling, morbol |
| 1 | Petrification/Freeze | ziz, cobra petrify; chimera deep freezes — hence the double name |
| 2 | Paralysis | opo-opo, coeurl, morbol |
| 3 | **Interruption** | coblyn, dullahan, golem, spriggan, ice golem |
| 4 | Blind | dodo, worm, morbol |
| 5 | Poison | diremite, wespe, flying trap, uragnite |
| 6 | Stun | buffalo, alone |
| 7 | Sleep | lamb; treant causes nightmares |
| 8 | Bind | diremite, slime |
| 9 | Heavy | mandragora, worm; goobbue sickens — the slot covers both |
| 10 | Flat Damage/Death | ghost, rafflesia doom; Karlabos cuts HP to a single digit |

**Slot 3 had been inferred as Silence and that was wrong.** The five beasts setting it inflict
nothing in either description the sheet carries, which is consistent either way — but the legend
has no Silence in it at all, and Interruption is the name left over. It also explains those five:
their interrupt is the Borrow the sheet does not hold.

The legend's order is *not* the storage order. It runs down the slots but moves Poison from 5 to
the end. `BeastStatusNames.DisplayOrder` keeps the legend's order, because that is the one the
player already knows from the window.

### The small sheets, in full

- `XBMElement` — 9 damage types: Fire, Wind, Earth, Lightning, Ice, Water, Blunt, Piercing,
  Slashing. Every value carries a leading space; trim it.
- `XBMActionTarget` — Self, Ground, Highest Enmity, Random, Player, Allies.
- `XBMActionEffectType` — Single Target, Front, Rear, Front/Rear, Lateral, Circle, Ring,
  Circle/Ring, Universal, Cross. **AoE shapes, not effect categories**, despite the name.
- `XBMItemType` — Beast Gear, Crucible Item, Feed.
- `XBMScoreRank` — Legendary, Apex, Elite, Renowned, Exemplary, Adept, Journeyman, Novice,
  Apprentice.
- `XBMScoreBonus` — 33 rows, each a name and its condition ("Clear the board with no incapacitated
  familiars").
- `XBMStageEventType` — 9 rows, one UInt8 each: 0,0,3,2,1,5,6,4,7. A remap of some kind; the room
  kinds are not spelled out here.
- `XBMEntrance` — 6 rows: id, flag, and a UInt32 stepping 71030..71037 (a Level or EObj id).
- `XBMItem` — 206 rows: icon, price, singular/plural, display name, full effect text, short text.
  Fully readable, no reverse engineering needed.

### XBMBattleDetailAction — 181 rows, 4 columns

**Column order differs from EXDSchema**, which lists Action, Status, ActionTarget, ActionEffectType.
The types settle it: the two middle columns are UInt8 and index sheets of 7 and 11 rows, while
status ids are UInt32 and appear last.

`0 Action(UInt32) · 1 ActionTarget(UInt8) · 2 ActionEffectType(UInt8) · 3 Status(UInt32)`

### XBMContent — 6 rows, 37 columns

One row per board (five real, plus the empty row 0), keyed by `ContentFinderCondition` 1088..1092.
Columns 1..3 are three small numbers per board (10/3/5, 12/8/10, 14/13/15, 12/18/20, 15/23/25).
**Columns 4..36 are 33 wide and line up one for one with `XBMScoreBonus`'s 33 rows** — the points
each board pays for each bonus, 0 where it does not offer it.

## What is still open

1. **Beast rank.** Column 3 is inferred by elimination and still unconfirmed. The bestiary detail
   page does have a Rank field — node 39 label, node 40 value — but the whole panel holding it,
   node 36, is **hidden**, along with EXP, HP, Satiety and five stat components at 47..51. It is
   not that the value is missing; the panel is not displayed on this page at all. Confirming
   column 3 needs whatever context does display it.
2. **The habitat column.** Column 2 is not a `PlaceName` id and not a fixed offset from one:
   goobbue's 21 is Lower La Noscea (`PlaceName` 31), golem's 25 is Southern Thanalan (45), coblyn's
   43 is Western Thanalan. All 7912 sheets were searched for a direct or numeric link and came back
   empty, so it resolves through something not yet found.
3. **Columns 22..26.** Five numbers, 1..100, disproved as the stats. Unknown.
4. ~~Classification names.~~ Answered: `Addon` row 17740 plus the value — Beastkin, Vilekin,
   Cloudkin, Seedkin, Wavekin, Scalekin, Soulkin, Ashkin, each cross-checked against a beast known
   to be one. Being in `Addon` means they arrive already localised.
5. **Auto-attack damage type.** Shown on the detail page, and in all three captures it equalled the
   element of that beast's Trick — golem earth, coblyn lightning, goobbue blunt. Three samples is a
   hypothesis, not a rule.

Answered, and no longer open: the eleven status slots, the three actions and where their names
live, what Borrow is, whether the notebook's list recycles.

## Verified against the installed Dalamud, not guessed

Dalamud 15.0.3.3, Lumina 7.6 / Lumina.Excel 7.5.1, FFXIVClientStructs as shipped with it.

Raw sheet access — `RawExcelSheet` itself is nearly bare (`Count`, `Columns`, `HasRow`,
`GetColumnOffset`; no indexer, no enumerator, no `GetRow`), so raw reading goes through the typed
sheet over `RawRow` instead:

```
Services.Data.Excel                                  → Lumina.Excel.ExcelModule
ExcelModule.GetSheet<RawRow>(null, "XBMPet")         → ExcelSheet<RawRow>   (enumerable, Count, GetRowAt(int), TryGetRow)
RawRow.RowId                                         → uint
RawRow.Columns                                       → IReadOnlyList<ExcelColumnDefinition>
RawRow.ReadColumn(int)                               → object
RawRow.ReadStringColumn(int)                         → ReadOnlySeString      (use ExtractText())
```

Addons — `Services.GameGui.GetAddonByName(name)` → `AtkUnitBasePtr` for `AtkValues`; ECommons'
`GenericHelpers.TryGetAddonByName` for the raw `AtkUnitBase*` needed to walk nodes.

Compiling against the installed assemblies is the cheapest way to check any of this: write the
member into a scratch file with a deliberately wrong type and read the real type out of the
compiler error. That is how every signature above was pinned down.

## Field initializers run before the services exist

Anything that touches a Dalamud service on construction must be built **inside the constructor
body, after `pluginInterface.Create<Services>()`** — never as a field initializer. Field
initializers run first, while every `[PluginService]` property is still its `null!` placeholder, so
a class that subscribes to `Services.Framework.Update` in its constructor throws there and the
plugin fails to load outright with "Failed to create BeastMastr.Plugin (ctor invocation)".

`DelayedSweep` was written as `= new()` and did exactly that. `WindowSystem` is fine as an
initializer because it only stores a name.

## FFXIVClientStructs already knows the Beastmaster UI

Confirmed present, which is why none of it has to be found by hand:

- Addons: `XBMMonsterNotebook`, `XBMPetParty`, `XBMStageMap`, `XBMStageList`, `XBMStageDetailList`,
  `XBMBattleMonster`, `XBMBattleMonsterDetail`, `XBMContentsMainHUD`, `XBMItemDetail`,
  `XBMRanking`, `XBMResult`.
- `AtkComponentXBMContentStageEventMap` with `Entries[]`, each exposing `EventMapEntryIndices`,
  `Components`, `TimelineStates` and `IsCurrentEvent` — per-room nodes *and* which room is current,
  handed over directly. This is what the board overlay anchors to.
- `AtkComponentXBMItem`; `XBMModule` / `XBMNoteModule` via `UIModule.GetXBMModule()`.

There are no `AgentXBM*` structs, so those agents are reached generically.

## Node walking

`Data/AddonReader.Walk` follows `PrevSiblingNode`, not `NextSiblingNode`: a node's `ChildNode`
points at its **last** child and the chain runs backwards from there. Component nodes (type id
≥ 1000) keep their children behind their own `UldManager.RootNode` rather than in `ChildNode`, so
they are followed separately — without that, list rows and the board's room entries never appear
in the dump at all.

## Captures

Sheet dumps are reproducible from `Harness/` and are not kept here; the tables above are their
result. What belongs here is anything read off a **live client**. Raw dumps land in `captures/` at
the repository root and are gitignored — the findings are what gets written down.

The client these were taken on runs in **English**, so the strings below are the game's own.

### Master's Bestiary, overview page — 2026-09-09

Taken with all fifty beasts captured ("Beasts Captured 50/50"), sitting on the grid overview.

**The five stat columns name themselves.** The window's own column headers arrive in the
AtkValues, which is what settles `XBMPet` columns 3 and 22..26:

```
15 "Beast Rank"  16 "Strength"  17 "Intelligence"
18 "Phys. Resistance"  19 "Mag. Resistance"  20 "Constitution"  21 "No."
```

Cross-checked: the plugin's own dump of `XBMPet` from the running client matches the offline
`Harness/` dump value for value — Cu Sith 100/91/100/79/100, lamb 82 across, ranks 3 and 4. So the
offline route can be trusted for the rest.

**The grid pages, and node ids are slots.** The overview is a fixed five by five block of
`Component/Base` tiles, node ids **27..51**, laid out in reverse — id 27 carries "No. 1" and id 51
carries "No. 25". Fifty beasts through twenty-five tiles means the same node id shows a different
beast depending on the page.

That answers the question this capture was taken for: **badges cannot be attached to a node id and
left there.** They have to be recomputed whenever the page changes and keyed to whatever beast is
in the slot at the time.

Reading which beast that is does not need the node tree. The AtkValues carry a block of eight per
slot starting at 24, and the fifth of each block is the icon id — 242001, 242002, … — which is
exactly `XBMPet` column 4. **The icon id is the join key** between a tile and its beast.
`Data/XbmColumns.MonsterNotebook` holds the arithmetic.

Also in the tree, and worth knowing before it wastes anybody's time: the `Component/List` with the
`ListItemRenderer` children (node ids 4 and 41001..41011) is **hidden**. It belongs to the "Team
Composition" dropdown, not to the bestiary, and is not where the beasts are.

Still to capture from this window: a detail page, which is where the third action and status slot 3
should become readable, and a second overview capture on a different page to confirm the slot
mapping holds.

### Beast detail page — 2026-09-09

The bestiary is one window: the grid on the left, the selected beast's detail on the right, always
populated. Goobbue, No. 30, read off the screen:

```
Classification  Beastkin          Appearance   S [M] L
Auto-attack     Blunt             Natural Habitat  Lower La Noscea
Borrow (Lv. 22)           Beastskin
Tempered Release (Lv. 18) Moldy Sneeze   Area of Effect
   Deals unaspected damage that sickens enemies.
Trick (Lv. 8)             Beatdown       Area of Effect
   Delivers a blunt physical attack.
```

Against `XBMPet` row 30: column 1 is 1, and the page says Beastkin. Column 9 is "Delivers a blunt
physical attack." — the Trick — and column 10 is "Deals unaspected damage that sickens enemies." —
the Tempered Release. **So the three actions are Trick, Tempered Release and Borrow, and the sheet
holds the first two in that order.** Borrow is the one that is missing, and no action *name* is in
the sheet at all.

"Area of Effect" beside each action is `XBMActionEffectType`, which confirms that sheet is the AoE
shape rather than an effect category.

Goobbue's only status flag is column 20, slot 9, and its Tempered Release sickens. Mandragora and
worm sit in the same slot and inflict heaviness. So **slot 9 is broader than plain Heavy** and
covers sicken as well.

The grid's roman numerals are a trap: tiles I, II and III sit on beasts whose column 3 reads 3, 3
and 4, while a beast at 3 carries none. They are Battlehorn slot assignments — player state — not
anything from a sheet.

### The Crucible board — 2026-09-09

Taken on the board **selection** screen, not inside a run: the values carry "Challenge This Board",
"Level: 50 (Sync to 50)" and the entry costs.

**`XBMStageMap` holds position, not content.** Its own AtkValues are two numbers. What it has is a
`Component/XBMContentStageEventMap` at the top of its tree, and under that a flat set of
`Component/Base` children carrying no text at all:

| Node ids | What |
|---|---|
| 30001 upward | the rooms — 50x50 tiles, three columns 60px apart, 13 visible |
| 40001 upward | straight links between two rooms |
| 50001, 70001 upward | the two diagonal link graphics |
| 60001 upward | all hidden in this capture |

Room tiles sat at x ∈ {1651, 1711, 1771} and y from 395 to 935 in steps of 60 — a single column down
the middle that forks left and right three times and rejoins. **The overlay anchors here**, and
because the tiles carry no text, everything a card says has to come from the detail list.

**`XBMStageDetailList` holds the content**, as one block of forty AtkValues per room, listed from
the last move backwards. Within a block: `+2` the move number, `+4` its "Move 12" label, `+5` the
room kind, `+7` the description. A whole board read out of one capture:

```
Move  1  Enemy          Enemy #1: Combat 2 types of beast.
Move  2  Enemy          Enemy #2: Combat 2 types of beast.
Move  3  Shop           Shop #1
Move  4  Enemy          Enemy #3: Combat 1 type of beast.
Move  4  EliteEnemy     Elite Enemy #1: Combat 3 types of beast.
Move  5  Campsite       Campsite #1: Recover HP for yourself and up to 3 familiars.
Move  5  Treasure       Treasure #1
Move  6  Enemy          Enemy #4: Combat 2 types of beast.
Move  7  Shop           Shop #2
Move  7  Treasure       Treasure #2
Move  8  EliteEnemy     Elite Enemy #2: Combat 3 types of beast.
Move  9  Treasure       Treasure #3
Move 10  Campsite       Campsite #2
Move 10  Random         Random Enemy or Treasure #1: Take your chances.
Move 11  Shop           Shop #3
Move 12  Boss           Boss of the Board: Combat 2 types of beast.
```

A move listed twice is a fork. The kind ids run Enemy 0, Elite Enemy 1, Boss 2, Shop 3, Campsite 4,
Treasure 5, Random Enemy or Treasure 6 — every one of them read off a description that names it.

What is **not** here is the thing the room cards most need: which beasts an enemy room actually
holds, and their weaknesses. "Combat 2 types of beast" is all the selection screen says. That
either arrives once a run is under way, or lives in `XBMBattleMonster` / `XBMBattleMonsterDetail`.

### The team roster — 2026-09-09

**`XBMPetParty` is the window that shows a beast's statuses.** The bestiary does not, which is why
looking there for a mark against coblyn finds nothing — the detail page has Classification,
auto-attack, habitat and the three actions, and no status flags at all.

One block of 77 AtkValues per roster slot, from index 9. Within a block: `+0` the name, `+15` five
label/value stat pairs (STR, PHY R, CON, INT, MAG R), `+44` eleven bools, `+56` the eleven status
names in the same positional order as those bools. The window pairs them for you, so read the label
rather than assuming an order.

**This confirms the slot mapping against the game itself**, and it is the second independent
confirmation after the legend:

| Beast | Party window says | `XBMPet` slots | Agrees |
|---|---|---|---|
| dullahan | Interruption | 3 | yes |
| diremite | Bind, Poison | 8, 5 | yes |
| slime | Bind | 8 | yes |
| pugil | nothing | none | yes |

It also proves the display order is genuinely different from the storage order rather than a
misreading: diremite's flags sit at display positions 7 and 10, which the labels call Bind and
Poison, while its sheet columns are slots 8 and 5.

**And it disproves what this file said about columns 22..26.** Those were labelled Strength,
Intelligence, Phys. Resistance, Mag. Resistance and Constitution on the strength of the bestiary's
column headers arriving in that order. The numbers do not match:

| Beast | `XBMPet` 22..26 | Party window |
|---|---|---|
| dullahan | 97 97 97 97 97 | STR 109, PHY R 80, CON 100, INT 67, MAG R 80 |
| diremite | 100 100 100 100 100 | STR 134, PHY R 83, CON 137, INT 83, MAG R 83 |
| cu sith | 100 91 100 79 100 | STR 140, PHY R 82, CON 113, INT 87, MAG R 78 |

Twenty-eight of the fifty beasts have all five equal, which no stat spread would do, and slime has
a 1 among four 97s. Whatever these columns are, the stats are computed elsewhere.

The mistake is worth naming because it was made twice in a row here: **a window's column headers
prove the window has those columns, not that a sheet column is one of them.** Both times the
reasoning was "five headers, five leftover columns, in order". Both times it needed a value to
check against, and only the second one had it.

### Capturing windows that only exist on hover

The shop, item descriptions and an enemy's detail panel close the moment the cursor leaves them, so
no button can capture them. `Data/DelayedSweep.cs` arms a timer instead: press, put the cursor
back, hold it, and the sweep fires on its own.

The sweep no longer walks the hand written window list either. `AddonReader.OpenAddonNames` asks
`RaptureAtkUnitManager` for every loaded unit whose name starts with `XBM`, so windows nobody has
named yet — the shop among them — are swept without having to guess what they are called. Anything
not in `BeastmasterData.Addons` is flagged in the file as worth naming.

### Enemies, and the rest of the windows — 2026-09-09

Letting the game list its own loaded windows immediately turned up three nobody had named:
**`XBMMonsterBookDetail`** (the bestiary's right hand page is a separate window from the grid),
**`XBMPetActionDetail`**, and **`XBMContentsItemShop`**. That is the argument for
`AddonReader.OpenAddonNames` in one line.

#### `XBMBattleMonsterDetail` is the room card, already written

Two AtkValues, and everything else in the node tree as plain text. For one enemy:

```
[21] Manticore Piece
[22] Weakness:        [23] Wind
[24] Vulnerabilities:      (icon nodes 13..18, no text)
[27] Strength ★★★   [28] Phys. Resistance ★★★   [29] Constitution ★★★★
[30] Intelligence ★  [31] Mag. Resistance ★★
[35] Hammerleap     Target: Ground   Damage Type: Blunt
                    Interruption: Ineffective   Area of Effect: Circle
                    Status: Petrification       (hidden: "Nullification ✓")
[39] Deadly Hold    Target: Player   Damage Type: Blunt
                    Interruption: Ineffective   Area of Effect: Single Target
                    Status: Paralyzing Spikes   (hidden: "Nullification ✓")
```

Every single thing the room cards were specified to show is in there: the damage type weakness, the
enemy's own damage type, what status it inflicts, and — per action — **whether it can be
interrupted at all**. The hidden "Nullification ✓" is the game saying the current team already
covers that status, which is a recommendation signal for free.

The catch is that this window only exists while the cursor rests on an enemy. It cannot be fetched
on demand, so the plugin has to hook it, read it while it is up, and cache per enemy. That is
tolerable — hovering enemies is what you do on the board anyway — and it is the only route until
something is found that lists a room's enemies without hovering.

#### The three actions, resolved

The bestiary detail page names them and groups them: **Trick**, **Tempered Release**, **Borrow**.

**Borrow is per Classification, not per beast.** Golem and coblyn are both Soulkin and both borrow
Soul Crush; goobbue is Beastkin and borrows Beastskin. So it follows from column 1 and needs no
column of its own. That closes the "missing third action" question — nothing is missing.

**The names come through the `Pet` sheet**, which `XBMPet` column 0 already points at. `Pet`
column 1 is the Trick's `Action` row and column 2 the Tempered Release's; column 19 points back at
the `XBMPet` row, verified for all fifty in both directions. Columns 3 and 4 are the same two
actions for every beast — "Aetheric Burst" and "Threaten" — so neither is the Borrow.

The ids also happen to sit at `44933 + 2 × XBMPet row id` for all fifty, which is how they were
first found, from the two ids the party window hands out at its offsets 29 and 30. **Use the link,
not the arithmetic.** A computed id is a coincidence of today's ordering and would shift silently
the moment a patch inserts a beast; a link would not.

Classification values so far: **1 Beastkin, 7 Soulkin**.

#### What the bestiary page does not have

No status flags, which is the answer to looking there for coblyn's Interruption and finding
nothing. It has the number, name, classification, appearance sizes, auto-attack type, the three
actions with their levels and descriptions, habitat, flavour text, and labels for Rank, EXP, HP and
Satiety whose value nodes were empty in every capture.

## Phase 1: how a beast is assembled

`Data/BeastCatalog.cs` reads, all raw by column index:

- `XBMPet` for the classification, icon, the eleven status bools and the two action descriptions.
- `Pet`, via column 0, for the two action ids — never by arithmetic.
- `Action` for the names, including the Borrow at `44895 + classification`.
- `Addon` at `17740 + classification` for the classification's own name.

**It reads `XBMPet` twice: the player's language to display, English to classify.** Cleanse and
dispel exist only as prose in the descriptions, and matching prose in eleven languages would be
eleven chances to be wrong. Nothing derived from the English text is ever shown.

`Rules/` holds the result and knows nothing about Dalamud: `Beast`, `BeastAction`, `DamageType`,
`BeastTrait`, the `TraitClassifier` that reads a description, and `BeastFilter`. The filter treats
several selected statuses as **and**, not or — "which of mine sleeps *and* is a Wavekin" is the
question worth asking, and an any-of filter cannot ask it.

87 of the 150 actions carry a damage type. The rest deal no damage at all, which is the right
answer for "Hastens allies."

### Interruption is exactly the Soulkin

All five Soulkin interrupt and nobody else does, because Interruption comes from Soul Crush, the
Borrow every Soulkin shares. So the interrupt filter has the same answer as picking a Soulkin —
five beasts of fifty. Still worth offering, but it is a smaller fact than it first appeared, and
the harness asserts the equivalence so a patch that breaks it surfaces the reason.

## Phase 2: writing into the game's bestiary

`Native/MonsterNotebookDecorator.cs` attaches a KamiToolKit `TextNode` to each of the twenty-five
tiles of `XBMMonsterNotebook`, showing that beast's status tags, and dims every tile the shared
`BeastFilter` excludes. Turning it off in Settings hands the window back exactly as the game draws
it the next time it opens.

**A tile is a slot, not a beast.** Fifty beasts page through twenty-five fixed node ids, so a tag
attached once and left alone follows the slot and starts describing the wrong beast the moment the
page turns. Everything is recomputed on every `PostRefresh` and `PostRequestedUpdate`, keyed to
whichever beast the window currently has there — read from the icon id in its AtkValues, the only
thing it gives out that identifies a beast, and one for one with `XBMPet`'s icon column.

Dimming writes `AtkResNode.Color.A` on nodes the game owns, so it is undone in three places: when a
slot turns out to be empty, on `PreFinalize`, and on plugin unload while the window may still be
open. A node left attached or a tile left dimmed outlives the plugin.

Badge node ids start at `0x42450000`, far above the window's own, which run to the low fifties.

**The addon events are not enough on their own.** They fire when the window does something, and a
window that is already open and sitting still says nothing — so enabling the plugin, or the
setting, with the bestiary open would leave it bare until it was closed and reopened. A throttled
framework tick covers both directions: it attaches when there is an open window and nothing on it,
and detaches when the setting goes off and no addon event is coming to notice. Once decorated it
costs a field read per frame, because it returns immediately while badges exist.

### KamiToolKit must be initialised first, or it crashes the game

`KamiToolKitLibrary.InitializeAsync(pluginInterface)` has to have **completed** before a single
node is constructed, and `KamiToolKitLibrary.Dispose()` belongs in the plugin's own dispose, after
the nodes are detached. Other plugins announce this in the log — "KamiToolKit initialized for
GlamourLog" — which is the clue that was there to be read.

Skipping it does not fail politely. `new TextNode()` throws inside `NodeBase`'s constructor, from
its internal service locator, so no reference ever comes back — the runtime is left holding a
half-built object it can only clean up in a finalizer, and **that finalizer dereferences null and
takes the game down**. The crash lands on the .NET finalizer thread, minutes later, pointing at
`NodeBase.Finalize` with nothing about this plugin in the stack.

Two guards follow from that, and both matter more than they look:

- Nothing native is built until the initialisation task reports success.
- Anything thrown while decorating sets a `broken` flag that is never cleared. Retrying a failing
  node constructor twenty-five times a frame turns one mistake into a crash; one failure disables
  the feature for the session and logs why.

### KamiToolKit, verified against 2.2.38

There is no `NativeController`. Attaching is on the node:

```
NodeBase.AttachNode(AtkResNode*, NodePosition)   // and an AtkUnitBase* overload
NodeBase.DetachNode()
TextNode.String        → ReadOnlySeString, assignable from a string
TextNode.Position/Size → Vector2
TextNode.TextColor     → Vector4
```

`NodePosition` lives in `KamiToolKit.Enums`, the nodes in `KamiToolKit.Nodes`, and `NodeBase` in
`KamiToolKit.BaseTypes`.

One trap: the AtkValue type enum is `AtkValueType`, not `ValueType` — and with `using System;` in
the file, a bare `ValueType` silently resolves to `System.ValueType` and the error blames the wrong
thing.

## Phase 3: room cards on the board

The board splits cleanly, and the split is what makes this tractable.

**The board is drawn by two different windows.** `XBMStageMap` is the board *selection* screen
before a run; during a run, "Board Layout" is `XBMStageDetailList`, which embeds the same
`XBMContentStageEventMap` component. Looking only at the first meant the overlay never appeared
inside a run at all. `StageMapReader` now finds the component in whichever window has it.

`Data/StageMapReader.cs` answers only *where*. The window carries two AtkValues and no text at
all, but its `AtkComponentXBMContentStageEventMap` hands out, per board position, an entry index, a
component, and whether it is the current room — as four parallel `Span`s on each `Entry`. **So the
pairing between a tile and a room is given, not guessed from where the tiles sit.** The component is
found by walking the window for `ComponentType.XBMContentStageEventMap` rather than by node id;
the id was 2 in the capture, but a component located by what it is survives a layout change.

`Data/StageDetailReader.cs` answers *what*. `XBMStageDetailList` is open whenever the board is, so
nothing has to be cached. Each room's sentence is split at its colon — "Elite Enemy #2" and
"Combat 3 types of beast." — which keeps both halves in the player's language for free, and is why
there is no table of room names anywhere in this plugin.

`UI/BoardOverlay.cs` draws a card under each floating icon, carrying the room's kind and the game's
own sentence about it. **It draws nothing while the board window is open** — that window already
shows its rooms with its own labels, and a second set of cards over the top is clutter in front of
something that does not need them. Cards are for the board you are standing on.

It is an overlay rather than injected nodes on purpose. The same readers will drive native nodes in
the Kür, and building against a separate model first is what makes that swap cheap.

### The Crucible board *is* the overworld

Worth stating plainly, because it changes what the feature is: a run is not played from a map
screen. You walk across the board — each room is a physical platform with an icon floating above it
— and "Board Layout" is a window you open on top of that. So the cards belong on the floating icons
first and on the board window second, which is the opposite of the order this was built in.

### The overworld markers are neither an addon nor an object

The room markers in the Crucible overworld — where the cards are actually wanted — are not any of
the things checked so far. The object table holds nothing but the player out there,
`XBMContentsMainHUD` is buttons and the item bar, and in the overworld neither board window is
open. Listing every loaded window in the overworld ruled out a window as well: nothing is open there that
could be drawing them.

It is the **map's marker list**. The icons show on the minimap as well as in the world,
which is what map markers do and what nothing else does. `Data/MapMarkerReader.cs` reads
`AgentMap`'s `MapMarkers` and `TempMapMarkers`, both of which carry an icon id, a map X and Y and a
subtext. Deliberately no conversion to world coordinates yet: map units and world units are not the
same, and the diagnostic prints the raw numbers beside the player's known world position so the
relationship can be established from data rather than assumed.

Two numbers in `XBMStageDetailList` were also being read wrong, and wrong in the quiet way: move
and kind arrive as `UInt`, and `AtkValuePtr.TryGet<int>` refuses a `UInt` rather than converting
it, so every room came back as move 0 of kind Enemy with nothing failing anywhere. Both types are
now accepted.

### Reading the enemy panel

`Data/BattleMonsterReader.cs` reads `XBMBattleMonsterDetail`: name, weakness, five stats as star
counts, and per action its name, target, damage type, area of effect, the status it inflicts,
whether it can be interrupted, and the game's own hidden note that the current team already covers
that status.

**Action blocks are found by shape, not by node id.** A component that has both a name and something
to say about interrupting it is an action block. The ids were 35 and 39 for a two-action enemy, and
hardcoding those would silently drop a third.

`Data/EnemyCache.cs` keeps what it read, because the panel does not stay: it exists only while the
cursor rests on an enemy, so the information you want on a card is visible exactly when you no
longer need a card. Hovering enemies is what you do on the board anyway, so it fills during normal
play. In memory only — a run's enemies mean nothing after it ends — and a newer reading replaces an
older one, because the nullification note reflects the team you have right now.

### The enemies are in the board window, in the node tree

This was got wrong first, and the mistake is worth naming because it is a general one: the room
list's **AtkValues** carry only the move, the kind and a sentence, so the conclusion was that
enemies were not in the window at all and had to be caught from the hover panel. They are in the
window — in its **node tree**, which is a different place entirely. Checking one and concluding
about the other cost a detour through mouse-position attribution that has now been deleted.

`XBMStageDetailList` holds two `AtkComponentList`s: the rooms, and the enemies of whichever room is
selected. The room list's `SelectedItemIndex` says which room the enemies belong to, so clicking
through the rooms — what you do to read a board anyway — fills everything in with no hovering.

Inside an enemy row, the label/value pairs are told apart **by their values, not their labels**: a
value made only of stars is a stat, the one that is not is the weakness. Labels are localised;
shapes are not.

### Two sources, because neither is complete

The room list gives which enemies are in which room, plus their weakness and stats. It does not give
what they *do* — the statuses they inflict and whether their actions can be interrupted are only in
the hover panel. So `EnemyCache` reads both: the room list by selection, the panel by name, merged
on the card. A room only selected shows its enemies and weaknesses; one also hovered shows the rest.

### Still to come

Both `Recommended` modes: they can now be built on `EnemyCache`, matching a room's weaknesses and
statuses against what the roster's beasts inflict. And Phase 4, the native cards.

### The rooms are map markers, and their icons name them

A capture in the overworld settled it. Among the zone's own landmarks — Bentbranch, Haukke Manor
and the rest of Central Shroud, since the board sits inside it — the first twelve markers are the
board: icon ids 63850..63856, at map X of exactly -320, 0 or 320, which is the three columns.

Laid against the minimap screenshot, the rows matched one for one, and that fixes the icons:

| Icon | Room |
|---|---|
| 63850 | Campsite |
| 63851 | Shop |
| 63852 | Treasure |
| 63854 | Enemy |
| 63855 | Elite Enemy |
| 63856 | Boss |

63853 is the gap; no board seen so far has offered a Random Enemy or Treasure room.

**Sorting the markers by map Y ascending and then map X descending reproduces the room list
exactly** — boss, shop, campsite, treasure, elite, and on down — and every kind the icons imply
agreed with the kind the list gives. So a marker's position in that order is its index into the
room list, and the two halves join without anything being guessed.

Map units are sixteen to the yalm, offset by the map's own origin, so world = raw / 16 minus
`AgentMap`'s `CurrentOffsetX` / `CurrentOffsetY`. Markers carry no height at all; the player's own
is used, which is right for a board walked across on the flat.

## The world transform

`world = raw / (16 × sizeFactor / 100) + offset`

Both halves were wrong once, and both were settled by measurement rather than argument — which is
the point worth keeping, because each error looked like the other from the outside.

**The sign.** The board's middle column is map X zero against an offset of -700, and the player
walks that column at world X of about -700, so the offset is added. Subtracting it put every card
seven hundred units the other way, which in game looked like every icon being far off to one side
whenever the camera turned.

**The scale.** With a plain sixteen the middle of the board sat almost right while everything else
spread too far out. That is a scale error and not an offset one, and the shape of the complaint said
so. This map's size factor is 400, so the divisor is 64 — and the decisive measurement was standing
in the boss room's trigger, which puts the player at Z -74.33 where a divisor of 64 places the boss
marker at -75.00. A normal zone has a size factor of 100 and gives back the plain sixteen that the
community formula quotes.

### What it looked like when it was wrong The board's columns sit at map X of -320,
0 and 320, which divides to -20, 0 and 20, while the player walks the middle column at world X of
about **-699.9**. So every card lands roughly seven hundred units east of the board, which shows up
in game as the icons all appearing far away when the camera turns.

Either the offsets are zero here and the board's markers are in some other origin, or the board
carries its own map. `MapMarkerReader.DescribeTransform` now prints `CurrentMapId`,
`CurrentTerritoryId`, `CurrentMapSizeFactor` and both offsets beside the player's world position, and
the marker table shows each computed world position and where it projects to. One capture in a run
settles it — this is not worth another guess.

## Automation: what it needs before it can be written

Four modes are settled and recorded in `Configuration`, and deliberately **not** offered in Settings
until they do something — a switch that does nothing is worse than no switch.

| Mode | What it does |
|---|---|
| `TeamMode.Leveling` | Fill the roster with the least advanced beasts, so the ones needing experience get it |
| `TeamMode.Recommended` | Fill for the board. Waits on enemy data |
| `FightMode.RepeatLast` | Take what the last fight took |
| `FightMode.Recommended` | Take what beats this enemy. Waits on enemy data |

The team screen is already mapped, which was the pleasant surprise: **the Master's Bestiary window
is the team composition screen**. Its AtkValues carry "Team Composition" and "50/50", its tiles are
node ids 27..51, and the icon id per slot already joins a tile to its beast. So filling a team means
clicking tiles in a window this plugin has measured.

### The progression rank is in the roster window, and the block was misaligned

`XBMPetParty` carries it, and finding it also uncovered a real error: **a block starts three values
before the name, not at it.** The icon is what proved it. Aligned on the name, every block's icon
read as the *next* beast's; aligned three earlier, every icon matches its own beast's `XBMPet` icon
column. So the constants were all off by three and any roster reading would have been quietly wrong.

Corrected, a block is: `+0` progression rank as a string, `+1` icon, `+3` name, `+18` five stat
pairs, `+47` eleven status flags, `+59` their labels, `+70` the sheet's own rank.

**The two ranks are different numbers.** Behemoth reads 5 at `+0` against a sheet value of 4 at
`+70`. The first is the one leveling has to sort on; the second is static and comes from
`XBMPet` column 3 — which incidentally confirms what column 3 is, after it was twice claimed and
once withdrawn.

### The rank is written with icon glyphs, not digits

Every rank read as zero while the captured text showed a bare number. The string is
`U+E0BC U+E036 5` — the game's own boxed-number glyphs from the private use area, in front of the
digit. They survive `ToString`, a plain `int.TryParse` refuses the lot, and the dump shows nothing
because the glyphs do not render. Only the digits are kept now.

**Anything read out of a game string may carry these.** The failure is silent and the evidence looks
like proof that the value is absent.

### One window, two jobs

`XBMPetParty` fills a run's team *and* a fight's call slots, and only its prompt says which — "Select
a team of familiars" against "Select familiars to call upon during combat". The prompt sits at a
fixed index past the end of the blocks, which is fragile, but the alternative is matching those
sentences and they are localised.

While it is asking for a fight, `+74` in each block is that beast's call slot — 0, 1 or 2 — or 3 when
it is not called. Diffing a capture before and after choosing three familiars showed exactly three
blocks change from 3 to 0, 2 and 1, which is what `FightMode.RepeatLast` has to record.

Also corrected: the `XBMPet` row is at `+76`, not `+70`. `+70` and `+71` are empty.

`FightMode.RepeatLast`'s reading half is done: the roster comes back with correct ranks, and after
choosing three familiars the three chosen slots read 0, 1 and 2.

**There is no bulk source for ranks.** The bestiary in Team Composition mode lists all fifty, but
its AtkValues carry nothing per beast beyond the icon — every slot's remaining values are identical.
The rank is on the **detail page**, one beast at a time, in the panel that is hidden while the
bestiary is merely being browsed and shown while a team is being assembled. That is why an early
capture found every value node empty and the rank looked unavailable: it was the wrong moment, not
the wrong node.

So `Data/RankWatcher.cs` learns instead of asking. It samples the detail page for whichever beast is
open and the roster window for the ten on a team, four times a second, and remembers what it sees.
Driving the window to read all fifty would mean fifty selections under the player's hands to answer
a question nobody has asked yet. What it has not seen it does not claim to know, and the Board tab
says how many of the fifty are known. Team sizes are ten for a standard board, then twelve
and fourteen. Only three boards are unlocked,
so `TeamPlanner.TeamSizes` holds three and anything beyond falls back to the smallest — under-filling
beats picking a beast that has no slot.

### Selecting is the risky half, so nothing is guessed

Sortr's notes are blunt about this: the first version built `AtkValue` payloads by hand, treated the
callback's return as success, and it did not work. Selection there went to ECommons' `AddonMaster`
wrappers instead — but there are none for the XBM windows, so that escape is not available.

The recorder answered it, though not the way it was aimed. `PreReceiveEvent` gives the event kind
and its own parameter — `ListItemClick` with param 0, rollover with 1, rollout with 2 — and not
which row was hit, so it does not identify a beast. What it did establish is that these windows are
driven as ordinary `AtkComponentList`s, and that list has `SelectItem(index)`, `SelectedItemIndex`
and `ListLength` in FFXIVClientStructs. **So selection is a method call, not a payload**, and the
whole hand-built-AtkValue hazard is avoided.

### What a click actually sends

Recorded, not derived. Clicking the first familiar makes `XBMPetParty` fire:

```
FireCallback  [0] Int=1  [1] Int=0
```

Command then row. **Selecting and deselecting fire exactly the same thing**, so it is a toggle and
not a set — which matters: sending it for a familiar already called would take it back out.

The first attempt used `AtkComponentList.SelectItem`, which moves the list's cursor and performs no
selection at all. Nothing errored; the window simply never took the familiar, and only the read-back
caught it. That is the whole argument for reading back rather than trusting a call to have worked.

`Automation/FightSelector.cs` is the only place in this plugin that changes game state. It picks
one row per six frames and reads the window back after each: a pick that did not take stops the run
and says so, rather than pressing on. It only acts when nothing is chosen yet, so it never overrides
a choice already begun.

**Which window is which is learned, not matched.** The prompt differs — "Select a team of familiars
to accompany you" against "Select familiars to call upon during combat" — but it is localised, so
hardcoding either sentence would work in one client and quietly misfire in every other. Instead the
first time familiars are actually called, whatever the window said at that moment is recorded as the
fight prompt. Sortr learns its retainer menu entries the same way and for the same reason.

Replacing a familiar reuses the freed slot rather than shifting the others up, so the slot number is
the order and the list position is not. The remembered order follows the slots.

`Data/EventRecorder.cs` stays. It listens on `PreReceiveEvent` for the selection windows and
records what the game sends when **you** click — event type and parameter, newest first. It watches
only and sends nothing. Once a real click is on record, replaying that is a known quantity rather
than a guess, and the plan is still to confirm success by reading the window back rather than by
trusting a return value.

## Leveling: the plan, not yet the filling

`TeamPlanner.ForLeveling` takes the beasts chosen to **carry** first and fills the rest with the
least advanced. Carrying is the whole reason it takes that parameter: a team of nothing but the
weakest levels them slowly or not at all, so a few strong ones do the work while the rest collect
the experience. At most three of them — three of ten already leaves seven levelling, and beyond that
the point of the exercise stops surviving.

Ties break on **who is already on the team first**, and only then on the bestiary number. That is
the difference between "fill the team" and "overwrite the team": among beasts of equal rank, keeping
the one already there changes nothing about how fast anybody levels, and swapping it changes
everything about how much the automation has to touch. A two-change adjustment became a dozen
without it, and every one of those is another chance for the window to refuse.

The number is still the last tie-break, so the same roster produces the same team twice. A plan that
shuffles under you is worse than one that is merely arguable.

The Beasts tab shows the plan and the ranks it rests on, because those are only as complete as what
`RankWatcher` has seen, and a team filled from half-known ranks is worth checking before it is
filled rather than after.

**What it cannot do yet is fill the team**, and the first attempt to record how says something
useful about the recorder. Every one of the 29 entries captured on the bestiary was `MouseOver` or
`MouseOut`: crossing a grid of tiles produces two events and two callbacks per tile, so a
sixty-entry buffer fills with cursor movement in under a second and pushes out the click it was
opened for.

The recorder now holds four hundred entries, drops mouse-move events outright, and marks the
callbacks that arrive within four milliseconds of one as hover-driven so they can be skipped by eye.
What is left is what a deliberate action sent.

### The bestiary's toggle

```
MouseDown  ->  FireCallback  [0] Int=7  [1] UInt=slot
```

Command **7** with the grid slot, recorded from a real click. `[5, slot]` and `[6]` are the cursor
entering and leaving a tile, and they fire every ten milliseconds while it merely rests there — which
is why the click had to be found by looking for the one command that was neither.

**The fight window's toggle is `[1, row]` and the bestiary's is `[7, slot]`.** Two windows, two
commands, no shared convention — which is the whole argument for recording each rather than
generalising from the first.

Turning the page is `[3, page]`, page counted from zero, recorded the same way. So the bestiary
answers three commands, all of them read off real clicks:

| Command | Meaning |
|---|---|
| `[3, page]` | turn to a page |
| `[5, slot]` / `[6]` | the cursor entering and leaving a tile — noise, fires every 10 ms |
| `[7, slot]` | put the beast in that tile into or out of the team |

`Automation/TeamSelector.cs` uses them. It acts only while the bestiary and the roster are both
open, because that pairing is what putting a team together looks like and it beats matching a
localised prompt. Removals go before additions, since a full team refuses one more. Each toggle is
verified by reading the roster back. A beast on the other page turns the page and requeues itself
rather than being skipped — the page is read back from the number in the first tile rather than
remembered, because the player can turn it too.

## The world cards are off by default

The placement is right — they sit on the platforms and follow the camera. But a card hanging under
every floating icon across a whole board is not a good way to read a board, which is worth admitting
rather than shipping on. The readers behind them stay and the Board tab shows what they see, so a
better presentation costs only the presentation.

## Two bugs the saved config exposed

Both were found by reading `pluginConfigs/BeastMastr.json` and the log rather than by trying things,
and both had the same shape: something that fails without failing.

### The second callback value is a UInt

The recordings read `[0] Int=1 [1] UInt=0`, and only the first is an Int. Sending the row or slot as
an Int as well is **silently ignored** — no error, no log, the window simply never takes the
selection. That is what "the window did not take X" had been reporting all along, after the command
number was already right. `SetUInt` for the argument in both selectors.

### The ranks were fine — that diagnosis was wrong

Recorded because the reasoning was the problem, not the code. Fifteen of fifty beasts read rank 1
and the lowest twelve were numbers 22 through 30, which looked like a stale panel writing one
beast's rank onto its neighbours. It was not: sorting by rank and then by number *produces*
consecutive numbers within a rank. The listing was evidence of the sort, not of a bug.

What settled it: the beasts actually used — the three carries — read 10, 8 and 10, and after the
store was discarded the same fifty values came back. Reproducible re-reading is what a correct
reading looks like.

`RankWatcher`'s two guards stayed anyway. Reading only while the panel is visible and only believing
a reading that repeats are both cheap and both true things to want; they simply were not fixing what
they were written to fix. `KnownRanksVersion` also stayed, since a way to discard a bad store is
worth having whether or not this was one.

### The team size was a setting, and settings go stale

`BoardTier` is chosen by hand. Ask for twelve on a board with ten slots and the last two additions
are refused with nothing explaining why — which reads exactly like the automation being broken. The
roster window lists one row per slot, so the real size is there to be read: the plan is now clamped
to it, and the status says so when the setting and the board disagree.

## Phase 4: the game's own windows, not a window of our own

Asked for: only the next room shown, in detail, and buttons for the extras in the screens they
belong to.

The first of those changes the shape of the whole thing. If only one room is ever shown, the card
does not need to hang off a world marker at all — it does not need per-frame projection, it does not
fight the camera, and it can be a panel in the run's own HUD. **Both halves then become the same
mechanism**: attach KamiToolKit nodes to a window the game already has, which is what the bestiary
badges do and what is therefore already proven.

`KamiToolKit` has `NativeAddon` for a standalone window, and it is not needed here. Verified while
looking: `NativeAddon.InternalName` is a string, `Title` a `ReadOnlySeString`, `Size` a `Vector2`,
with `Open()`/`Close()` and a `ContentStartPosition`; nodes go on with the `AttachNode(NativeAddon,
NodePosition)` overload rather than a method on the addon. `TextButtonNode` takes `String`, `Size`,
`Position`, `IsVisible` and an `OnClick` action — and has no `Tooltip`.

### Buttons

`Native/ActionButtons.cs` puts "Fill for levelling" under the bestiary, and only while the roster is
open beside it — that pairing is team composition, and anywhere else the button would do nothing.
It sits below the window rather than inside it, because the bestiary's own area is tiles all the way
across and a button among them covers one.

**A button is a better home for this than a mode.** You press it when you mean it, pressing it again
is how you retry after a failure, and nothing happens while you are only looking. `RequestFill`
therefore also clears the give-up flag, which until now needed a plugin reload. The automatic mode
stays for anyone who wants it, and its description now says it does what the button does.

Node ids start at `0x42460000`, clear of the bestiary badges at `0x42450000`.

## Reading every rank without changing anything

The ranks do exist as data — the game knows all fifty — but the only place found that shows one is
the detail page, for whichever beast the cursor is on. `XBMModule` is the job's save file and would
presumably hold them, but its format is unknown and that is a reverse-engineering round of its own.

There is a cheaper route that needs no new knowledge. `[5, slot]` is what the window is told when
the cursor **enters** a tile, and it repaints the detail page and does nothing else.
`Automation/RankPuller.cs` sends it deliberately, one tile at a time, six frames apart, across both
pages, and reads the panel each time. Then it puts the page back where it started.

**Hovering is not selecting.** No beast joins or leaves a team, which is the whole reason this is
acceptable — clicking each beast would rearrange a team to answer a question about it. All three
commands it uses were recorded from real clicks, so none of it is a guess.

It is a button, not a background job: fifty tiles' worth of the detail page flickering past is
something to ask for, not something to spring on someone.

### Where the buttons sit, in three attempts

All three were the same mistake: comparing a position against something measured in different units.

The first offset from `GetScaledHeight`, which is screen pixels while a child node's position is
local. With the UI scaled up those disagree by exactly the scale factor, and the button landed that
far *below* the window.

The second measured from the last tile, which is the right idea and still wrong: **the tiles do not
hang off the window.** They sit in a container with its own offset, so a tile's `Y` is measured from
the container and the button's from the window. Adding one to the other put it a whole row too
*high*.

The third summed `Y` up the parent chain to convert between them, and landed too low again.

The fix is not better arithmetic, it is not needing any. The button is **attached to the same
container as the tiles**, `lastTile->ParentNode`, so its position and theirs are already in the same
units and the placement is a subtraction the game does not have to be asked about:

```csharp
Position = new Vector2(0f, lastTile->Y + lastTile->Height + 1f);
fill.AttachNode(lastTile->ParentNode, NodePosition.AsLastChild);
```

The numbers come from a capture rather than from the eye. In
`captures/XBMMonsterNotebook-nodes-20260909-145336.txt` the grid container sits at screen y 227, the
bottom tile row at 795 and "Beasts Captured" at 973, with the UI at scale 2 — so in the container's
own units the tiles end at 348 and the caption starts at 373. A 24-high button one unit under the
tiles fills that gap exactly.

Worth keeping in mind for the next node: **a captured screen position divided by the UI scale is a
local one**, and that is how any of these could have been checked without a round trip through the
client.

### The rank sweep is shelved

`RankPuller` works and stays, but its button is gone from the bestiary. Fifty detail pages flickering
past is not a good answer to "the ranks should just be there", and a button that does something
awkward is worse than the awkwardness being visible. The Beasts tab still offers it.

## Choosing carries where the beasts are

`Native/CarryContextMenu.cs` adds "Add as carry" / "Remove as carry" to a beast's right-click menu.
Carries are chosen while looking at the beasts, and the checkboxes in the Beasts tab ask you to find
the same beast twice — once in the game and once in a list beside it.

**Which beast was right-clicked is not in the menu.** `IMenuOpenedArgs` gives the addon and nothing
about the tile, and the bestiary's grid is not a list with an item id. The only handle is the tile
the cursor last entered, which the window announces as `[5, slot]` — so `EventRecorder` tracks that
whether or not anything is being recorded, and the icon in that slot names the beast.

Past three carries the entry is not offered at all rather than shown and refused: an entry that
cannot do anything reads as a bug, while a missing one reads as a limit.

## Phase 4: the next room, and only the next room

`Native/NextRoomPanel.cs` shows one room — the one you are about to enter — in a `NativeAddon`,
which is a real game window: the game's own frame, draggable, and it remembers where it was left.
That last part is the reason it is a window rather than something anchored to the HUD. Three
attempts went into placing a single button inside a window whose layout is fixed and captured; a
panel that the player positions once needs none of that.

The board shows twelve rooms at once and each is a click away from what it holds. Only one of them
is a decision about to be made. The arrows look further ahead, which is a different question from
what belongs in front of you, and they reset the moment a room is entered.

### The room list says two different things, depending on where it is opened

First version, wrong: boards were saved per territory. The whole board is only ever listed **at the
entrance** (territory 148, Central Shroud) — the run itself is territory 1339 — so inside the run the
cache looked up an empty entry and the panel had nothing to show. It was never on screen.

What the captures actually say, once sorted by territory:

| Where | Room list | Board marks a room current |
|---|---|---|
| Entrance, 148 | the whole board, 12 rooms | no |
| In a run, 1339 | **one room** | yes, on the same move as that room |

Five run captures, five times the single room sits on the marked tile's move — including two taken at
the very start of a run, where it is move 1, "Enemy #1". So in a run the list shows the room at your
position, and that room has **not been entered yet**. That is the room to brief, not the one after
it; the first version was off by one there too.

`Data/BoardCache.cs` now tells the two apart by shape: rooms spanning several moves are a board, kept
and saved (one board, the last one planned — that is the one being played); rooms all on one move are
a position, taken only while the board marks something current. A branching move offers two rooms on
the same move, which is still a position.

The marked-tile condition is what keeps the leftovers out. The window stays loaded after it closes
and keeps its contents: `captures/board-20260909-224042.txt` is one stale room read in Central Shroud
with both board windows reporting themselves visible — and nothing marked.

### Enemies are keyed by room label, not list index

Inside a run the list holds one room, so its index is always 0 — and keyed by index, every room read
in a run overwrote room 0 of the whole board, which is the boss. Labels ("Enemy #4", "Boss of the
Board") are unique on a board and read the same in both lists.

### The window can be opened by hand

`/beastmastr room` opens or closes it regardless of the run. The automatic rule is a guess about when
it is wanted, and "I cannot find it" was the first thing anyone said about it — so there is now a way
to make it appear, and the Settings tab says in words why it is or is not showing.

"In a run" is the run's HUD being up, *or* being in the territory where the board last marked a
position. The HUD has not been seen in a capture yet; the territory has.

### Which move the run is on, read off the board's rows

The board is a ladder drawn bottom to top, and the lines connecting two rooms are entries of the
same component with positions of their own — so its rows alternate, room row, link row, room row.
Every second row from the bottom is a room row: the first is where the run starts, and after that
there is exactly one row per move. `AtkComponentXBMContentStageEventMap` marks the current entry, so
the row holding it is the move.

**The tile index is not the room-list index**, which is the trap here: a twelve-room board handed out
indices 0 to 28, because the connectors are entries too. Pairing on that index points past the end of
the list and the Board tab has always said so.

The row mapping is checked rather than trusted. `StageMapReader.MoveOf` is given the room list's own
per-move counts and refuses to answer unless the rows match them one for one. On the twelve-room
board of `captures/board-20260909-185350.txt` the room rows hold 1, 1, 2, 2, 1, 1, 1, 2, 1, 1 tiles,
which is the start plus the list's counts branch for branch — moves 2, 3 and 7 fork and so do rows 2,
3 and 7. A move read off a mapping that does not hold would brief the wrong room with nothing to say
it had, so it gives up instead.

### What a room asks you to bring

`Rules/RoomBriefing.cs` is pure and covered by the harness. Two needs are derivable today:

- **Interrupt**, which the enemy panel states per action. It words it rather than flagging it, and
  the only value seen is "Ineffective" — so anything else counts as interruptible. That errs towards
  offering: a room briefed as needing an interrupt it does not need costs a team slot, one that hides
  a needed interrupt costs the run.
- **Cleanse**, which follows from a status an enemy applies that the team does not already nullify.
  The panel has its own hidden "Nullification" note for exactly that, so this is the game's answer
  rather than ours.

**Dispel is not claimed.** No window found so far marks it, and a need invented in the rules layer
would be indistinguishable from one read out of the game.

Every name — enemy, status, weakness — is passed through in whatever language the client is in.
Writing our own words for them would mean a table per client to maintain.

## Filling a team: empty it, then fill it

The first version diffed the team it found against the team it wanted and toggled only the
difference. That made the result depend on reading the starting team exactly right, and a misread
beast simply stayed. `TeamSelector` now works in two phases: take everyone out, **check that the
roster reads empty**, then add the plan in order — carries first, then the least advanced. The end
state depends on nothing but the plan.

Each step says what it is for — join or leave — rather than inferring it from the roster when it
runs. Inferring it turned a "remove" into an "add" for a beast that had already gone.

If an addition is refused while the team already holds something, the team is full: the board takes
fewer than the tier setting says. The plan is ordered, so what made it in is the right team for that
size, and it finishes with a message saying so rather than as a failure. The old clamp to "the
roster's slot count" is gone — it counted the *filled* rows, since the reader stops at the first blank
name, so a half-filled team clamped the plan to its own size.

### "Remove all", recorded and replayed

`captures/board-20260910-171738.txt` has it. It lives in the **team list's** right-click menu, not
the bestiary's, and it takes three windows:

| Step | Window | Values |
|---|---|---|
| right-click row 0 | `XBMPetParty` | `[0] Int=2 [1] Int=0` |
| pick the third entry | `ContextMenu` | `[0] Int=0 [1] Int=2 [2] UInt=0 [3] Undefined [4] Undefined` |
| confirm | `SelectYesno` | `[0] Int=0` |

The team list takes Ints for both values, unlike the bestiary's `[Int, UInt]`. The types are spelled
out per value in `TeamSelector.Fire` for exactly that reason.

The entry is picked by position, which is the part worth being careful about. The confirmation is the
guard: if the third entry were ever something other than "Remove all", it is unlikely to also ask "are
you sure". No confirmation within a second means stop, and the message lists what the menu offered.
What the menu offered and what the confirmation asked also go to the log every time.

Emptying needs only the team list, so it works without the bestiary. Adding needs the bestiary's
tiles, and the team list has its own button to open it — `captures/board-20260910-173503.txt`:
`XBMPetParty [0] Int=5`, sent **with the window closing**, after which the bestiary reports a child
window attached. So the team list closes and comes back beside the bestiary, and anything watching it
sees it gone for a moment. The fill therefore gives the team list two seconds to come back before
treating its absence as the screen being left; without that it would cancel itself every time it
opened the bestiary. If the bestiary does not appear, the fill says so in chat and carries on the
moment it is opened by hand.

The recorder now also notes whether a callback closes its window. That flag was not recorded before,
so the replay assumes the menu and the confirmation close on being answered, which is what they do on
screen.

### The team list keeps its old rows, and only its counter says who is in

The first live run of "Remove all" did empty the team, and then reported that it had not: "the team
list still shows Gigantoad, Ghost, Morbol…". The second press gave it away — the menu's first entry had
changed from "Remove from Team" to "View the Master's Bestiary", which is what an **empty** row offers.

The roster's blocks are not the team. The window writes a block per beast and never clears them; how
many of them are the team is a separate counter, the "0/14" it draws above the list, at value 1181.
`captures/board-20260910-171738.txt`, taken right after a manual "Remove all", has fourteen named
blocks and "0/14". The reader had been reading until the first blank name, so it counted old rows as
members.

It now reads only as many blocks as the counter says. That also fixes a quieter bug in filling: a beast
that happened to sit in an old row read as already in the team and was skipped rather than added.

Value 1162 is the capacity — 14, 12, 10 across the captures, the same number as after the slash. The
team size now comes from there, with the board tier setting only as the fallback.

### Open is not ready

The first fill that got past emptying gave up on its very first beast: "the bestiary is open but says
nothing about which page it is on". The log has the timing — "Remove all" confirmed at 17:52:24.839,
the failure at .885, three frames later. The bestiary was visible and still refilling its tiles for
the team that had just changed underneath it, so neither the page number nor any icon could be read.

It was always on page 1, as it always opens there, and that is no help: without the tiles' values
there is no telling which tile is which beast either. So the fill now waits until the first tile's
number comes back before touching anything, for up to two seconds, and if that runs out the message
says what the tiles held at the time.

### Team composition is the team list, with or without the bestiary

The button appeared only while the bestiary was open, because "composing a team" was defined as both
windows being up. That had it the wrong way round: the team list is the screen, and the bestiary is
opened from it. The team list does both of its jobs in one window, though. Its value 2 tells them
apart: `0` in both team-composition captures, `2` in the fight capture. That is the only header value
that split them, and it is used instead of the prompt because the prompt is a localised sentence.

The automatic mode still waits for both windows. Emptying a team the moment the screen opens, before
anyone asked, is not something to spring on a player; the button is the thing you press.

### The roster's button

Hung off the same parent as the roster's list component and placed above it, for the same
same-units reason as the bestiary's. If the list sits flush with the top it goes below instead — over
the first row it would take that row's clicks. The same capture has the roster's node tree and shows the spot is free:
at the window's scale of 1.2, the header's separator line ends 57 units down and the list starts at
90. That was wrong, see below: the button row of the window sits in that stretch.

## Nothing happens without a button press

The first working fill was followed straight away by the complaint that mattered more: the plugin
was blocking manual input. The config showed why. Both automatic modes were on, filling the team
and calling the last fight familiars, and the team one started over whenever the team did not match
its plan. Change a single beast by hand and it emptied and refilled the team underneath you. Once
the team did match, it would have printed "Team already matches" to chat on every frame.

The rule now, for everything in this plugin that changes game state: **it acts on a button press,
once, and at no other time.** The modes are removed, not switched off, together with their settings
and their config entries, so an old saved "on" cannot bring them back.

- The team list in team mode carries "Fill for levelling", and so does the bestiary while it is open.
- The team list in fight mode carries "Call last familiars". It calls the ones from the last fight,
  leaves any already called alone, and stops at the first pick that does not take. Nothing is
  permanently given up on any more; pressing again is how to retry.
- Remembering the last fight is still automatic, because it only reads. It pauses while the button
  is calling, so a failed pick cannot store half a selection as the last fight.
- The learned fight prompt is gone. It existed only so the automatic mode could recognise the fight
  window, and the button is only offered there to begin with: the team list mode number decides it.

### Automatic, but once: calling the last fight familiars

Taking calling-familiars off automatic was right about the cause and wrong about the cure. The
automatic version was a problem because it acted **every frame**: whenever nothing was called, it
called the last fight familiars again, so taking them out by hand put them straight back. The step
itself is one people want done for them.

So it is automatic again, as an event rather than a condition: it fires **once, as the team list
turns into the fight window**, ten frames after, and only if nothing is called yet. After that the
window is left alone until it closes. Taking every familiar out leaves them out; the button puts
them back if that is what you want.

It says nothing in chat when it works. Once per fight, a line saying that what always happens has
happened is noise; failures still go to chat. It can be switched off in Settings and is on by default.
The team fill stays on its button alone.

### The team list has more than two jobs

"Not team composition" was taken to mean "a fight", so the familiar call fired at shops (choosing
who to feed) and at campsites (choosing who rests) too. The window does at least four jobs, and only
two of their mode numbers are captured: 0 team composition, 2 fight. The automatic call and its
button now need exactly 2.

The others are not guessed. Every change of mode is logged with the header values, the counter and
the prompt, so opening a shop and a campsite once is enough to pin their numbers down from the log,
without anyone taking a capture.

Reading only up to the counter is now limited to team composition, the one mode where the stale
rows were seen. At a shop or campsite the counter could count something else entirely, and a "0/2"
there would have hidden every row.

### Pick lowest HP

Shops and campsites get a "Pick lowest HP" button. The row HP is offset 4, "2943/2943". It picks
the lowest share of HP left first, and between equal shares the one missing more. A share rather
than a raw number, because a big HP pool can have more left and still be closer to going down.
Full HP is never picked.

Zero HP is not "the most hurt", it is out: such a familiar cannot be rested or fed at all, and
picking it would spend one of the window's few picks on nothing. It is left out of the ordering
rather than sorted to the front of it, and the message says how many were left out — otherwise
"nothing to pick" would read as "everyone is fine" while half the team is down.

Those windows allow different numbers of picks, and the limit does not need to be known: it picks
one at a time until the window refuses the next. A refusal after at least one pick is the expected
end. A refusal of the very first pick says so plainly. The pick is confirmed through the same call
slot flag a fight uses, and whether these windows use that flag is not yet captured, so that message
is written to point at the likely cause if it does not hold.

The row click itself now lives once, in `TeamListCommands`, shared by the fight call and this.

### The team list's buttons live in its header

The first placement, above the list, sat on the game's own buttons. The claim that the capture
showed that spot free was wrong: it came from reading the node tree only one level deep. One level
further down, a row holding the team counter, "Master's Bestiary", "Recommended Team" and three icons
fills exactly that stretch, at 60 to 88 in the window's units.

The same capture, read all the way down, has one stretch nothing uses: the header, right of the title
and above its gold line. The title and an empty subtitle slot end 190 across, the gold line is 53
down, and two hidden header buttons start 54 in from the right edge. The buttons now go at 27 down,
ending 60 short of the right edge. The header belongs to the window frame, not its content, so this
holds in every mode of the window, and it is measured from the window's own width, not from the list.

The lesson for the next one is the one the bestiary taught, one level deeper: **read the whole tree
before calling a spot empty.**

## The Crucible mode, which the game forgets

Once every board has been cleared the board window offers a mode: Standard, First Degree, Second
Degree, Third Degree. The game starts every visit at Standard, so anyone playing a harder one steps
it up by hand each time.

The recording put the two stepper buttons in `XBMStageDetailList`, not in the board selection —
`XBMStageList` was not even open — and they are single Ints sent with the window closing: `[5]` and
`[4]`. The dump of that window then showed the rest: node 25 is a `DropDownList` whose closed face
(a checkbox with text node 3) shows the mode, and whose list holds the four names in order.

Three things worth keeping:

- **The mode is remembered as a position, not a name.** The names are localised; a saved name would
  stop matching the day the client language changed, and would then walk the mode somewhere wrong.
- **Which button raises the mode is measured, not assumed.** `DifficultySelector` presses one, reads
  the position again, and flips direction if that went the wrong way. The right-hand button looking
  like "up" is a guess; what one press does is evidence. There is also a hard cap of eight presses,
  because a loop that cannot terminate is worse than a mode left where it was.
- **Once per opening**, and only while the mode differs from the remembered one. What is set by hand
  afterwards stays, and becomes the new remembered mode — the same rule the familiar call follows.

### Why the first version did nothing at all

Two mistakes, both invisible from the outside, and the log said nothing because the interesting line
was written at Debug — of which **not one from this plugin reaches `dalamud.log`**. Anything worth
reading afterwards goes to Information.

**The window being open is not an opening.** `XBMStageDetailList` stays loaded and reports itself
open long after it was last used; a capture caught it "open" in Central Shroud, nowhere near the
Crucible. Waiting for it to open therefore waited for something that had already happened. The
opening is now the **mode block appearing**, which only exists while the window is really up.

**Learning has to wait its turn.** The window opens on Standard, and learning ran on every frame, so
Standard was remembered as the mode wanted within a frame of the window appearing — before the
setting step ran. It then found target and current equal and did nothing, having overwritten the very
thing it existed to restore. Nothing is learned now until the opening has had its one chance to set.

The general shape of both: **an automation that acts on an event has to be sure the event is the
event**, and anything that learns from the same state it corrects has to learn after it, not before.

### The list of modes is not the list of modes

With the log finally talking, the third mistake showed itself in one line: `Crucible mode remembered:
Standard, 1 of 1`. The drop-down only draws rows it has needed, so a freshly opened window carries
**one** name — the one it is set to. The four in the capture were there only because the modes had
just been stepped through.

That broke the range check. Opening on Standard with Third Degree remembered, the target read as "4
of 1", was rejected as out of range, and the opening was marked done — after which learning stored
Standard over the mode it was there to restore. Exactly the reported "the mode is not saved right".

So the count comes from the list's own `ListLength` and never from the names drawn so far. The
**position** still comes from the label: stepping through the modes, the name shown always landed
where it should among the drawn ones, which the log proves line by line, while the list's own
`SelectedItemIndex` has never been checked against anything — it is carried into the log to be
compared, and decides nothing.

## The world cards are gone

`UI/BoardOverlay.cs` is deleted, and with it the setting that turned it on. Switching it off by
default was not enough: the flag was already saved as on from before that change, so the cards were
still hanging under every room icon out in the world and in the way.

Nothing is lost by removing it. The card under an icon was a first attempt at "what does this room
hold", and the next-room window answers that better — one room, the one you are about to enter,
readable without hunting for an icon. What made the overlay possible stays: `MapMarkerReader` and the
world transform still work and are still shown in the Board tab, so a better presentation costs
nothing but the presentation.

A setting that no longer exists cannot be saved as on, which is the point of removing the property
rather than only the drawing. An old config carrying `ShowBoardOverlay` is simply ignored.

## Board automation, from the ground up

The goal: `/beastmastr run` plays a whole board on its own. It is built in the order the plan fixed —
record what is not known, place the board in the world, scan the ground, pick a route on the board
window, then walking, the rotation, the rooms and the runner that ties them together.

### Recording a run

`Data/RunRecorder.cs` writes a timeline to `captures/run-*.txt` while `/beastmastr record` is on:
condition flags, windows opening (with their values, and their node tree the first time) and the
values that change while they stay open, every callback, every action used, the combo, the raw gauge,
the familiar bytes, the board's current event, the target, casts, statuses, objects appearing and
going, chat the game writes, and the player's position ten times a second.

It exists because everything the automation still lacks is a *sequence* — what "Commence Battle"
sends, what closes the spoils, what the job presses while the gauge does what — and a capture is a
moment. One board played by hand with it on answers all of it.

`Data/ActionWatcher.cs` watches `ActionManager.UseAction` and the hotbar's `ExecuteSlot` and
`ExecuteSlotById`. A use that arrives inside a hotbar press is the player's; BossMod and this plugin
call `UseAction` directly and never pass through the hotbar. That is how a manual press will be told
from an automated one later, which `UseAction` alone could not say.

`EventRecorder` prints callback values as their own type now. It printed `.Int` for everything, which
turned a string into half a pointer.

### The board is in the game data, links included

Found offline with a Lumina dump, and it replaces every screen-position heuristic:

- **`XBMContentStageEventMap`**, a subrow sheet, one row per board, one subrow per drawn cell:
  `X, Y, Type, EventIndex, LinkedEventIndex`, all UInt8. Type 1 is a room. Every other type is a piece
  of a link *from* `EventIndex` *to* `LinkedEventIndex` — 6 straight, 4/9 and 5/10 the diagonals, 7/8
  sideways on the fifth board, where a link spans two cells. Type 0 is padding. Y counts down the
  window: the start (event 0) has the largest Y, the boss the smallest. X is the column, 6 in the middle.
- **`XBMContentStageEvent`**, subrow index = event index: `Move, EventType, ?, ?`. Event type 1 is the
  start, then the room list's kind ids plus two — 2 Enemy, 3 Elite, 4 Boss, 5 Shop, 6 Campsite,
  7 Treasure, 8 Random. Checked move for move and kind for kind against the room lists captured at the
  entrance.
- The board window's component carries the same five bytes live, as
  `AtkComponentXBMContentStageEventMap.EventMapEntries`, plus `CurrentEventIndex`, `GridSize` and
  `XBMContentStageEventMapRowId` — the board's row in the sheet. The "tile index" the component hands
  out per drawn node is an index into these entries. That is why a twelve-room board handed out indices
  up to 28: the links are entries too.
- The room list at the entrance counts back from the boss: list index = highest event − event.

All five boards come out as whole graphs — one start, one boss, nothing leading nowhere — and the
harness asserts it. `Rules/BoardGraph.cs` builds them, `Data/BoardSheets.cs` reads the sheets,
`Data/BoardEventMapReader.cs` reads the live component.

### Placing the board in the world

`Rules/BoardJoin.cs` pairs every room with its map icon. Rows pair by order from the start, columns by
position (map X 320 per column, taken from the icons themselves), and every pair has to agree on the
kind. A mirrored or shifted set of icons is refused rather than joined; the harness checks both. The
icon range 63850–63859 counts as rooms, known kind or not, so an unidentified icon cannot shift a row.

`Data/BoardModel.cs` keeps it together: the board's row (remembered in the config, since the window
has to be open to name it), the graph, the join, where the run stands — the window's marked event
while it is up, else the room trigger object 2015483, which sits on the current platform — and, while
the window is open, `Rules/PreviewProjection.cs`: grid to screen fitted from the drawn tiles, world
to grid linear across the columns and piecewise down the rows, because the platforms are not evenly
spaced (7.5 and 9 yalms alternate on the first board).

The start has no icon. Its position is measured when the board marks event 0, else estimated five
yalms short of the first room, which is where one capture had the player standing.

### Scanning the ground

`Ipc/NavmeshIpc.cs` wraps vnavmesh 1.2.3.14. Every signature was read off the installed dll with
reflection — the lambdas it registers keep their parameter and return types — because an IPC type
mismatch fails at runtime, not at build time. `Nav.Pathfind` returns `Task<List<Vector3>>`,
`Query.Mesh.PointOnFloor` is `(Vector3, bool allowUnlandable, float halfExtentXZ) → Vector3?`,
`Nav.Rebuild` returns a bool, `Path.Stop` is a plain action.

`Data/BoardTerrain.cs` asks, and never moves:

1. the floor under every room — a room off the mesh cannot be walked to, and usually means the mesh
   cached for this zone belongs to a different board, which is what "Rebuild the mesh" is for;
2. a half-yalm grid of the ground around the board, for the map;
3. every link as a walk: a straight line if the floor holds along it, a planned path if not, and in
   either case the closest it comes to any *other* room. The columns are five yalms apart and walking
   onto a platform starts its room, so a walk that comes within the trigger radius (a setting, 2 y by
   default) plus half a yalm of a room it is not heading for is retried around that room, and marked
   unsafe if it still does. The route never takes an unsafe link.

A few hundred queries a frame at most. The result is stored per board in the config folder as JSON.

### Picking the route

`Rules/RoutePlanner.cs` scores whole paths, not single rooms — a good room can lead into a lane whose
next rooms are worse. A room picked by hand binds its move while it is still reachable; everything
else follows the preference order (treasure, shop, campsite, enemy, random, elite by default), a
campsite jumps to the front while the most hurt familiar the team list last showed is under the set
share, and elite rooms can be avoided outright. Ties go to the lower event index, so a board plans the
same way twice. It is planned again every quarter second, from where the run stands.

`Data/RouteKeeper.cs` holds the picks, per board. `Native/RouteOverlay.cs` draws the route on the
board window — an arrow on every planned room, a star on every picked one — and puts a pin in the
corner of every room on a fork; clicking the pin picks the room. The marks hang off the window's root,
not the tiles: the tiles are components inside a component, and the team list's buttons, which work,
hang off a root too. The Run tab offers the same picks as buttons and on its map.

### The job, read offline

Beast Tamer is ClassJob 43. From the `Action` and `ActionTransient` sheets:

| Id | Action | Level | What |
|---|---|---|---|
| 44879 | Smash Axe | 1 | combo 1, GCD |
| 44883 | Axeblade Bite | 2 | combo 2 after Smash Axe |
| 44885 | Shieldsplitter | 12 | combo 3 after Axeblade Bite, +15 TP |
| 44884, 44887, 44888, 44889 | Avalanche, Mistral, Spinning, Gale Axe | 4, 8, 14, 16 | 100 TP minimum, spends all TP, potency 400 to 1000 with TP; shared 5 s timer (group 16); affinity Rampant, Durant, Eldritch, Volant |
| 44930 to 44933 | Brutal Rage, Hawkish Talons, Risen Fall, Calamity | 50 | the four axes at 250 TP (trait 758) |
| 47093 | Trick | 8 | 100 familiar TP; grants a Heart |
| 44890, 47092 | Tempered Release | 18 | 30 s; needs One with Nature (4601) |
| 44895 | Borrow | 22 | 30 s; grants a Kinship, turns Beast Mode into that kinship's action |
| 44886 | Beast Mode | 22 | GCD |
| 44891 | Parting Blow | 6 | 10 s; the familiar retreats after an AoE of 1000 |
| 44881, 44892, 44894 | First, Second, Third Battlehorn | 1, 10, 20 | 1 s cast; summons that slot's familiar |
| 44893 | Shield Charge | 24 | 60 s gap closer |
| 44905, 44904 | Rally, Rallying Cheer | 28, 40 | 120 s; spend Instinct stacks for TP and familiar TP |

The resource loop: the combo builds TP (cost type 111); Trick spends familiar TP (cost type 112) and
grants a Heart — Volant 4595, Rampant 4596, Durant 4597, Eldritch 4598. An axe whose affinity follows
the Heart completes an intentional combo: Rampant after Volant, Durant after Rampant, Eldritch after
Durant, Volant after Eldritch. Those grant Sunstrider 4599 or Moonstalker 4600, and a skill of the
other one completes the infinitive combo. Summoning a familiar grants One with Nature (trait 692) and,
from 30 and 34, resets Tempered Release and Borrow.

BossMod 7.5.6.5 knows the job (`BossMod.BST`), but its "xan BST" rotation presses the combo only; its
gauge read is commented out. The gauge layout it declares — counted from the gauge pointer, past eight
bytes: player TP, familiar TP, the last familiar action's TP, the summoned beast — is what
`Data/GaugeReader.cs` reads raw, since neither ClientStructs nor Dalamud has a struct for it. What each
byte does in a fight is for the first run recording to show.

### Walking one room

`Automation/Run/BoardWalker.cs` walks to one room and never further — a path over several rows is one
more chance to cross a platform on the way. It follows the link the ground scan checked, starting from
wherever the player stands; if the straight way from there comes too close to another room, it goes
back over the current room first. It stops as the room starts (the board marking it, the trigger object
moving onto it, the team list switching to a fight or a campsite, a shop or treasure window, combat)
and stops hard if the player comes within the trigger radius of a room it was not heading for.
`/beastmastr step`, the Run tab and the next-room window's Walk button all walk to the route's next room.

### Taking over

`Automation/Run/ManualInputGuard.cs` reads manual input from the game's own input ids
(`UIInputData.IsInputIdDown`) — movement 321–327, autorun, jump, the targeting ids 362–393 — plus the
left stick, a left click on a world object, and a hotbar press from `ActionWatcher`. Input ids follow
the player's own bindings, and vnavmesh and BossMod move the character underneath that layer, so
neither is mistaken for the player. While a game text field or a Dalamud window has the keyboard,
keys do not count (a setting).

Whatever is running lets go the moment input is seen: the walk stops vnavmesh, the fight stops
pressing and moving. It carries on after the set time without input — three seconds by default — or
stops for good, if that is the setting. Which inputs count is a setting too.

### Fighting

`Rules/BstRotation.cs` is the rotation, pure and covered by the harness. Given a snapshot — level,
combo, both TP values, statuses, what the game says is usable, target distance and cast, whether a
familiar is out — it names one weaponskill and one ability:

1. no familiar in the fight: the first Battlehorn that is ready;
2. One with Nature: Tempered Release; a familiar out: Borrow;
3. an axe that completes a combo with the Heart or star that is up; without one, the highest axe
   once TP reaches the set threshold (200 by default) — the axes grow stronger with TP and spend all
   of it, so spending early wastes potency;
4. Trick when familiar TP allows and no Heart is up to be overwritten, and not under Wavering Heart;
5. Rally and Rallying Cheer when their gauge is low;
6. Beast Mode under a Kinship — except Soul Kinship, whose Beast Mode interrupts and is kept for a cast;
7. Parting Blow, only if switched on, once Tempered Release and Borrow are spent (a new familiar
   resets them from 30 and 34); Shield Charge to close a gap;
8. the combo, in melee reach.

Before 2, the duty actions decide who takes the hits: Snarl hands them to the familiar, Challenge
takes them back. See "The second automated test" below.

`Data/BeastmasterJob.cs` holds the assumed levels and combo links against the `Action` sheet as the
plugin loads and reports any difference in the Run tab.

`Automation/Combat/CombatDriver.cs` plays it: the current target if it is a living enemy, else the
nearest one — but a new target only while a fight is under way, or once the run has started one.
It asks `GetActionStatus` for the adjusted id before every `UseAction`, waits out the animation lock,
and walks into reach with vnavmesh when BossMod is not moving. `/beastmastr combat` switches it on
by hand.

`Automation/Combat/BossModBridge.cs` hands the moving — and, if set, the combo — to BossMod for the
length of a fight, by preset: "BeastMastr Dodge" is `MiscAI.NormalMovement` (Destination Pathfind,
Range MaxRange); "BeastMastr Full" adds `MiscAI.AutoTarget` and `xan.BST`. They are created over IPC
when missing. Whatever BossMod had active is noted and put back when the fight ends, when fighting
stops, and on unload. vnavmesh is stopped first, so only one thing moves the character, and an
obstacle map is generated around the fight, since BossMod has none for the Crucible. BossMod's IPC
signatures were read off the installed 7.5.6.5 with reflection; every call returns a value.

### The run

`Automation/Run/BoardRunner.cs` plays a board: `/beastmastr run [boards]` or the Run tab, and nothing
else starts it. Each step it plans from the room it last finished, walks one room, and plays what the
room opens:

| The room opens | The run |
|---|---|
| team list, fight mode | waits for `FightSelector` to call the last fight's familiars, then commences |
| combat, or enemies about | fights until combat has been over for three seconds and nothing hostile is near |
| the spoils | takes them, then moves on |
| team list, campsite mode | picks the most hurt with `HealthSelector`, then confirms |
| the shop | leaves it |
| a treasure coffer | takes an item |
| the result, after the boss | closes it; the board is done |

`Automation/Run/RoomActions.cs` holds what those buttons send, each copied out of a run recording
(below), and `ConfirmedStep` carries one through: send it, answer the `SelectYesno` that follows, see
the window close. What cannot be carried through is handed to the player, who is told in chat and in
the Run tab what to press; the run carries on once the game shows it was done. A room that opens
nothing the run knows is handed over too, and the Continue button (`/beastmastr continue`) tells the
run it is finished. Nothing is guessed. The shop is left without buying and the first treasure offer
is taken, unless the Run tab says otherwise.

`Automation/Run/RunSafety.cs` refuses to start off Beastmaster, outside the run's zone, without
vnavmesh or without a known board; stops the run when the player goes down, the job changes or the
zone is left before the boss; waits through loading and cutscenes; and names the loaded plugins that
answer dialogs, press actions or move the character on their own (WrathCombo, YesAlready, TextAdvance,
AutoDuty, Questionable, PandorasBox) as a warning.

Pause (`/beastmastr pause`) stops walking and fighting where they are and picks the step up again
after. Manual input pauses walking and fighting by itself, as above. `/beastmastr stop` ends
everything, and BossMod gets its own presets back.

More than one board needs the way back in from the entrance — choosing the board, "Challenge This
Board", the team — which is not built yet. Until it is, the run says so after each board.

### What the first run recording showed — 2026-09-16

`captures/run-20260916-170423.txt`: board 1, level 30, played by hand from the start platform to the
boss in six minutes. It answered everything the run had been handing over.

**A room starts as it is stepped on.** The trigger fired 0.4 to 1.8 yalms from the room's centre in
all nine rooms. First sign: the status "In Event" (1268) on the player and the condition flag
SufferingStatusAffliction2, about two seconds before any window. The trigger object 2015483 moves
onto the room at the same moment. The walker now stops on "In Event".

**The board window opens only for fights**, together with the team list in fight mode, and marks the
room *being entered*: events 1, 2, 6, 8 and 12. Campsites, treasure and shops open their own windows
and leave the board window's mark where it was — so `BoardModel.CurrentEvent` now takes whichever of
the mark and the trigger object moved last.

**Fights happen elsewhere.** "Commence Battle" closes both windows and loads an arena in the same zone
(X 120 or 520, far from the board at X -700); after the spoils another load puts the player back on
the room just finished. The run waits to be back on the board before planning the next walk — there
is a second or so between the spoils closing and the load.

**The boss ends the run.** After it, a cutscene and a load back to the entrance (territory 148). No
result window appeared in the recording.

**What the buttons send**, every one recorded, with the window closing each time:

| Step | Window | Values | Then |
|---|---|---|---|
| Commence Battle | `XBMStageDetailList` | `[Int 8]` | load into the arena |
| Take the spoils | `XBMContentsBooty` | `[Int 1]` | `SelectYesno` "You will receive:" → `[Int 0]` |
| Rest at a campsite | `XBMPetParty` (mode 4) | `[Int 3]` | `SelectYesno` → `[Int 0]` |
| Leave a shop | `XBMContentsItemShop` | `[Int 0]` | `SelectYesno` "Conclude purchasing and leave the shop?" → `[Int 0]` |
| Take treasure offer n | `XBMContentsTreasure` | `[Int 2, Int n]`, n from 0 | `SelectYesno` "Choose the angel robe?" → `[Int 0]` |
| Buy or feed shop item n | `XBMContentsItemShop` | `[Int 2, Int n]` | team list mode 3 to pick who is fed, then a `SelectYesno` |

`[Int 1]` answers No. The shop sends itself `[8]` after every change.

**The team list's row click differs by job.** In a fight it is `[Int 1, UInt row]`; at a campsite and
for a shop's feeding it is `[Int 1, Int row]`, sent as closing. `TeamListCommands` sends whichever the
window's mode calls for. At a campsite a picked row shows at block offset +75 (a bool), where a fight
shows its call slot at +74. Modes: 2 fight, 3 shop feeding ("Feed the grape simular to whom?"),
4 campsite. Confirming a campsite with nobody picked asks "Rest alone while your familiars keep watch
and recover 90% of HP?"; with a familiar picked, "Rest and recover 45% of HP for you and your: …".

**The treasure coffer** offers four items in blocks of five values from 3: a bool (offered) at +0 and
the `XBMItem` row at +3. Offers 0..3 were green beret, windblown axe, demonic armour and angel robe;
choosing 3 asked about the angel robe and choosing 1 gave the windblown axe.

**The gauge**, from 178 changes: byte 0 is the player's TP (Shieldsplitter +15, Rally +40, an axe
spends all of it), byte 1 the familiar's TP (it rises with the familiar's attacks and Parting Blow),
byte 2 what the familiar's last action cost (Trick at 108 familiar TP left 108 here and 0 in byte 1 —
Trick spends everything, not 100), byte 3 the Battlehorn slot summoned (1–3, 0 while none). Bytes 4–8
move with the instinctual combos and are not identified.

**The job as it was played:** a familiar is summoned before the pull; Borrow, Beast Mode (Beastskin
under Beast Kinship) and Tempered Release follow; Rally when TP is low; Parting Blow once Tempered
Release is spent, and the next Battlehorn straight after it — while the old familiar is still on the
field — which grants One with Nature again and resets Tempered Release from 30. Trick at 100 familiar
TP or more, then the axe that answers its Heart (Rampant Heart, Mistral Axe → Moonstalker). Familiars
are battle NPCs of the pet kind. The rotation now does all of this, Parting Blow on by default (config
version 2 turns it on for older configs).

**The ground.** A grid of the board by height: the platforms are at Y 0, about five yalms long and
separated by bridges about one and a half yalms wide; the ground 2.5 yalms below is everywhere else.
The straight line between two rooms of different columns does not stay on the platforms, so those
links are planned by vnavmesh — all at platform height, and five yalms clear of any other room. The
Run tab's map now draws platforms and ground apart. vnavmesh's own bitmap
(`captures/navmesh-20260916-170845.bmp`) only draws what is reachable from where it was taken — the
middle column's three rooms and their bridges there — and agrees on the widths and the nine-yalm rows.

The recorder now leaves unset values out of its dumps; they were 93% of this file.

### The first automated test — 2026-09-16

Played with the Debug build of 17:03, not the Release build with the recorded buttons: Dalamud has both
folders registered, and the Debug one was loaded. Both are built from now on.

**The board window's "current event" follows the selection.** Opening the Board Layout at the start
logged "marks event 1", then "event 2" as tiles were clicked — `CurrentEventIndex` is the selected
room, not where the run stands. Walking "from event 2" while standing on the start sent the character
straight across a gap into a wall. Three things follow:

- The window's mark is now the tile flagged `IsCurrentEvent`, not the selected event.
- `BoardModel.PositionEvent` — the room (or the start) the player stands within three yalms of — is
  asked first. After a fight the player is put back on the room just finished, so standing on a room
  means having entered it.
- A walk only starts from the room the player is standing on, and always goes over that room's centre,
  where the checked link begins. The run takes where the player stands as the room last finished,
  whatever it thought — someone may have walked on by hand.

**A fight was declared over before it began**: the arena had loaded and nothing was in combat yet. A
fight now ends only after combat has been seen and has stopped, when the spoils open, or when the run
is back on the board after the arena. Hostiles seen from the board no longer count as the fight
beginning. The room that opens is taken as the one stood on, even when it is not the one walked to.

**The opener**, as the player plays it: a Battlehorn, Borrow from that familiar, a second Battlehorn —
all before the pull, and nothing is walked to or attacked until it is done. The second summon grants
One with Nature, so the first familiar's Tempered Release and the borrowed Beast Mode are ready as the
fight starts. The familiar only appears about half a second after the cast; for two and a half seconds
after a Battlehorn it counts as there, or the opener would summon twice instead of borrowing.
`GetActionStatus` is asked without the casting check, so what follows a cast is already decided during
it; the use itself still waits.

**Parting Blow** now needs a familiar to follow: another Battlehorn usable within ten seconds (a horn's
recast only starts once its familiar has retreated; the one out, per gauge byte 3, is left out) — or
the target at 10% HP or less, where the blow finishes it. Both are settings.

**Treasure** takes a random piece of gear by default (`XBMItem` column 0 is the kind, 1 Beast Gear);
the spoils are always taken whole.

**On the board window**, "▶" and "☆" are not in the game's font and showed as bars and empty buttons.
The arrow is now the game's own glyph (`SeIconChar.ArrowRight`), the pin a "+", the star stays. Marks
are placed by the grid's cell size on screen rather than the tile node's own size.

**On the board itself**, `UI/WorldRouteOverlay.cs` draws the next step: a green ring on the room the
route takes next (yellow while walking), red rings on the other rooms of that move, and the checked
way there on the ground. Nothing is drawn on the game's map; the map in the Run tab is the plugin's.

### The second automated test — 2026-09-16

Two whole boards ran on `/beastmastr run`: 17:56–18:02 with the recorder on
(`captures/run-20260916-175650.txt`), and 18:05–18:16 without. Every room was walked to and
handled — spoils, treasure, shop, boss — but only the first two fights of a board were fought.

**Later fights never started, because the enemy was out of reach of the target search.** Every
arena puts you at z −404. The first fight's Piscodemon stood 22.6 yalms away, and the boss 21. The
Banemite and the Ogre stood at z −430, 26 yalms away, and the driver only looked 25 yalms around.
It found no target, so it pressed nothing, not even the opener; you started those fights by hand.
Before the pull, the search now reaches 45 yalms: the arena holds this fight alone. The run's own
hostile search, used to see that the fight has begun and that nothing is left, reaches 45 as well.

**Continue while waiting for the fight ended it.** Eight seconds into the arena, the run went to
"the fight is over", and the only thing that ends a fight that has not begun is Continue. Continue
before any combat now means the fight did not start: the driver is started again, and the run
stays in the fight.

**BossMod broke the Battlehorns to dodge.** The driver's Third Battlehorn went off twice while Bedrock
Uplift was cast. Both times you were moving within 70 and 380 ms, and the cast broke. A Battlehorn has
a one second cast in the sheet, 0.54 s under Haste. BossMod does not know a cast it did not start,
so it slides out of it at once. Its movement module has a `Cast` track with the options Leeway,
Explicit, Greedy, FinishMove, DropMove, FinishInstants and DropInstants. The presets now set it to
Greedy, which never breaks a cast to move. The only casts are these one-second horns. The presets
carry a version: older ones already in BossMod are written again before the next fight, which
replaces any edits made to them there. The driver also starts a cast only once you have stood
still for 300 ms, and it stops walking into reach while a horn is next.

**BossMod has no module for any Crucible enemy.** None of the recording's enemy ids (19338 Piscodemon,
19339 Banemite, 19341 Ogre, 19344 Pas de Seul, 19345/19346 its adds) is an `OID` in
`BossMod.Modules.dll`. BossMod dodges them only by the shape the `Action` sheet gives each cast.

Most area hits come from hidden helpers: BattleNpc sub kind 11, base 9020, named after the enemy,
never targetable. The visible enemy casts a same-named action without a shape a few rows away:
Void Flare Star is cast by the enemy as 46893 (single, radius 0) and by a helper as 46895 (circle,
radius 100). Before the change, the recorder only kept the helpers in `obj~` lines.

What the recording's casts are, by the sheet:

| Enemy | Cast | Shape | Can it be dodged? |
|---|---|---|---|
| Piscodemon | Void Blizzard III 46889 (helper) | circle 5 | yes |
| Piscodemon | Void Flare Star 46893 → 46895 (helper) | circle 100 | no — 439 damage at 17:57:57 |
| Piscodemon | Arcane Blast 46899 | circle 100, 8 s | no |
| Banemite | Bedrock Uplift 46901 → 46902–46905 (helpers) | circle 6, then rings 12/18/24 | yes, in turn |
| Banemite | Deadly Thrust 46906 | on you | no — you pressed Snarl for it |
| Banemite | Venom Web 46908 (helper) | placed circle 9 | yes |
| Ogre | Scorching Smite 46913 → 46912 | cone 40 | yes |
| Ogre | Allfire 46915 | circle 40 | no |
| Ogre | Magma 46916/46917 (helpers) | placed circles 3/5 | yes |
| Pas de Seul | Blood Rain 46923 → 46924 | ring 40 | yes |
| Pas de Seul | Void Aero II 46932 / 46933 | line 60×8 / cone 60 | yes |
| Pas de Seul | Cold Caress 46935 | on you | no — you pressed Snarl for it |

`Rules/IncomingHits.cs` calls a hit unavoidable when it is aimed at you alone, or when it is a circle
of 30 yalms or more around its caster. `Data/EnemyCasts.cs` scans every battle NPC that is not a
familiar, helpers included. A cast without a shape takes the widest same-named neighbour within six
rows.

**The duty actions** are `ContentExAction` row 26: Duty Action I is **Challenge** (46750), Duty Action II
is **Snarl** (46751). In the recording, `GeneralAction 27` went to Snarl.
- Challenge: you go to the top of the enmity list, and the familiar's cover ends.
- Snarl: the familiar goes to the top, and "takes all damage intended for the beastmaster" for 45 s.
  You get Covered (status 2413).
- Both have a 15 s recast in cooldown group 81, so they share one timer. Both reach 25 yalms and use
  the enemy as target. `UseAction` takes them as plain actions, which is what the game does too.

You pressed Snarl exactly twice, for Deadly Thrust and for Cold Caress. The rotation now does the
same:
- Snarl for a hit that cannot be dodged, and when your HP is at or below a setting (50%). Either
  needs a familiar that is not itself low.
- Challenge when the familiar is at or below its setting (35%) while it covers you or is being hit,
  unless your own HP is low. While an undodgeable hit is on its way, the cover stays.
- The setting "Otherwise, the hits go to" picks Auto (the rules above), the familiar (Snarl whenever
  it can), or you (Challenge whenever the target turns to a familiar).
- Neither is pressed before the pull, since both draw the target. The HP matters: status 1097
  "Auto-heal Penalty" stops your regeneration for the whole run, and familiars carry their HP from
  room to room. Every Challenge and Snarl is logged with its reason.

**The recorder** now writes two more kinds of line:
- `ecast`: every enemy cast as it starts, helpers included, with caster, target (you, itself, or an
  id), cast time, and the sheet's shape. A borrowed shape is marked, and each line says whether the
  cast counts as unavoidable.
- `hp`: yours and your familiars' HP whenever it changes. `pos` only carries HP when you move.

**Still open:** the ring sequence of Bedrock Uplift and the placed circles are dodged by BossMod's
generic shapes. Whether that holds needs the `ecast` and `hp` lines of the next recording.
Auto-attack (`GeneralAction 1`) is asked for once a frame for about 13 frames at the pull, and 421 times
after the boss, always refused. It is not from a hotbar and not from BeastMastr; BossMod is the likely
source.

### The third automated test — 2026-09-16

Board 1 again, 19:05–19:14, recorded in `captures/run-20260916-190511.txt`. Every fight started on its
own now. The user's notes and the recording showed the following.

**The first spoils were never confirmed.** The `SelectYesno` opened in the same frame as the spoils
callback. `AddonReader.IsOpen` saw it visible before it had loaded, so `RoomActions.Send` refused it.
The step moved on anyway and waited for a window that stayed open. The step now moves on only once the
answer has really been sent. A yes that has not closed the question is sent again after a second. A
window that is visible but not yet loaded is waited out for two seconds before the step gives up.

**A dead familiar stopped the call.** Opo-opo died in the Banemite fight. At the next fight the call
"as last time" toggled it anyway, and the game answered "Incapacitated familiars cannot be assigned to
battlehorns." `Commence Battle` then brought up "At least one battlehorn has not been assigned. Commence
battle anyway?", which nothing answered.
- `PetPartyReader.Slot.IsDown` is now true for a familiar with 0 HP.
- The fight selector leaves such a familiar out and fills its place with the healthiest familiar not
  already wanted.
- `Commence Battle` now answers yes to that question when it comes, since by then every familiar that
  can fight has been called.

**Beast Gear already held is refused.** The coffer offered a Ninja Eyepatch that was already held. Taking
it brought up a `SelectOk` instead of the confirmation, and the run waited. The treasure window lists
the gear held: blocks of five from value 75, with a Bool while the block is used, the `Item` row at +2,
the `XBMItem` row at +3 and the name at +4. All three recordings with a coffer showed eight such
blocks.
- Offers of held gear are no longer picked.
- If the game refuses an offer anyway, the `SelectOk` is closed with `[0]`, as recorded, and another
  offer is chosen.
- Shops are always left without buying, so they cannot run into this.

**BossMod never walks in for a Beastmaster.** In `MiscAI.NormalMovement` the range "MaxRange" is melee
range only for the roles Tank and Melee; anything else gets 25 yalms. BossMod does not give the
Beastmaster a melee role. So after every dodge, and when the Piscodemon jumped back to the middle, the
character stood still.
- While no enemy casts anything a position avoids (`EnemyCasts.Dodging`) for 1.5 s, BossMod's
  movement is now held. This uses the transient strategy `Presets.AddTransientStrategy(preset,
  NormalMovement, "Destination", "None")`, which leaves the preset itself unchanged.
- During the hold, vnavmesh walks into reach.
- A cast with a shape clears the hold, and BossMod dodges again.
- The hold is also cleared when you take over and when the fight ends.

**BossMod ran the character out of the arena.** In the Banemite fight the player ran along a circle
about 20.5 yalms from (120, −420). There Bleeding (3077, then 3078) set in and stacked: 70–80% HP and
Opo-opo were lost.
- BossMod read Bedrock Uplift correctly. Its `GuessDonutInner` gets the rings 6–12, 12–18 and 18–24
  from the omens `gl_sircle_1005`, `3020` and `4836`.
- What BossMod lacks is the arena's edge. With no module, its pathfinding bounds are the obstacle map's
  square (`AIHintsBuilder.CalculateAutoHints` builds an `ArenaBoundsRect` from the map's view, and
  `ObstacleMapManager.GenerateMap` sizes it as centre ± radius, capped at 60).
- The map is now generated around the arena's middle (`Rules/CrucibleArena.cs`) with a half-width of
  13.5, a setting. The square's corners, at 19.1, stay inside the safe circle.

The known middles, by where the fights put the player (12–16 yalms from the middle):

| Middle | Fights | Evidence |
|---|---|---|
| (120, −420) | Banemite, Ogre, Bone Bishop | helpers placed there; Bleeding at 20.5 |
| (120, 0) | Piscodemon | helpers cast from there; the Piscodemon jumps back to it |
| (520, −420) | the boss | adds placed around it |
| (520, 0) | seen once in the first recording | taken by symmetry, not confirmed |

The map only applies once the player is inside the square. The spawn, 16 yalms out, is not, but the
pull walks in.

**Void Blizzard III** is the Piscodemon's line of exaflares. Helpers move to (120, 0), then cast
circles of radius 5 in rows at z −17.5, x 102.5 to 137.5 in steps of 7, one after the other. BossMod
sees only the circles being cast, not the next steps. The arena limit and the walking in should make
this better. Whether the rows still hit needs the next recording.

**The opener** is now Battlehorn II (or III, or I below level 10), then Borrow from that familiar, then
Battlehorn I, which comes in with One with Nature and starts the fight with its Release. Summons after
the pull still take Battlehorn I first.

The duty actions worked as intended:
- Snarl for Arcane Blast, Deadly Thrust and Cold Caress.
- Snarl at low HP.
- Challenge twice, once the covering familiar dropped below 35%.

A familiar that comes in hurt, like Opo-opo at 308/667, still loses a lot to a single Snarl.

### The fourth automated test — 2026-09-16: dying to Bleeding, and dodging of its own

Board 1, 21:17–21:21 (`captures/run-20260916-211658.txt`). The run ended in the Banemite fight (event 6)
when the player died.

**What killed the player.** Bedrock Uplift was cast at 21:20:11. BossMod ran the player out: 13.4
yalms from the middle at 12.6 s, 21.8 at 14.7 s, then along x = 138.5, 18.5 yalms out. Bleeding
(3077/3078) set in at 14.5 s, and it ended only with death. It took about 95 HP every three seconds,
from 1098 HP to 0 in 33 seconds. Snarl covered for Deadly Thrust, but the Diremite came in at 290/667
and died at 21:20:47, the same moment as the player.

**The arena square did not hold BossMod.** `AIHintsBuilder.CalculateAutoHints` only uses an obstacle
map while the player stands inside its box (`ObstacleMapManager.Find`). One step out, and BossMod's
bounds are its default 30-yalm square around the player again. On top of that, the second fight
logged `ObstacleMap.Generate failed`: `GenerateMap` rethrows the fault of the previous generation, so
the first fight's map had failed too.

**BossMod runs the wrong way.** In the first recording, where the fight was played by hand, the player
stayed within 5–13 yalms of the Banemite through two Bedrock Uplifts and lost no HP. BossMod dodges
all four hits at once. Every spot within 24 yalms is in one of them, so it runs out of the arena.

**So BeastMastr dodges now, and BossMod is off by default** (config version 4 sets it off once; it can
be turned back on in the Run tab).
- `Rules/CastShapes.cs` reads a cast's shape the way BossMod does. From its IL: `CastType` 2 and 5 are
  circles (5 adds the hitbox), 3 and 13 are cones (angle from the omen's "fanNNN"), 4 and 12 are lines,
  10 is a donut (hole from the omen's "sircle_OOII"), 11 is a cross.
- `Data/EnemyCasts.Zones` takes each cast's own shape. A placed hit is centred on
  `BattleChara.CastInfo.TargetLocation`, and the facing comes from `CastInfo.Rotation`. It leaves out
  single-target hits, hits placed on you (they follow you), and circles over the whole arena.
- `Rules/Dodger.cs` dodges only the hits that land within 1.5 s of the first. It searches a 1-yalm
  grid inside the safe circle (18 yalms, a setting) for a spot outside those hits with a yalm to spare.
  Each spot is scored by the distance to walk plus half the distance past melee reach.
  - Against Bedrock Uplift this steps 7 yalms out of the circle, then back to 4 yalms once the circle
    has hit, and stays there through the rings. This is the hand-played line, and the harness checks it.
- Outside the safe circle with nothing cast, it walks back in.
- `CombatDriver` plans every 150 ms and walks straight to the spot with `Path.MoveTo`. It starts no
  Battlehorn while it has somewhere to go, and walks in to the target only 1.5 s after the last dodge.
  Each dodge is logged: "Dodging Bedrock Uplift: to (x, z)".

**Still open:** the Void Blizzard III exaflare rows are dodged cast by cast. Their next steps are not
known ahead of time.

### The First Master's Board — 2026-09-16

`/beastmastr run` refused the First Master's Board ("Start a board first"): the run only knew zone
1339. Every board has a zone of its own, all marked `TerritoryIntendedUse` 62 in the TerritoryType
sheet:

| Zone | Board | Map |
|---|---|---|
| 1339 | First Board of the Unbroken | 1204 |
| 1340 | Second Board of the Unbroken | 1209 |
| 1341 | Third Board of the Unbroken | 1214 |
| 1342 | First Master's Board | 1219 |
| 1343 | Second Master's Board | 1224 |

`BoardModel.IsRunTerritory` now asks the sheet. The master board recorded by hand
(`captures/run-20260916-215152.txt`) has the same layout: start at (−700, 0, 0), columns at −705,
−700 and −695, rows 7.5 yalms apart from z −9, and the map's y offset is −36 instead of −38. Its
arenas use the same middles: the Strix, Corpse Flower and Treant at (120, −420), the Gargoyle at
(120, 0).

Its fights, by the `ecast` lines:
- **Strix:** Plummet, circles of 10 in waves two seconds apart.
- **Strix:** On the Properties of Quakes and of Floods, circles of 60 that cannot be dodged. Snarl
  did not spare the player the 713 damage of Quakes.
- **Corpse Flower:** Floral Trap, a circle of 80; Rotten Stench, a 45×12 line.
- **Gargoyle:**
  - Rippling Evisceration, a circle of 13, then a ring out to 30.
  - Sweeping Evisceration, a cone.
  - Malady, circles of 6 on a 7-yalm grid.
  - Fivefold Fallout, circles of 60.
- **Treant:**
  - Rustling Breeze, cones.
  - Arboreal Storm, a circle of 12, then rings out to 36 every two seconds; the dodger's line for
    this is the same as for Bedrock Uplift.

### The first runs on the First Master's Board — 2026-09-16

Two runs, `captures/run-20260916-220836.txt` and `run-20260916-221153.txt`. Walking, scanning (15 rooms,
17 links) and the room windows all worked on the new board.

**The Morbol killed the first run, with BeastMastr dodging.** The player stood 0.6 yalms from the
Morbol's middle, behind it. The breath (48672/48673, a cone of 50) counted that as outside, so the
dodge answered "already clear". Then the helper's short breath (48675) came every two seconds for a
fifth of a second, 650 damage each, under Poison, Toxicosis, Slow, Blind and Paralysis. It was always
too short to react to.
- A cone now hits everything within the caster's hitbox of its origin, at least 2 yalms (`Zone.Apex`).
- A cast that `EnemyCasts` sees start again within six seconds is predicted between its casts: the
  last shape, due an interval after the last start.
- The recorder now writes each caster's facing and cast facing, so that a turning breath shows next
  time.

**Snarl does not cover hits on the whole arena.** Quakes did 713 damage and the breath 650 while
Covered. Snarl is now pressed only for a hit aimed at the player, and at low HP.

**The Gargoyle killed the second run, with BossMod dodging** (switched back on for that run). It ran to
z 20.9, 21 yalms from (120, 0), into Bleeding, and the obstacle map failed again. Afterwards,
BeastMastr's own dodging handled Rippling Evisceration correctly: out to 14 yalms from the 13-yalm
circle, then in to 11 inside the ring from 13 to 30. Malady puts circles of 6 on a 7-yalm grid, so
the dodge margin is now 0.5 instead of 1, which leaves the free cells usable.

**Both deaths were revived** by the Ring of Sacrifice three seconds later. The run gave up anyway.
Going down now waits up to 10 seconds for a revive.

**After the campsite** the run walked on while "In Event" (1268) was still on from the rest. The walker
took that as the next room beginning and stood waiting 15 seconds for a window. Settling now waits for
the status to go, for up to 15 seconds. A walk that starts with the status already on ignores it until
it has gone once.

### The Gargoyle's Sweeping Evisceration — 2026-09-16

`captures/run-20260916-222548.txt`. Board, campsite and revive all worked. The Gargoyle killed the run.

**Every cone and line pointed north.** `BattleChara.CastInfo.Rotation` read 0 for every cast. Zones now
take the caster's own facing, which the recorder logs as "facing" with every `ecast`. Desolation (48727,
a line 60 long and 7 wide) had been placed wrong because of this.

**Rippling Evisceration's ring had no hole.** 48721 is a ring out to 30 with no omen, so its hole could
not be read and it was treated as a full circle. It follows the 13-yalm circle 48720, cast from the same
spot. A ring without a hole now takes the largest circle of the same name cast from its origin.

**Sweeping Evisceration** (`Rules/ScriptedMechanics.cs`): 48717 is cast for 7.9 s and has no shape of its
own. Its hits (48718, a 60-yalm cone with no omen) are not cast. The player describes it: stretch the
tether while it casts, get behind the Gargoyle after its dash, and go back through it after the first
swing. The recording shows the timings, the same both times:

| After | What |
|---|---|
| cast end + ~1.1 s | a 0.45 s dash of 6–7 yalms, towards the tethered player |
| dash end + 1.96 s | the first swing — the player was hit |
| dash end + 4.0 s | the second swing — the player was hit |

`EnemyCasts` follows each Sweeping Evisceration from its cast through the dash (seen as the Gargoyle
moving, then stopping). It hands the dodger:
- a 14-yalm circle while the Gargoyle casts,
- then a half-circle in front of the dash direction until the first swing,
- then a half-circle behind it until the second.

The dodger takes them in turn: out to 14 yalms, then behind the Gargoyle, then back through it to its
front. The harness checks all three steps with the recorded positions. If no dash is seen within three
seconds, the Gargoyle's own facing is used.

Tethers are not recorded yet. If the swings turn out to be the other way round, swap the two cones
in `SweepingEvisceration.Zones`.

### Treant, shops and Borgny — 2026-09-16

`captures/run-20260916-223448.txt`: the whole First Master's Board up to its boss. The Gargoyle was
survived; Sweeping Evisceration logged its dash both times and was dodged in turn.

**The Treant** keeps Sludge (3071) on the ground around its middle: an event object, base 2010106,
sits under it. Sludge set in 8.0 yalms from the middle. Walking in went to 2.5 yalms of the middle,
because `PathfindAndMoveCloseTo` was given melee reach from the middle rather than from the hitbox
edge. The player died four seconds later, having entered with 937 of 6632 HP. Changes:
- Walking in now stops at the hitbox's edge.
- `GroundHazards` lists patches that hurt for as long as they are there: 2010106 at 8.5 yalms, and
  Borgny's Poison Clouds (19674) at 6.5.
- The dodger keeps such patches alongside whatever else is due (`Zone.Lasting`). When only patches
  are around and none is underfoot, it does nothing.
- The route now counts the player's own HP share next to the familiars' when choosing a campsite.

**Shops buy Beast Gear** by default (`ShopBuysGear`). No full dump of the shop's values was ever taken,
so the layout comes from diffs and hovers:

| Value | Meaning |
|---|---|
| [1] | tokens, as text |
| [2] | number of offers |
| from [3], blocks of five | a Bool, the `XBMItem` row, the price as text, a Bool, and "bought" |

- Hovering offer 13 showed row 14 (Ice Shield), and buying `[2, 13]` bought the Ice Shield.
- Prices are five times `XBMItem` column 2: 140 → 700 and 91 → 455.
- Rows 1–76 are gear (kind 1). Feed and potions are kinds 2 and 3.
- `ShopBuyer` buys the dearest piece the tokens allow that is neither bought nor held. It answers the
  game's "Purchase the ice shield?" only when the question names the piece (`XBMItem` column 3), and
  answers no otherwise.
- Gear held is found as any value that is a gear piece's `Item` id (243001–243076). Both the shop and
  the coffer list what is held that way.
- The recorder now writes a window's values out whole once they change by 40 or more after opening,
  so the next shop leaves a full dump to check this against.

**Borgny the Venomous**, the board's boss, fights in a fifth arena at (920, −420).
- Toxic Breath (48807, 3 s) is followed by a leap back to the south wall (z −439.6), then a cleave
  2.8–2.9 s after the cast ends. 48808 is a donut of 60 with no omen.
- The player died standing 10 yalms in front of it. By the player's account, the one safe spot is
  right behind Borgny, against the wall.
- `ToxicBreath` gives the dodger a refuge: 6 yalms past where Borgny lands, straight back. It is
  outside the safe circle, and the walk ends at the wall.
- Fuming Vomit (48812) places circles of 6 that leave Poison Clouds behind; the clouds are ground
  hazards now.

### Two wipes on the second move — 2026-09-16

`captures/run-20260916-225232.txt` took the right-hand room (a Corpse Flower), and
`run-20260916-225756.txt` the left-hand one (a Morbol). Both ended in a wipe.

**Both second fights began low.** The Strix leaves the player at 20–25% (1282 and 1603 of 6199 HP). Auto-
heal is off for the whole run, and the second move has no campsite. No recording shows a crucible item
(potion) being used, so the way to use one is not known yet.

**The Morbol's breath turns.** The helper cast 48673 (a 90-degree cone of 50), then 48675 every 2.1 s,
each 45 degrees on: facings 3.14, −2.36, −1.57, −0.79, 0.00, then 0.79 for the next 48673.
- Repeats are now kept by caster and name, since the opening and repeating actions differ. Each
  predicted repeat is turned by the last step (`TurningHits.Step`).
- A cone's apex now takes the largest hitbox standing at its origin: the Morbol's, not its helper's.

**The Corpse Flower's Floral Trap** (48683, 5 s, a circle of 80) drew the player in, bound (2518) and
stunned (2656) them. Devour (48685, a cone of 8 in front) then ate them: Devoured (421) took 1100 HP to
19. Sapling Pieces leave briar patches (event object 2015458) just before. Briar (5176) "prevents
draw-in and knockback effects".
- `FloralTrap` now sends the player into the nearest patch.
- Refuges are walked to as soon as they are known, not only once they are the next hit.

A run started after a death no longer says "Revived" at its start.

### Drinking potions — 2026-09-16

`captures/run-20260916-231237.txt` ends with a G3 Beast Potion drunk by hand, at 23:17:43. The first try, at
23:16:33, came a moment too late: the player died with the menu open.

The run's HUD, `XBMContentsMainHUD`, lists ten item slots in blocks of five from value 9: a Bool, a Bool
while the slot holds something, the `Item` row, the `XBMItem` row and the name. Drinking goes like this:
- The HUD sends `[Int 6, Int slot, Undefined]` with the window closing.
- A `ContextMenu` opens offering "Use" and "Discard" (values 8 and 9, both managed strings).
- `[Int 0, Int 0, UInt 0, Undefined, Undefined]` on the menu uses the item.
- The slot's Bool turns false, and its name becomes "Crucible Item 1".

`Automation/ItemUser.cs` does the same, looking for "Use" among the menu's strings. It only drinks
healing items, by `XBMItem` row:

| Rows | Item | Heals |
|---|---|---|
| 76–79 | G1–G4 Beast Potion | 10 / 23 / 36 / 50% |
| 80–82 | G1–G3 Crucible Ash | 10 / 25 / 40%, familiars too |

- In a fight it drinks the strongest item at 40% HP or below.
- On the board, before walking on, it drinks up to 60%, using the smallest item that gets there.
- Both thresholds are settings. After a failed try it waits ten seconds.

Beast Gear is rows 1–75, not 1–76 as first assumed: row 76 is the G1 Beast Potion.

The recording also holds a board being entered from the entrance (23:17:05): the board window's
`[8]`, a `SelectYesno`, the duty finder's `ContentsFinderConfirm` `[8]`, and the load. That is what the
re-entry for more than one board (M6) needs.

### A run that would not start, and campsite overheal — 2026-09-16

**"Not on the board."** `captures/run-20260916-232409.txt`: three starts in a row sat in Deciding until
"The run is not on the board".
- The plugin was reloaded at 23:21 while the player was in a fight's arena. The room icons were turned
  into world positions with the arena's map: room 1 at (120, −393) instead of (−700, −9).
- Back on the board, the raw icon positions were unchanged. The join's signature held only those, so
  the rooms were never placed again. `OnBoard` stayed false, and the terrain was scanned around the
  arena: 57 s and a 9 MB file.
- The signature now includes the world positions. A wrong placement is replaced as soon as the map
  reads right, and the terrain signature then asks for a fresh scan by itself.

**Campsites share their healing.** The player alone recovers 90%. With one familiar, each recovers 45%:
at 21:56 that was 2789 of 6199 and the Opo-opo's 1571 of 3492. So with two familiars each gets 30%.
- `Rules/CampsiteRest.cs` picks the number of familiars that restores the most in total, counted as
  shares of each one's HP. A share that heals past full counts only up to full, and fewer wins a tie.
- The campsite's limit is read from its prompt ("You and 2 familiars can recover HP…").
- `HealthSelector.RequestPick(avoidOverheal)` then picks only that many of the most hurt. The setting
  `CampsiteAvoidOverheal` is on by default.

**Which map places the board.** The fix above made it worse: with world positions in the signature,
the join ran again in every fight's arena, and placed the rooms around the arena. The run then counted
itself as on the board, and after Commence Battle "the fight had not begun" (both recordings of
23:53 and 23:54). An arena shows another map of the same zone, with its own offsets. Markers now only
get a world position while the map shown is the zone's own map (TerritoryType's `Map`, 1219 for the
First Master's Board). Without any world positions the join keeps what it has.

### Out of potions at the Treant — 2026-09-17

`captures/run-20260916-235916.txt` (started 23:59): the fight started again. The recording shows:
- shop purchases (Thunder Armor, Thief's Boots);
- a campsite resting with one familiar ("you are missing 6%, the most hurt Cu Sith 13%");
- the Gargoyle survived, with Sweeping Evisceration dodged in turn;
- a G1 Crucible Ash drunk on the board.

The run reached the Treant at 1654 of 6199 HP, with nothing left to drink.
- Sludge set in at 8.4–8.7 yalms from the Treant while walking in. The patch is now 9.5.
- Walking in to a target stops outside any lasting patch centred on it (`EnemyCasts.HazardAround`).

**Healing is scarce, so it is bought and taken.**
- With the gear bought, the shop spends the tokens left on healing items, strongest first
  (`ShopBuysPotions`).
- At or below half HP, a coffer's strongest healing item is taken instead of gear
  (`TreasureHealBelow`).

### As far as Borgny — 2026-09-17

`captures/run-20260917-003214.txt` played the First Master's Board to its boss. What worked:
- shops bought gear and a G2 Beast Potion;
- the campsite rested with two familiars;
- the Gargoyle and the Treant were survived;
- potions were drunk in a fight and on the board;
- a coffer gave a G3 Crucible Ash at low HP.

At Borgny, the first Toxic Breath was dodged against the south wall without a scratch.

**The death:** Borgny leapt back to the middle, and Shield Charge carried the player after it, through
drifting Poison Clouds: two hits of about 850 and stacking Toxicosis. The clouds move; the ones placed at
(920, −408) were gone from (920, −400.5).
- Shield Charge now waits while any hit or patch is around (`BstState.MayDash`).
- Poison Clouds count as 7.5 yalms.

**The HP:** the run met Borgny at 45%, the last shop's 1100 tokens having gone on a Mystic Veil. At or
below the board's drinking threshold (60%), the shop now buys healing items before gear.

Borgny faced −0.77 when it cast the second Toxic Breath from the middle, so its refuge was predicted at the
south-east wall. Whether it leaps straight back from its facing there is not shown yet: the player died
before the leap.

### Borgny's turn, and area items — 2026-09-17

`captures/run-20260917-010634.txt` reached Borgny again, and the player ran into Toxic Breath twice.

**Which way Borgny leaps.** The player's account: Borgny jumps to the middle, turns, leaps towards its back
and cleaves. The recording pins it down:

| Breath | Facing at cast start | Player at cast start | Leap |
|---|---|---|---|
| 01:13:46 | 0.74 | just south of Borgny (919.9, −420.6) | due north, to (920, −400.5) |
| 01:14:37 | −3.12 | 16 yalms west (904.4, −413.9) | due east, to (939.6, −420) |

- The facing at cast start is not the one Borgny leaps from.
- Both leaps went along an axis, away from where the player stood as the cast began.
- The leap follows the cast's end by about 0.6 s and takes about one second.
- `ToxicBreath.FacingFor` now takes the axis towards the player at cast start. The refuge is the wall
  behind the landing, across from the player. Once Borgny is seen leaping, the leap's own direction
  replaces the guess.
- The recorder writes the target's facing whenever it turns ("facing" lines), so the moment of the turn
  shows next time.

**Area items.** The first room's spoils brought a Fang of Ice ("ranged ice damage with a potency of
2,000 to target and all enemies within 12 yalms"). The Fangs (`XBMItem` 128–134) and Celestial Sand
(139, 18 yalms) are now thrown from the HUD the same way potions are drunk. The target is set to one of
the adds first.
- A throw happens once `AreaItemAtAdds` (3) enemies attack the player or a familiar within 8 yalms,
  as the Treant's Slug Pieces do.
- The strongest enemy, the boss, is not counted as an add.

### Borgny again: its back, the opener, tornadoes — 2026-09-17

`captures/run-20260917-012255.txt`.

**Toxic Breath with the player on top of Borgny.** At 01:30:21 the player stood half a yalm from Borgny.
The "towards the player" rule gave south, so the player ran north. Borgny had walked in from the south,
still faced north, and leapt south, towards its back, as the player put it. Closer than 3 yalms, Borgny's
own facing is now taken, snapped to an axis and followed until it leaps. Farther away, the axis towards
the player still decides, which fits both earlier leaps. The "facing" lines were never written: the
first comparison was against NaN. That is fixed.

**The opener at the boss.** Shield Charge followed the first Battlehorn and pulled Borgny with one familiar
out. Before the pull, only horns and Borrow go out now, and nothing is engaged until two horns are out,
or one below the second horn's level, or no horn can still be used.

**The tornadoes** are event objects named "Magitek Armor" (2012932). Four rise, three seconds apart, where
Toxic Vomit (48809, a circle of 6 on the player) landed, and they stay until the next Toxic Vomit. One
rose under the player at 01:32:18 (2527 → 1113 → 598 → dead).
- They are ground hazards of 6.5 now.
- While Toxic Vomit is cast on the player, the player carries it to the arena's edge on the far side
  from Borgny (`ToxicVomit`, 15 yalms out).

**The Fang was refused.** It was thrown 0.1 s after Shieldsplitter, and the menu's "Use" did nothing. Items
are now only used without an animation lock and not while casting. If Fangs still fail, a recording of
one thrown by hand will show whether they need a target picked on the ground.

### Gargoyle tether, the Treant's two breezes, Borgny's clouds — 2026-09-17

`captures/run-20260917-014231.txt` and `captures/run-20260917-015708.txt`.

**Sweeping Evisceration** needs at least 20 yalms of tether (the user). A circle of 18 cannot hold that,
and the Gargoyle's arena (120, 0) is a square anyway. Its Malady grid spans 102.5–137.5, and Bleeding
began 21.5–22.5 yalms out along an axis. So `CrucibleArena.IsSquare` marks that centre, and the dodger
searches a square of ±19.5 there. `StretchRadius` is 20.

**Rustling Breeze** comes in two versions. The helpers all read facing 0, and the Treant turns to 0 as it
casts.

| Cast | Helpers | Shape | What the recordings show |
|---|---|---|---|
| 48776 | 48778 | One 90° cone ahead | The player stood 49° off the front and was not hit. |
| 48777 | 48779 + 48780 | Two 150° cones | The player stood 76° off the front and was hit twice (01:48:21, 02:01:25). |

The two 150° cones point to the sides, so they are turned ±90° (`RustlingBreeze.Turn`), and the middle in
front is safe, as the user plays it.

**Borgny, first death (01:50:00):**
- **Shield Charge during Toxic Vomit.** The cast ended at 29.5 and the vomit landed at 31.8. In between,
  the zone was gone and Shield Charge carried the player 13 yalms back to Borgny. The vomit now counts
  until 2.5 s after its cast (`LandsAfterCast`). No dash is used within 3 s of a dodge.
- **The tornadoes follow the player.** They rise every three seconds where the player just stood (4 in
  all), not only where the vomit landed. After the landing the player now runs round a ring of 15 for
  11 s, avoiding patches (`ToxicVomit.ChasePoint`).
- **Poison Clouds.** Fuming Vomit places three circles. Eight clouds rise from each, stay about 2.3 s,
  then drift outward along the eight compass ways at about 2.1 y/s and vanish at the edge. The dodger
  used to see them standing still, at 7.5.
  - The dodger now tracks each cloud's velocity and covers where it will be in 1.5 s
    (`GroundHazards.Drifting`), with a radius of 6.5.
  - The walk to a dodge spot goes round lasting patches (`Dodger.Route`, a grid search). Covered cells
    are allowed, but each costs 10 yalms. The route is handed to vnavmesh as waypoints.

**Borgny, second death (02:05:10):** Toxic Breath. The player walked from the middle straight to the
south wall, through eight clouds that had just risen at (920, −432). Hits of 1050 and 758 followed, and
the player died as Borgny landed. The leap direction (south, from its own facing) was right. The player
reached the wall before Borgny landed. Whether the landing itself hurts is still open.

**Chains of Condemnation (4562)** ("moving deals fire damage") came up at the Gargoyle and at Borgny. The
player moved 19 yalms under it without losing HP, so nothing is done about it yet.

### Starting the next board from the entrance (M6) — 2026-09-17

Taken from the two recorded re-entries, `run-20260916-231237.txt` (23:16) and `run-20260917-014231.txt`
(01:55). The steps are listed in `XbmColumns.Entrance`, and `Automation/Run/BoardEntrance.cs` plays them.

**Leaving the board.** `XBMResult` opens after a win and after a wipe (88%).
- By hand, the result's `[0]` was pressed every time (4 recordings). That opens `NeedGreed`, and the load
  only came once Need was rolled. The Need click has no callback and was not recorded, so `NeedGreed` and
  `XBMResult` are now watched for input events.
- At 01:55 the result timed out and closed with `[-1]`. The load followed at once, and the loot was handed
  over without a roll.
- So the run closes the result with `[-1]`. If the run is still on the board 8 s later, it presses `[0]`
  and hands the roll to the player.

**The entrance.**
- The load ends in Central Shroud (148) at 26.5/65.4. The NPC talked to is **Lauda** (EventNpc 1059759)
  at 25.5/67.5, 2.3 yalms away. EventObj 2015511 next to her is not targetable.
- Lauda's `SelectString` is answered `[0]`. Its options are logged, since they were not recorded.
- `XBMStageList` lists the board count at `[1]`, then name and board row in turn from `[2]`. `[1, row]`
  picks a board; `[2, row]` only previews it. The row is the one the run started on
  (`configuration.LastBoardRowId`).
- The board window opens with the team (`XBMPetParty` mode 0). After 2 s for the Crucible mode, `[8]` is
  sent. That is the same command as Commence Battle, and it is followed by a `SelectYesno`.
- `ContentsFinderConfirm` `[8]`.
- After the load comes a short cutscene, then `XBMContentsMainHUD` opens. "In Event" (1268) stays until
  the first room, so it is not waited for. The run goes back to Preflight.

**A lost board** counts as played when "go on after a lost board" is on (the default), and the next one
is started.

### Borgny at 19:29, the lag, and a Debug tab — 2026-09-19

`captures/run-20260917-191334.txt`. Boards 1 → 2 → 3 were started from the entrance on their own. The
boss was won, mostly by luck.

**No dodging without a target.** Borgny is untargetable while it charges with Cauterize. At 19:30:48 the
Toxic Mass had just died, so nothing was targetable, and the combat tick returned before planning a
dodge. The player stood 2 yalms off the charge's line and took 963 + 924. The second Toxic Breath
(19:30:17) went the same way. Dodges are now planned with no target too (`PlanDodge(player, null)`).

**The walk in.** The player stood 15–25 yalms from Borgny for most of the fight. With only patches on
the ground and the target out of reach, the dodger now searches a clear spot by the target.
- `TargetWeight` is 1.5, aimed half a step inside the reach.
- The walk there goes round the patches.
- While hits are still being cast, a clear player still stays put, as Bedrock Uplift needs.

**The lag.** Dalamud logged `CombatDriver::OnUpdate` at up to 740 ms. The causes:
- A zone was tested with 9 samples.
- Each cloud had three zones.
- The route search re-tested cells.

The fixes:
- `Zone.Covers(point, margin)` tests the grown shape once.
- A drifting cloud is one `Capsule`.
- Stacked clouds and tornadoes are merged.
- The route caches each cell's price and is bounded.

The harness times 34 patches at 4.4 ms for plan and route.

**Wriggling Phlegm** (48817 on Borgny) is the drop the user wants at the edge. Its helper places 48819,
a circle of 6, on the player 5.3 s in, and a Toxic Mass rises there. Until it is placed, the player goes
to a spot on the ring of 15, away from Borgny and clear of patches (`EdgeBait`). Toxic Vomit's spot uses
the same rule. **Cauterize** leaves a line of ten Poison Clouds along the charge; they are ordinary
hazards.

**The boards field** is read live (`BoardRunner.RunsWanted` is `configuration.RunCount`), so it can be
changed mid-run.

**Players from the repo had no Run tab.** The workflow had only ever run for `beastmastr-implementation`,
so the repository offered `v0.1.0.0` on both channels, and that version predates the automation.
Enabling testing builds changed nothing.

**UI.** The tab layout for players:

| Tab | What it holds |
|---|---|
| Beasts | As before. |
| Run | Start and stop, boards, what happens in the rooms, fight choices, what counts as taking over, the route and fork picks. |
| Settings | As before, plus "Show the Debug tab". |
| Debug | Hidden by default. Its pages are listed below. |

The Debug tab's pages:
- **Run:** plugins, the recorder, the run state, single steps, the fight driver, dodging and BossMod,
  the ground scan and the map.
- **Board:** the board captures.
- **Windows:** the window inspector.
- **Sheets:** the sheet explorer.

The setting is still stored as `ShowDataTab`, so old configs keep it. It takes effect at once: `ITab`
has a `Visible` property.

### Carries first, and one copy at a time — 2026-09-19

**Carries first.** `TeamPlanner.ForFight` calls the carries (`CarryBeasts`) that are in the team and not
down, in their order, then the last fight's familiars. It calls up to three: as many as last time, or
as many carries as can come if that is more. A familiar that is down is still replaced by the
healthiest one left. The Settings switch is `CallCarriesFirst`, on by default.

**Two copies at once.** At 14:37 the installed v0.2.0.0 and the dev build v0.2.1.0 were both loaded.
Dalamud only warns ("another plugin with the same assembly name was already loaded"). Both copies called
Cu Sith, and a pick toggles, so each undid the other's. The log read "The window did not take Cu Sith",
and nobody was called.

`Plugin` is now a shell and everything else is `PluginCore`. Every copy registers in Dalamud's data share
(`BeastMastr.Instances`, a `ConcurrentDictionary<string, string>` that every load context can read).
Exactly one copy builds its core. The order of preference is:
1. a dev build before an installed one,
2. then the higher version,
3. then the copy loaded first.

The others stay idle and say so in chat. When a preferred copy arrives, the active one disposes its core
first, and the newcomer builds only once no other copy is active. Copies older than 0.2.1.0 do not take
part.

### Second treasure picks, Devour, walking in during casts — 2026-09-19

**Two picks from a coffer.** An item lets a coffer be picked from twice. The pick goes through, the window
stays open, and at 15:32 the run handed the coffer to the player. Now the run tries again: when the window
is still open after a confirmed pick, that offer counts as taken and another is picked. It tries up to
four times before handing over.

**Corpse Flower, Devour.** The briar stopped the draw-in. Then the flower turned to the player, and the
walk back to it went straight into Devour, a cone of 8 in front: the trap ended at 21.0, and the player
was Devoured 5 yalms in front of it at 25.96 (17.09, 19:15). The briar is now held for 6.5 s after the
trap (`FloralTrap.DevourAfterTrap`).

**Walking in at Borgny.** A clear player stood still whenever anything was being cast, which at Borgny
is most of the time. Out of reach, a spot by the target is now walked to during casts too, as long as
it is clear of every cast under way, not only the soonest (Bedrock Uplift's next ring). With no such
spot, the player stays.

**Opener at Borgny.** No recording yet. Battlehorn, Borrow and Parting Blow are now logged ("Pressed …",
"before the pull") so the next run shows what happened. In the recording of 17.09, 19:29, Parting Blow
sent off the first familiar four seconds after the pull.

### Horns at Borgny, a T of tornadoes, Run from Lauda, farming teams — 2026-09-19

`captures/run-20260919-155513.txt`.

**The opener at Borgny.** Horn II was pressed at 16:17:36.32, the moment Borgny's opening cutscene let
go, and its cast was cut off after 0.09 s. Nobody came (`SummonedBeast` stayed 0). The driver counted
the horn anyway, went on to Horn I and Borrow, and pulled with one familiar.
- A horn is now only counted once its familiar is there (the gauge's summon count changes, or a
  familiar appears). One that summons nobody is forgotten, so it is pressed again.
- Nothing is pressed during a cutscene or event, nor for a second after.

**Toxic Vomit's tornadoes in a T** (the user: lay them round the boss for uptime). The four drops go on a
T round Borgny, in melee reach:
1. bar left,
2. bar right,
3. stem,
4. stem, one step further out.

The side across from the stem stays clear to fight from. The T is laid out as the cast starts, turned
the way that stays inside the arena and off the patches. The next spot is taken once the previous drop's
tornado has risen (the new "Magitek Armor" objects are counted).

**Area items at the Treant** wait until the adds have been on the player for 1.5 s (a slider), so one
throw catches the whole wave.

**Run from the entrance.** In Central Shroud, Run walks to Lauda with vnavmesh (her spot is known before
she is in the object table), then starts the board last played (`LastBoardRowId`).

**Team per board** (`RunTeam`), set in the board window before Challenge, by the TeamSelector:

| Setting | Team |
|---|---|
| Farming (default) | The carries alone. |
| Leveling | The carries, then the least advanced beasts, anew each board. |
| Keep | The team as it is. |

### No opener at the Treant, Borgny's last Parting Blow, a loot count — 2026-09-19

**The Treant was pulled before a horn** (three times, `run-20260919-155513.txt`). The Sludge lies under
the Treant from the start, so the dodger saw a patch and the target out of reach, and walked in with
"closing in" the moment the arena loaded (15:59:14). The ordinary walk-in waits for the opener; the
dodger's did not. Dodges are now planned without a target while the opener is not done, so only real
hits move the player before the pull.

**Borgny's third familiar** (the user): its Parting Blow is not spent on Borgny unless it finishes it.
`BstState.KeepLastPartingBlow` holds back the "make room for the next familiar" blow when the target is
Borgny (`Bosses.Borgny`, 19672) and three familiars have been summoned. The finishing blow
(`PartingBlowFinisherShare`) still goes. On the adds, nothing changes.

**Boards and loot** (`LootTracker`, Run tab):
- **Loot:** at a board's end the chat puts items on the loot list ("3 bright remnants of resilience have
  been added to the loot list."), then "You obtain 3 bright remnants of resilience." Only items that
  were on the list are counted. A room's spoils ("… as loot."), gil and tokens are not. Names are
  turned into the item's own name through the Item sheet's singular and plural (`LootLog` parses the
  lines).
- **Boards:** a board counts as finished when its result window opens.
- **Totals:** kept in the configuration, and for the session.

### Toxic Breath follows Borgny's back, and boards won — 2026-09-19

**From the second Toxic Breath on the player ran to Borgny's front.** Standing farther than 3 yalms off,
the facing was taken as "towards the player", snapped to an axis. Sixteen breaths in four recordings say
otherwise: Borgny turns for up to 0.65 s after the cast starts, and leaps exactly away from the way it
settles, wherever the player stands. For example, at 16:01:54 it settled at −1.51 and leapt east while
the player stood south.

Its facing is now followed for every breath, snapped to an axis until it leaps. It is only trusted once
it has held for 0.3 s or the cast is 1 s old; until then nothing is planned. The leap follows the cast
by more than a second, and the cleave comes 2.8 s after that, so there is time.

**Boards won.** The result window's value 3 is the board's completion: "100%" when the boss fell, "88%"
when it did not, in every recording. "Boards finished" now says how many were won
(`Configuration.BoardsWon`, and for the session).

### Every useful item in the final fight — 2026-09-19

The user asked for every item in the final fight, the Beast Potion Kit above all, but nothing before the
horns are out, so nothing pulls early. `Rules/BossItems.cs` picks from the `XBMItem` rows (kind 2 are
the consumables).

In order, each once per fight:
1. the Beast Potion Kit (140, "Grants Auto-potion to self"),
2. the reraisers (99, 98),
3. the antipoison serums (87, 86),
4. the remedy kit (141),
5. tannin (102) and stimulant (103),
6. the tempered potions (104–112),
7. breathtaking swiftness (114),
8. vampiric essence (135),
9. the tomes of reflection (136) and the impervious (137),
10. the feather (115),
11. the weakeners (116–127).

The antidote (83) is used again whenever one of Borgny's Toxicosis forms is on the player. The Fangs and
Celestial Sand go at the boss.

Never used:
- the feral potions (each also stuns, blinds, petrifies or puts to sleep the user),
- the smokebomb,
- the Spellforge and Steelsting tomes,
- Temporal Sand,
- the eyes,
- the needle and the Blessed Horn.

The healing items still go by HP.

The runner marks the boss room's fight (`CombatDriver.BossFight`). Items only go once two horns have
brought their familiars (`hornsThisFight >= 2`, no horn pending). "In the final fight, use every useful
item" on the Run tab switches it off.

### The Strix's levitation puddle, and 0.3.0.0 — 2026-09-19

`captures/run-20260919-224253.txt`. After Plummet the Strix (19638) leaves three puddles on three of the
four spots (110|130, −410|−430). It then casts On the Properties of Quakes (48657, 60 yalms, 7.7 s),
then Magical Mallet Theory. The user: one puddle floats the player over the quake; the other two
protect against later mechanics but stop the player attacking. The boss has to be pulled to the
floating one, which Heel does while the familiar tanks.

- **The puddles:** event objects 2004354, 2015456 and 2015457, shuffled over the spots from fight to
  fight. None has a name. `EObj` column 11 leads through `ExportedSG` to their effects:
  - 2004354: `bgcommon/world/btl/shared/for_vfx/sgvf_w_btl_b0483.sgb`, an effect shared across the game;
  - 2015456 and 2015457: this board's own (`fst_f1/…b4224`, `…b4223`), like the briar patch (`…b4225`).

  No recording has the player standing in one, so which one floats is learned. 2004354 is tried first.
  Standing in a puddle for 1.5 s, the statuses gained are named in the log. A name with "Levitat",
  "Float" or "Airborne" marks the puddle as the one (`StrixLevitationPuddle`); anything else marks it
  as wrong (`StrixNotLevitation`), and the next fight tries another.
- **The plan:** from the moment the puddles appear until the quake is over, the puddle is a refuge
  (`StrixPuddles`). While the player is sent there and the familiar holds the Strix out of reach, Heel
  (`PetAction` 2) goes out every 4 s.

The Beast Potion Kit worked: Auto-potion at 22:50:33 and 22:59:46.

Version 0.3.0.0 has everything since 0.2.0.0.
