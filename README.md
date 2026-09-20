# BeastMastr

Quality of life for Beastmaster: the Master's Bestiary and the Crucible of the Unbroken.

The game already knows what every beast's actions do and what every Crucible room holds. It just
makes you click for it. BeastMastr puts that where you are already looking.

- **Bestiary** — see at a glance what a beast brings: which status it lands, what damage type it
  deals, what it resists, and the rank it has reached. Filter and search by that, so "which of mine
  can interrupt?" is one question instead of forty entries. Ranks are learned by browsing and only
  ever go up: a board that syncs your beasts down does not write them down.
- **Crucible board** — the facts that decide a route sit next to the room on the board instead of
  one click deep, and the beasts worth bringing are highlighted when you enter a room.

- **Board automation (experimental)** — `/beastmastr run` plays a Crucible board by itself.
  Needs [vnavmesh](https://github.com/awgil/ffxiv_navmesh).
  - **Where to press Run:** on a board's start platform, or anywhere in Central Shroud; there it walks
    to Lauda and starts the board last played.
  - **What it does:** walks from room to room and picks the route (forks can be picked on the board
    window). It buys Beast Gear and potions, rests at campsites, takes treasure — twice from a coffer
    that allows it — and fights: an opener with the Battlehorns and Borrow, duty actions, potions,
    Fangs at adds, and every useful item in the final fight.
  - **Dodging:** it dodges the mechanics it knows, including the First Master's Board's own.
  - **Several boards:** with a board count above one (`/beastmastr run 5`, or "boards" on the Run tab)
    it leaves the result and starts the next board from the entrance. Which board and which Crucible
    mode are picked beside the Run button, and each board's team can be set to farming (the carries
    alone) or leveling (the carries and the least advanced beasts).
  - **Auto-repair:** switched on, gear below full durability sends the run to the repair window between
    boards; pressing the repair is yours, and the run carries on once everything is whole.
  - **Carries:** marked with "Add as carry" in the bestiary's right-click menu. They are also called
    into every fight first.
  - **Tracking:** the Run tab counts the boards finished and won, how long they take, and the loot
    rolled for at their end.
  - **Stopping:** `/beastmastr stop` ends it. Moving, jumping, targeting or using an action yourself
    pauses the run; it carries on a few seconds after you let go.

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
