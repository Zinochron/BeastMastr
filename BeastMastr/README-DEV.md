# BeastMastr — implementation notes

Written for whoever touches this next, including future me.

## Layout

| Folder | What lives there |
|---|---|
| `Data/` | Reading the game: Excel sheets, addon values and node trees, the beast catalogue |
| `Rules/` | Pure calculation: the beast model, trait classification, filtering |
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

### "Remove all" is not wired yet, and why

The game's right-click menu has "Remove all", which would empty the team in one step. What it sends is
not in any capture — and could not have been: the recorder only kept callbacks from windows named
`XBM*`, and a right-click menu is its own window, `ContextMenu`. It now also records `ContextMenu`
and `SelectYesno`. Until that click is recorded, emptying uses the per-beast toggle that is already
proven, which is slower but not guessed.

### The roster's button

Hung off the same parent as the roster's list component and placed above it, for the same
same-units reason as the bestiary's. If the list sits flush with the top it goes below instead — over
the first row it would take that row's clicks. Nobody has captured the roster's node tree, so the
board report now includes it: if the button is wrong, one capture has the numbers to fix it.
