# Sheet harness

`Program.cs` reads the Beastmaster sheets straight out of the installed game files with Lumina. It
is not a test project and is not built with the plugin — **no client has to be running**, because
Lumina opens `sqpack` directly.

That property is the point. The Beastmaster sheets are unnamed upstream, so identifying a column
means dumping it next to something you can recognise and correlating. Doing that offline is far
faster than opening the game for every question, and it is repeatable after a patch.

To run it:

```bash
dotnet new console -o /tmp/xbm --force
cp Harness/Program.cs /tmp/xbm/
cd /tmp/xbm && dotnet add package Lumina --version 7.6.0 && cd -
dotnet run --project /tmp/xbm
```

The game path is hardcoded at the top of `Program.cs`; change it if the install moves.

It now also asserts on `Rules/`, the way LootMastr's harness covers `Planning/`. Copy the pure
files in beside it:

```bash
cp BeastMastr/Rules/*.cs /tmp/xbm/
```

That `Rules/` compiles here at all is half the check — it carries no Dalamud references, and if it
ever needs one, something game-facing has leaked into the calculation layer.

The beasts are built from the **real sheets** rather than invented. A classifier that passes on
three hand written sentences and quietly misses fourteen of fifty is exactly the failure worth
catching, and only the real corpus catches it.

## How the current column mapping was derived

Correlation, not guesswork. For each of `XBMPet`'s eleven bool columns, list every beast that sets
it next to its action descriptions, and read off what they have in common: the three beasts setting
column 13 are the three whose actions paralyse. Nine of the eleven fell out that way in one pass.
What did not is written down as open in `BeastMastr/README-DEV.md` rather than guessed.


## What it pins down

- All fifty beasts resolve, each with three named actions and a named classification, across all
  eight classifications.
- No action that describes dealing damage is left without a damage type. Actions that deal none —
  "Hastens allies." — are expected to come back with none; the check is worded to allow that after
  a first attempt flagged "Increases physical damage dealt by allies" as a miss.
- The four beasts whose statuses were read off the party window in game come back the same from the
  sheet: dullahan interrupts, diremite binds and poisons, slime only binds, pugil does neither.
- Cleanse and dispel, the two things that exist only as prose, land on exactly one beast each — bat
  and vulture — rather than on none or on half the roster.
- Borrow is shared across a classification, and coblyn's is Soul Crush.
- Selecting two statuses means both, not either.
- Free text reaches action descriptions, so "knocks back" finds beasts no status flag would.

## One result worth knowing

**Interruption is exactly the Soulkin** — all five of them, nobody else. It comes from Soul Crush,
the Borrow every Soulkin shares, so "which of my beasts can interrupt?" has the same answer as
"which are Soulkin?", and that answer is five of fifty. The filter is still worth having, but the
underlying fact is simpler than it looked. Asserted, so that if a patch ever breaks the
equivalence, the reason surfaces rather than passing unnoticed.
