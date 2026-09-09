# BeastMastr

Quality of life for Beastmaster: the Master's Bestiary and the Crucible of the Unbroken.

The game already knows what every beast's actions do and what every Crucible room holds. It just
makes you click for it. BeastMastr puts that where you are already looking.

- **Bestiary** — see at a glance what a beast brings: which status it lands, what damage type it
  deals, what it resists. Filter and search by that, so "which of mine can interrupt?" is one
  question instead of forty entries.
- **Crucible board** — the facts that decide a route sit next to the room on the board instead of
  one click deep, and the beasts worth bringing are highlighted when you enter a room.

Opens with `/beastmastr`.

## Status

Early. The plugin currently ships the data explorer that the rest is being built on — the
Beastmaster sheets are unnamed in the game data, so their columns have to be identified before
anything can be derived from them. See `BeastMastr/README-DEV.md`.

## Building

```
dotnet build -c Release
```

Needs Dalamud in `%AppData%\XIVLauncher\addon\Hooks\dev`, which XIVLauncher puts there itself.
