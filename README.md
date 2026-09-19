# BeastMastr

Quality of life for Beastmaster: the Master's Bestiary and the Crucible of the Unbroken.

The game already knows what every beast's actions do and what every Crucible room holds. It just
makes you click for it. BeastMastr puts that where you are already looking.

- **Bestiary** — see at a glance what a beast brings: which status it lands, what damage type it
  deals, what it resists. Filter and search by that, so "which of mine can interrupt?" is one
  question instead of forty entries.
- **Crucible board** — the facts that decide a route sit next to the room on the board instead of
  one click deep, and the beasts worth bringing are highlighted when you enter a room.

- **Board automation (experimental, testing builds)** — `/beastmastr run` plays a Crucible board by
  itself: it walks from room to room, picks the route, buys Beast Gear and potions, rests at
  campsites, takes treasure and fights, dodging the mechanics it knows. With a board count above one
  (`/beastmastr run 5`, or "boards" on the Run tab) it leaves the result and starts the same board
  again from the entrance. `/beastmastr stop` ends it.
  Needs [vnavmesh](https://github.com/awgil/ffxiv_navmesh). Moving, jumping, targeting or using an
  action yourself pauses the run; it carries on a few seconds after you let go.

Opens with `/beastmastr`.

## Status

The automation is tuned on the First Master's Board and still dies to some mechanics there. Other
boards have enemies it has never seen: it dodges their casts from the game data alone. Settings are
on the Run tab. If a run goes wrong, a recording (`/beastmastr record`) of it is the most useful thing
to send along. The tools for that — recorder, single steps, the fight's decisions, the ground scan —
are on the Debug tab (Settings → "Show the Debug tab"). See `BeastMastr/README-DEV.md`.

## Building

```
dotnet build -c Release
```

Needs Dalamud in `%AppData%\XIVLauncher\addon\Hooks\dev`, which XIVLauncher puts there itself.
