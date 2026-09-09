# BeastMastr — implementation notes

Written for whoever touches this next, including future me.

## Layout

| Folder | What lives there |
|---|---|
| `Data/` | Reading the game: Excel sheets, addon values and node trees |
| `Rules/` | Pure calculation: trait classification, room requirements, beast ranking |
| `Native/` | Everything that mutates the game's UI: KamiToolKit node injection |
| `UI/` | ImGui windows and tabs |

`Data` and `Rules` never change game state. `Native` is the only place that writes.
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
| 9, 10 | String | The Trick and the Tempered Release, in that order. Descriptions only — no names |
| 11..21 | Bool ×11 | **Which status the beast inflicts.** The feature this plugin is built on |
| 22..26 | UInt8 ×5 | Strength, Intelligence, Phys. Resistance, Mag. Resistance, Constitution |

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

1. **Borrow.** Each beast has three actions, grouped on the detail page as **Trick**, **Tempered
   Release** and **Borrow**, each with an unlock level. Columns 9 and 10 are the first two
   descriptions in that order. Borrow is not in the sheet — and neither are any of the three
   action *names*, only descriptions, so both have to come from somewhere else.
2. **Classification names** for column 1's values 2..8. 1 is Beastkin.
3. **Beast rank.** Column 3 is inferred by elimination and has not been seen against a named beast.
4. **The habitat column.** Column 2 is not a `PlaceName` id: goobbue's habitat is Lower La Noscea,
   which is `PlaceName` 31, and its column 2 is 21. All 7912 sheets were searched for one holding
   that string at row 21, and for one linking both goobbue's 21 → 31 and squirrel's 35 → Central
   Shroud numerically. Both searches came back empty, so the resolution goes through something not
   yet found. Column 6 switches between three branches, not two: 0 for one beast, 1 for the
   overworld ones, 2 for the late ones that live in duties.
5. **Auto-attack damage type.** The detail page shows one — goobbue's is Blunt, which is
   `XBMElement` 7 — and no column of `XBMPet` has been matched to it.

6. **Whether the map and the detail list agree on room count.** The board capture showed thirteen
   visible room nodes; the detail list described sixteen rooms. The two were taken eight seconds
   apart on different screens, so this may be nothing — but a capture with both open at once is
   needed before an overlay pairs them up.

Answered, and no longer open: what columns 22..26 are, whether the notebook's list recycles, and
what all eleven status slots are.

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
