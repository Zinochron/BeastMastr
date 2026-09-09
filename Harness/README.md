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

Once `Rules/` exists this file also becomes where its pure calculation is asserted, the way
LootMastr's harness covers `Planning/` — `Rules/` carries no Dalamud references precisely so that
it can be checked here.

## How the current column mapping was derived

Correlation, not guesswork. For each of `XBMPet`'s eleven bool columns, list every beast that sets
it next to its action descriptions, and read off what they have in common: the three beasts setting
column 13 are the three whose actions paralyse. Nine of the eleven fell out that way in one pass.
What did not is written down as open in `BeastMastr/README-DEV.md` rather than guessed.
