using System.Numerics;
using BeastMastr.Rules;
using Lumina;
using Lumina.Data;
using Lumina.Excel;

// Asserts on Rules/, which carries no Dalamud references — that it compiles here at all is half the
// check. The beasts are built from the real sheets rather than invented, because a classifier that
// passes on three hand written sentences and misses fourteen of fifty is the failure worth catching.

var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {name}{(detail.Length > 0 ? $"  [{detail}]" : "")}");
    if (!ok) failures++;
}

var game = new GameData(@"U:\SteamLibrary\steamapps\common\Final Fantasy XIV\game\sqpack",
                        new LuminaOptions { DefaultExcelLanguage = Language.English });

ExcelSheet<RawRow> S(string n) => game.Excel.GetSheet<RawRow>(null, n);
var xbmPet = S("XBMPet");
var petSheet = S("Pet");
var actions = S("Action");
var addon = S("Addon");

static string Cap(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
static uint Num(RawRow r, int c) => Convert.ToUInt32(r.ReadColumn(c));

var beasts = new List<Beast>();
foreach (var row in xbmPet)
{
    if (row.RowId == 0) continue;
    if (!petSheet.TryGetRow(Num(row, 0), out var pet)) continue;

    var cls = (int)Num(row, 1);
    var slots = new (ActionSlot Slot, uint Id, int Text)[]
    {
        (ActionSlot.Trick, Num(pet, 1), 9),
        (ActionSlot.TemperedRelease, Num(pet, 2), 10),
        (ActionSlot.Borrow, (uint)(44895 + cls), -1),
    };

    var built = new List<BeastAction>();
    foreach (var (slot, id, text) in slots)
    {
        var name = actions.TryGetRow(id, out var a) ? a.ReadStringColumn(0).ExtractText() : "";
        var desc = text < 0 ? "" : row.ReadStringColumn(text).ExtractText();
        built.Add(new BeastAction(slot, Cap(name), desc,
                                  TraitClassifier.DamageOf(desc), TraitClassifier.TraitsOf(desc)));
    }

    var statuses = new HashSet<BeastStatus>();
    for (var i = 0; i < 11; i++)
        if (row.ReadColumn(11 + i) is true) statuses.Add((BeastStatus)i);

    beasts.Add(new Beast(row.RowId, Cap(pet.ReadStringColumn(0).ExtractText()), Num(row, 4), cls,
                         addon.TryGetRow((uint)(17740 + cls), out var l) ? l.ReadStringColumn(0).ExtractText() : "",
                         "", built, statuses));
}

Beast By(string name) => beasts.First(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

Console.WriteLine();
Check("fifty beasts", beasts.Count == 50, $"{beasts.Count}");
Check("every beast has three actions", beasts.All(b => b.Actions.Count == 3));
Check("every action is named", beasts.All(b => b.Actions.All(a => a.Name.Length > 0)));
Check("every classification is named", beasts.All(b => b.ClassificationName.Length > 0));
Check("eight classifications", beasts.Select(b => b.Classification).Distinct().Count() == 8);

// Not every action deals damage — "Hastens allies." is a pure buff and None is the right answer.
// What must not happen is an action that plainly describes damage and classifies as None.
var missed = beasts.SelectMany(b => b.Actions)
                   .Where(a => a.Description.Length > 0
                               && a.Damage == DamageType.None
                               // "Increases physical damage dealt by allies" mentions damage and
                               // deals none; an action that deals it says "deals ... damage".
                               && (a.Description.Contains("attack", StringComparison.OrdinalIgnoreCase)
                                   || (a.Description.Contains("deals", StringComparison.OrdinalIgnoreCase)
                                       && a.Description.Contains("damage", StringComparison.OrdinalIgnoreCase))))
                   .ToList();
Check("no damaging action is left unclassified", missed.Count == 0,
      string.Join(" | ", missed.Take(3).Select(a => $"{a.Name}: {a.Description}")));

var damaging = beasts.SelectMany(b => b.Actions).Count(a => a.Damage != DamageType.None);
Check("most actions do classify", damaging > 60, $"{damaging} of {beasts.Count * 3}");

// Statuses come from the game's own flags, and these four were read off the party window in game.
Check("dullahan interrupts", By("Dullahan").Inflicts(BeastStatus.Interruption));
Check("diremite binds and poisons",
      By("Diremite").Inflicts(BeastStatus.Bind) && By("Diremite").Inflicts(BeastStatus.Poison));
Check("slime binds only",
      By("Slime").Statuses.SetEquals(new[] { BeastStatus.Bind }));
Check("pugil inflicts nothing", By("Pugil").Statuses.Count == 0);

// The two things that exist only as prose.
Check("vulture dispels", By("Vulture").Has(BeastTrait.Dispel));
Check("bat cleanses", By("Bat").Has(BeastTrait.Cleanse));
Check("bat also heals", By("Bat").Has(BeastTrait.Heal));
Check("nothing else cleanses",
      beasts.Count(b => b.Has(BeastTrait.Cleanse)) == 1,
      string.Join(", ", beasts.Where(b => b.Has(BeastTrait.Cleanse)).Select(b => b.Name)));

// Borrow follows the classification, so every beast of one shares it.
var borrowPerClass = beasts.GroupBy(b => b.Classification)
                           .All(g => g.Select(b => b.Actions[2].Name).Distinct().Count() == 1);
Check("Borrow is shared across a classification", borrowPerClass);
Check("coblyn borrows Soul Crush", By("Coblyn").Actions[2].Name == "Soul Crush");
Check("coblyn's Trick is Bestial Thunder", By("Coblyn").Actions[0].Name == "Bestial Thunder");

// Filtering.
var sleepers = new BeastFilter();
sleepers.Statuses.Add(BeastStatus.Sleep);
Check("someone sleeps", sleepers.Apply(beasts).Any(),
      string.Join(", ", sleepers.Apply(beasts).Select(b => b.Name)));

var both = new BeastFilter();
both.Statuses.Add(BeastStatus.Bind);
both.Statuses.Add(BeastStatus.Poison);
var bindAndPoison = both.Apply(beasts).ToList();
Check("two statuses means both, not either",
      bindAndPoison.All(b => b.Inflicts(BeastStatus.Bind) && b.Inflicts(BeastStatus.Poison))
      && bindAndPoison.Count < sleepers.Apply(beasts).Count() + beasts.Count,
      string.Join(", ", bindAndPoison.Select(b => b.Name)));

var byText = new BeastFilter { Search = "knocks back" };
Check("free text reaches action descriptions", byText.Apply(beasts).Any(),
      string.Join(", ", byText.Apply(beasts).Select(b => b.Name)));

var byClass = new BeastFilter();
byClass.Classifications.Add(By("Coblyn").Classification);
Check("classification filter keeps only that kin",
      byClass.Apply(beasts).All(b => b.ClassificationName == "Soulkin"));

var empty = new BeastFilter();
Check("an empty filter keeps everyone", empty.Apply(beasts).Count() == beasts.Count);

// Interruption turns out to be exactly the Soulkin, all five of them and nobody else — because it
// comes from Soul Crush, the Borrow every Soulkin shares. So "which of mine can interrupt" has the
// same answer as "which are Soulkin", and the answer is five of fifty. Pinned down because if a
// patch ever breaks the equivalence, the reason will be worth knowing.
var soulkin = beasts.Where(b => b.ClassificationName == "Soulkin").ToList();
var interrupters = beasts.Where(b => b.Inflicts(BeastStatus.Interruption)).ToList();
Check("interruption is exactly the Soulkin",
      interrupters.Count == soulkin.Count && interrupters.All(b => b.ClassificationName == "Soulkin"),
      $"{interrupters.Count} interrupt, {soulkin.Count} Soulkin");
Check("and there are few of them", soulkin.Count == 5, $"{soulkin.Count}");

// Levelling picks the least advanced, keeps the carries, and changes as little as it can.
var ranked = new[]
{
    new TeamPlanner.Candidate(1, "Carry", 10),
    new TeamPlanner.Candidate(2, "OnTeamLow", 1),
    new TeamPlanner.Candidate(3, "OffTeamLow", 1),
    new TeamPlanner.Candidate(4, "OnTeamHigh", 9),
    new TeamPlanner.Candidate(5, "OffTeamMid", 4),
};

var onTeam = new uint[] { 1, 2, 4 };
var levelled = TeamPlanner.ForLeveling(ranked, 3, new uint[] { 1 }, onTeam).Select(c => c.BeastNumber).ToList();

Check("levelling keeps the carry", levelled.Contains(1u), string.Join(",", levelled));
Check("levelling takes the least advanced", levelled.Contains(2u) && levelled.Contains(3u),
      string.Join(",", levelled));
Check("a carry does not have to be least advanced to stay", levelled.Count == 3);

// The tie-break that matters: two beasts at rank 1, one already on the team. Without preferring it,
// the lower bestiary number wins and the team churns for nothing.
var tie = new[]
{
    new TeamPlanner.Candidate(10, "OffTeam", 1),
    new TeamPlanner.Candidate(20, "OnTeam", 1),
};

var kept = TeamPlanner.ForLeveling(tie, 1, null, new uint[] { 20 }).Single().BeastNumber;
Check("an equal rank already on the team is kept over a lower number", kept == 20u, $"kept {kept}");

var byNumber = TeamPlanner.ForLeveling(tie, 1, null, null).Single().BeastNumber;
Check("with nobody on the team the number decides", byNumber == 10u, $"kept {byNumber}");

Check("the same inputs give the same team twice",
      TeamPlanner.ForLeveling(ranked, 3, new uint[] { 1 }, onTeam).Select(c => c.BeastNumber)
                 .SequenceEqual(levelled));

// What a room asks you to bring. The strings are the game's own words in the player's language, so
// the rules turn on shape and on the one word the game uses for "you cannot interrupt this".
var room = new[]
{
    new BriefEnemy("Diremite", "Wind",
    [
        new BriefAction("Venom Spray", "Poison", "Effective", false),
        new BriefAction("Skitter", "", "Ineffective", false),
    ]),
    new BriefEnemy("Goobbue", "Ice",
    [
        new BriefAction("Moldy Sneeze", "Paralysis", "Ineffective", true),
    ]),
};

var needs = RoomBriefing.Needs(room);
Check("an interruptible action asks for an interrupt", needs.Contains("Interrupt"), string.Join(" | ", needs));
Check("an uncovered status asks for a cleanse, and names it",
      needs.Any(n => n.StartsWith("Cleanse") && n.Contains("Poison")), string.Join(" | ", needs));
Check("a status the team already nullifies is not asked for",
      !needs.Any(n => n.Contains("Paralysis")), string.Join(" | ", needs));

Check("both weaknesses are listed once each",
      RoomBriefing.Weaknesses(room).SequenceEqual(new[] { "Wind", "Ice" }));

Check("every status shows up when covered ones are wanted too",
      RoomBriefing.Statuses(room).OrderBy(s => s).SequenceEqual(new[] { "Paralysis", "Poison" }));

var lines = RoomBriefing.Lines(room);
Check("an action that does nothing worth preparing for is left out",
      !lines.Any(l => l.Contains("Skitter")), string.Join(" / ", lines));
Check("a covered status is still shown, marked as covered",
      lines.Any(l => l.Contains("Paralysis") && l.Contains("covered")), string.Join(" / ", lines));

// "Ineffective" is the only value the enemy panel has ever been seen to use, so anything else has
// to count as interruptible: hiding an interrupt that is needed costs the run.
Check("an unrecognised interruption still offers the interrupt",
      RoomBriefing.Interruptible("Highly effective"));
Check("nothing said about interrupting is not an offer to interrupt",
      !RoomBriefing.Interruptible(""));

var quiet = new[] { new BriefEnemy("Sheep", "Fire", []) };
Check("a room with nothing to prepare for asks for nothing", RoomBriefing.Needs(quiet).Count == 0);

// The board as a graph, straight out of the sheets the board window itself is built from. Every one
// of the five boards has to come out whole: one start, one boss, nothing that leads nowhere.
List<BoardCell> Cells(uint board)
{
    var sheet = game.Excel.GetSubrowSheet<RawSubrow>(null, "XBMContentStageEventMap");
    return sheet.TryGetRow(board, out var rows)
               ? rows.Select(r => new BoardCell(Convert.ToInt32(r.ReadColumn(0)), Convert.ToInt32(r.ReadColumn(1)),
                                                Convert.ToInt32(r.ReadColumn(2)), Convert.ToInt32(r.ReadColumn(3)),
                                                Convert.ToInt32(r.ReadColumn(4))))
                     .ToList()
               : [];
}

List<BoardEventInfo> Events(uint board)
{
    var sheet = game.Excel.GetSubrowSheet<RawSubrow>(null, "XBMContentStageEvent");
    return sheet.TryGetRow(board, out var rows)
               ? rows.Select(r => new BoardEventInfo(r.SubrowId, Convert.ToInt32(r.ReadColumn(0)),
                                                     BoardGraph.KindOfEventType(Convert.ToInt32(r.ReadColumn(1)))))
                     .ToList()
               : [];
}

var graphs = new Dictionary<uint, BoardGraph>();
for (var board = 1u; board <= 5; board++)
{
    var g = BoardGraph.Build(Cells(board), Events(board));
    graphs[board] = g;
    Check($"board {board} is a whole graph", g.IsValid, string.Join(" | ", g.Problems));
}

var first = graphs[1];
Check("board 1 runs nine moves to its boss", first.LastMove == 9 && first.Boss?.Move == 9, $"{first.LastMove}");
Check("board 1 forks at move 2 and rejoins at move 4",
      first.OnMove(2).Count == 2 && first.OnMove(3).Count == 2 && first.OnMove(4).Count == 1);
Check("the start leads to the first enemy", first.Next(0).Single().Kind == BoardRoomKind.Enemy);
Check("the room list starts at the boss", first.RoomListIndex(first.Boss!.EventIndex) == 0);
Check("the fifth board's sideways links are one edge each",
      graphs[5].Edges.Count(e => e.From == 17) == 1 && graphs[5].Edges.Count(e => e.From == 16) == 1);

// The icons of board 1 as a capture in the run listed them (captures/board-20260909-200727.txt).
var icons = new List<MapPoint>
{
    new(0, 1856, BoardRoomKind.Enemy, -700f, -9f),
    new(-320, 1376, BoardRoomKind.Enemy, -705f, -16.5f),
    new(320, 1376, BoardRoomKind.Enemy, -695f, -16.5f),
    new(-320, 800, BoardRoomKind.Campsite, -705f, -25.5f),
    new(320, 800, BoardRoomKind.Treasure, -695f, -25.5f),
    new(0, 320, BoardRoomKind.Enemy, -700f, -33f),
    new(0, -256, BoardRoomKind.Treasure, -700f, -42f),
    new(0, -832, BoardRoomKind.EliteEnemy, -700f, -51f),
    new(-320, -1312, BoardRoomKind.Treasure, -705f, -58.5f),
    new(320, -1312, BoardRoomKind.Campsite, -695f, -58.5f),
    new(0, -1792, BoardRoomKind.Shop, -700f, -66f),
    new(0, -2368, BoardRoomKind.Boss, -700f, -75f),
};

var joined = BoardJoin.Join(first, icons);
Check("every room of board 1 finds its icon", joined.IsComplete && joined.ByEvent.Count == 12,
      string.Join(" | ", joined.Problems));
Check("the campsite on move 3 is the left one",
      joined.ByEvent.TryGetValue(4, out var campIcon) && campIcon.MapX == -320
      && first.Node(4)!.Kind == BoardRoomKind.Campsite);

var mirrored = icons.Select(i => i with { MapX = -i.MapX }).ToList();
Check("mirrored icons are refused, not joined", !BoardJoin.Join(first, mirrored).IsComplete);

var missing = icons.Skip(1).ToList();
Check("a missing row of icons is refused", !BoardJoin.Join(first, missing).IsComplete);

// Board 2's tiles as the entrance window drew them (captures/board-20260913-145220.txt), by cell index.
var second = graphs[2];
var secondCells = Cells(2);
var drawn = new Dictionary<int, (float X, float Y)>
{
    [0] = (1121, 808), [2] = (1121, 838), [4] = (1152, 869), [6] = (1091, 869), [9] = (1121, 899),
    [28] = (1121, 1112), [19] = (1152, 1021), [21] = (1091, 1021),
};
var tiles = drawn.Select(d => ((float)secondCells[d.Key].X, (float)secondCells[d.Key].Y,
                               new System.Numerics.Vector2(d.Value.X + 25, d.Value.Y + 25))).ToList();
var projection = PreviewProjection.Fit(tiles, []);
Check("the window's grid is fitted", projection != null);
var secondStart = second.Start!;
var startAt = projection!.GridToScreen(secondStart.X, secondStart.Y);
Check("the start lands where the window drew it",
      Math.Abs(startAt.X - 1146) < 2 && Math.Abs(startAt.Y - 1137) < 2, $"{startAt.X:0}/{startAt.Y:0}");

var anchors = joined.ByEvent.Select(p => ((float)first.Node(p.Key)!.X, (float)first.Node(p.Key)!.Y,
                                          p.Value.WorldX, p.Value.WorldZ)).ToList();
var firstTiles = first.Nodes.Select(n => ((float)n.X, (float)n.Y,
                                          new System.Numerics.Vector2(100 + (30 * n.X), 100 + (30 * n.Y)))).ToList();
var world = PreviewProjection.Fit(firstTiles, anchors)!;
var campsite = first.Node(4)!;
var placed = world.WorldToGrid(-705f, -25.5f);
Check("a room's world position maps back onto its own cell",
      placed is { } onCell && Math.Abs(onCell.X - campsite.X) < 0.01f && Math.Abs(onCell.Y - campsite.Y) < 0.01f,
      $"{placed}");
var between = world.WorldToGrid(-700f, -29.25f)!.Value;
Check("halfway between two rows is halfway between their cells",
      Math.Abs(between.Y - ((first.Node(4)!.Y + first.Node(6)!.Y) / 2f)) < 0.01f, $"{between.Y}");

// Which way to go.
var noneChosen = new HashSet<int>();
var nothingBlocked = new HashSet<(int, int)>();
var planned = RoutePlanner.Plan(first, 0, noneChosen, RoutePreferences.Default, 1f, nothingBlocked);
Check("a plan runs from the next room to the boss",
      planned.Events.Count == 9 && planned.Events[^1] == first.Boss!.EventIndex, string.Join(",", planned.Events));
Check("with nothing chosen, the treasure beats the campsite on move 3",
      planned.Events.Contains(5) && !planned.Events.Contains(4), string.Join(",", planned.Events));

var hurtPlan = RoutePlanner.Plan(first, 0, noneChosen, RoutePreferences.Default, 0.3f, nothingBlocked);
Check("a hurt team takes the campsite", hurtPlan.Events.Contains(4), string.Join(",", hurtPlan.Events));

var chosenLeft = new HashSet<int> { 2 };
var picked = RoutePlanner.Plan(first, 0, chosenLeft, RoutePreferences.Default, 1f, nothingBlocked);
Check("a room chosen by hand is taken", picked.Events.Contains(2) && picked.ChosenByHand.Contains(2),
      string.Join(",", picked.Events));

var blockedRight = new HashSet<(int, int)> { (1, 3) };
var detour = RoutePlanner.Plan(first, 0, noneChosen, RoutePreferences.Default, 1f, blockedRight);
Check("an unsafe edge is never taken", !detour.Events.Contains(3), string.Join(",", detour.Events));

var fromMiddle = RoutePlanner.Plan(first, 6, chosenLeft, RoutePreferences.Default, 1f, nothingBlocked);
Check("a choice already behind the run is ignored", fromMiddle.Events.Count == 5 && fromMiddle.Notes.Count == 0,
      string.Join(",", fromMiddle.Events) + " " + string.Join(" | ", fromMiddle.Notes));

// The rotation, against snapshots. Every action is ready unless a test says otherwise, so each check
// is about the priority and nothing else.
BstState Fight(int level = 50, uint combo = 0, float comboTimer = 0f, int tp = 0, int familiarTp = 0,
               uint[]? statuses = null, uint[]? notReady = null, bool inCombat = true, float distance = 1f,
               bool casting = false, bool familiarOut = true, bool leaving = false, bool prePull = false,
               int horns = 2, float otherHornIn = 0f, float hpShare = 1f, float playerHp = 1f,
               float familiarHp = 1f, bool onFamiliar = false, string? unavoidable = null)
{
    var blocked = new HashSet<uint>(notReady ?? []);
    return new BstState(level, combo, comboTimer, tp, familiarTp, new HashSet<uint>(statuses ?? []),
                        id => !blocked.Contains(id), inCombat, true, distance, casting, familiarOut, leaving,
                        prePull, horns, otherHornIn, hpShare, playerHp, familiarHp, onFamiliar, unavoidable);
}

// Everything but the ability under test is on cooldown, so the check is about that ability alone.
uint[] AllBut(params uint[] keep) =>
    new[]
    {
        Bst.FirstBattlehorn, Bst.SecondBattlehorn, Bst.ThirdBattlehorn, Bst.TemperedRelease, Bst.Borrow,
        Bst.AvalancheAxe, Bst.MistralAxe, Bst.SpinningAxe, Bst.GaleAxe, Bst.Trick, Bst.Rally, Bst.RallyingCheer,
        Bst.BeastMode, Bst.PartingBlow, Bst.ShieldCharge, Bst.Challenge, Bst.Snarl,
    }.Where(id => !keep.Contains(id)).ToArray();

var plain = BstOptions.Default;

var opening = BstRotation.Next(Fight(familiarOut: false), plain);
Check("a fight opens by summoning a familiar", opening.Ogcd == Bst.FirstBattlehorn, opening.Why);
Check("and with the first step of the combo", opening.Gcd == Bst.SmashAxe, opening.Why);

var secondStep = BstRotation.Next(Fight(combo: Bst.SmashAxe, comboTimer: 20f), plain);
Check("the combo goes on to Axeblade Bite", secondStep.Gcd == Bst.AxebladeBite, secondStep.Why);

var third = BstRotation.Next(Fight(combo: Bst.AxebladeBite, comboTimer: 20f), plain);
Check("and ends with Shieldsplitter", third.Gcd == Bst.Shieldsplitter, third.Why);

var low = BstRotation.Next(Fight(level: 10, combo: Bst.AxebladeBite, comboTimer: 20f), plain);
Check("below level 12 the combo starts over", low.Gcd == Bst.SmashAxe, low.Why);

var lapsed = BstRotation.Next(Fight(combo: Bst.SmashAxe, comboTimer: 0f), plain);
Check("a lapsed combo starts over", lapsed.Gcd == Bst.SmashAxe, lapsed.Why);

var released = BstRotation.Next(Fight(statuses: [Bst.OneWithNature], notReady: AllBut(Bst.TemperedRelease)), plain);
Check("One with Nature means Tempered Release", released.Ogcd == Bst.TemperedRelease, released.Why);

var withoutNature = BstRotation.Next(Fight(notReady: AllBut(Bst.TemperedRelease)), plain);
Check("no Tempered Release without One with Nature", withoutNature.Ogcd == 0, withoutNature.Why);

(uint Heart, uint Axe)[] pairs =
[
    (Bst.VolantHeart, Bst.AvalancheAxe),
    (Bst.RampantHeart, Bst.MistralAxe),
    (Bst.DurantHeart, Bst.SpinningAxe),
    (Bst.EldritchHeart, Bst.GaleAxe),
];

foreach (var (heart, axe) in pairs)
{
    var paired = BstRotation.Next(Fight(tp: 120, statuses: [heart], notReady: [Bst.TemperedRelease, Bst.Borrow]), plain);
    Check($"heart {heart} is answered with axe {axe}", paired.Ogcd == axe, paired.Why);
}

var tooLow = BstRotation.Next(Fight(level: 10, tp: 120, statuses: [Bst.DurantHeart],
                                    notReady: [Bst.TemperedRelease, Bst.Borrow, Bst.Rally]), plain);
Check("an axe not learned yet is not pressed, and TP is kept",
      tooLow.Ogcd is not (Bst.SpinningAxe or Bst.AvalancheAxe or Bst.MistralAxe or Bst.GaleAxe), tooLow.Why);

var strider = BstRotation.Next(Fight(tp: 250, statuses: [Bst.Sunstrider], notReady: [Bst.TemperedRelease, Bst.Borrow]),
                               plain);
Check("Sunstrider is answered with a Moonstalker skill",
      strider.Ogcd is Bst.MistralAxe or Bst.GaleAxe, strider.Why);

var saving = BstRotation.Next(Fight(tp: 150, notReady: AllBut(Bst.AvalancheAxe, Bst.GaleAxe)), plain);
Check("without a Heart, TP below the threshold is kept", saving.Ogcd == 0, saving.Why);

var spending = BstRotation.Next(Fight(tp: 220, notReady: AllBut(Bst.AvalancheAxe, Bst.GaleAxe)), plain);
Check("without a Heart, TP above the threshold goes into the highest axe", spending.Ogcd == Bst.GaleAxe,
      spending.Why);

var trick = BstRotation.Next(Fight(familiarTp: 100, notReady: AllBut(Bst.Trick)), plain);
Check("familiar TP with no Heart up means Trick", trick.Ogcd == Bst.Trick, trick.Why);

var noTrick = BstRotation.Next(Fight(familiarTp: 100, statuses: [Bst.VolantHeart], notReady: AllBut(Bst.Trick)), plain);
Check("a Heart already up is not overwritten by Trick", noTrick.Ogcd == 0, noTrick.Why);

var wavering = BstRotation.Next(Fight(familiarTp: 100, statuses: [Bst.WaveringHeart], notReady: AllBut(Bst.Trick)),
                                plain);
Check("no Trick while Wavering Heart blocks combos", wavering.Ogcd == 0, wavering.Why);

var soulQuiet = BstRotation.Next(Fight(statuses: [4608], notReady: AllBut(Bst.BeastMode)), plain);
Check("Soul Kinship's Beast Mode waits for a cast", soulQuiet.Ogcd == 0, soulQuiet.Why);

var soulCast = BstRotation.Next(Fight(statuses: [4608], casting: true, notReady: AllBut(Bst.BeastMode)), plain);
Check("and interrupts it", soulCast.Ogcd == Bst.BeastMode, soulCast.Why);

var beastKin = BstRotation.Next(Fight(statuses: [4602], notReady: AllBut(Bst.BeastMode)), plain);
Check("any other Kinship's Beast Mode goes out on cooldown", beastKin.Ogcd == Bst.BeastMode, beastKin.Why);

var bossModCombo = BstRotation.Next(Fight(statuses: [Bst.OneWithNature]), plain with { Combo = false });
Check("while BossMod plays the combo, only resources are pressed",
      bossModCombo.Gcd == 0 && bossModCombo.Ogcd == Bst.TemperedRelease, bossModCombo.Why);

var calm = BstRotation.Next(Fight(inCombat: false, familiarOut: false), plain);
Check("out of combat no ability is pressed", calm.Ogcd == 0, calm.Why);

var far = BstRotation.Next(Fight(distance: 8f, notReady: AllBut(Bst.ShieldCharge)), plain);
Check("out of reach the combo waits and Shield Charge closes in",
      far.Gcd == 0 && far.Ogcd == Bst.ShieldCharge, far.Why);

var noParting = BstRotation.Next(Fight(notReady: AllBut(Bst.PartingBlow)), plain with { UsePartingBlow = false });
Check("Parting Blow stays off when switched off", noParting.Ogcd == 0, noParting.Why);

var parting = BstRotation.Next(Fight(notReady: AllBut(Bst.PartingBlow)), plain);
Check("by default it sends a spent familiar off, as the recording did", parting.Ogcd == Bst.PartingBlow, parting.Why);

var partingEarly = BstRotation.Next(Fight(notReady: AllBut(Bst.PartingBlow, Bst.TemperedRelease),
                                          statuses: [Bst.OneWithNature]), plain);
Check("not while Tempered Release is still to be used", partingEarly.Ogcd == Bst.TemperedRelease, partingEarly.Why);

var lowParting = BstRotation.Next(Fight(level: 20, notReady: AllBut(Bst.PartingBlow)), plain);
Check("not below 30, where a new familiar resets nothing", lowParting.Ogcd == 0, lowParting.Why);

// The recording pressed the next Battlehorn straight after Parting Blow, while the old familiar was
// still on the field.
var nextHorn = BstRotation.Next(Fight(leaving: true, notReady: AllBut(Bst.SecondBattlehorn)), plain);
Check("a familiar sent off is replaced at once", nextHorn.Ogcd == Bst.SecondBattlehorn, nextHorn.Why);

var noBorrowLeaving = BstRotation.Next(Fight(leaving: true, notReady: AllBut(Bst.Borrow)), plain);
Check("nothing is borrowed from a familiar on its way out", noBorrowLeaving.Ogcd == 0, noBorrowLeaving.Why);

var prePull = BstRotation.Next(Fight(familiarOut: false, distance: 12f, prePull: true, horns: 0), plain);
Check("the opener summons the second familiar first, out of reach", prePull.Ogcd == Bst.SecondBattlehorn,
      prePull.Why);

var prePullNoSecond = BstRotation.Next(Fight(familiarOut: false, prePull: true, horns: 0,
                                             notReady: [Bst.SecondBattlehorn]), plain);
Check("or the third, when the second is not ready", prePullNoSecond.Ogcd == Bst.ThirdBattlehorn, prePullNoSecond.Why);

var prePullLow = BstRotation.Next(Fight(level: 5, familiarOut: false, prePull: true, horns: 0), plain);
Check("or the first, below the second's level", prePullLow.Ogcd == Bst.FirstBattlehorn, prePullLow.Why);

var inFightSummon = BstRotation.Next(Fight(familiarOut: false, horns: 2), plain);
Check("in the fight the first familiar is summoned first again", inFightSummon.Ogcd == Bst.FirstBattlehorn,
      inFightSummon.Why);
Check("and nothing is engaged while it is", !prePull.Engage && prePull.Gcd == 0, prePull.Why);

// The opener as it is played: horn II (or III), Borrow from that familiar, then horn I — all before the pull.
var openBorrow = BstRotation.Next(Fight(prePull: true, horns: 1, notReady: [Bst.SecondBattlehorn]), plain);
Check("the opener borrows from the familiar summoned first", openBorrow.Ogcd == Bst.Borrow && !openBorrow.Engage,
      openBorrow.Why);

var openSecond = BstRotation.Next(Fight(prePull: true, horns: 1, statuses: [4602],
                                        notReady: [Bst.SecondBattlehorn, Bst.Borrow]), plain);
Check("then summons the first familiar", openSecond.Ogcd == Bst.FirstBattlehorn && !openSecond.Engage,
      openSecond.Why);

var openDone = BstRotation.Next(Fight(prePull: true, horns: 2, statuses: [4602], distance: 1f,
                                      notReady: [Bst.FirstBattlehorn, Bst.SecondBattlehorn, Bst.ThirdBattlehorn,
                                                 Bst.Borrow, Bst.TemperedRelease, Bst.Trick, Bst.Rally]), plain);
Check("and only then goes in", openDone.Engage, openDone.Why);

var openLow = BstRotation.Next(Fight(level: 15, prePull: true, horns: 1, notReady: [Bst.FirstBattlehorn]), plain);
Check("below Borrow's level the second horn follows at once", openLow.Ogcd == Bst.SecondBattlehorn, openLow.Why);

var noThirdEarly = BstRotation.Next(Fight(horns: 2, statuses: [4602],
                                          notReady: AllBut(Bst.ThirdBattlehorn)), plain);
Check("no third horn while two familiars are working", noThirdEarly.Ogcd != Bst.ThirdBattlehorn, noThirdEarly.Why);

// Parting Blow for the last familiar of a cycle.
var lastWaits = BstRotation.Next(Fight(otherHornIn: 25f, notReady: AllBut(Bst.PartingBlow)), plain);
Check("the last familiar stays while no horn is near ready", lastWaits.Ogcd == 0, lastWaits.Why);

var lastGoes = BstRotation.Next(Fight(otherHornIn: 8f, notReady: AllBut(Bst.PartingBlow)), plain);
Check("it goes once the first horn is ready within ten seconds", lastGoes.Ogcd == Bst.PartingBlow, lastGoes.Why);

var finisher = BstRotation.Next(Fight(otherHornIn: float.PositiveInfinity, hpShare: 0.05f,
                                      notReady: AllBut(Bst.PartingBlow)), plain);
Check("or when the blow finishes the target", finisher.Ogcd == Bst.PartingBlow, finisher.Why);

// Who takes the hits: Snarl hands them to the familiar, Challenge takes them back. They share a recast.
uint[] duties = AllBut(Bst.Snarl, Bst.Challenge);
var buster = BstRotation.Next(Fight(unavoidable: "Deadly Thrust", notReady: duties), plain);
Check("a hit no position avoids goes to the familiar", buster.Ogcd == Bst.Snarl, buster.Why);

var busterFirst = BstRotation.Next(Fight(unavoidable: "Deadly Thrust", statuses: [Bst.OneWithNature],
                                         notReady: AllBut(Bst.Snarl, Bst.TemperedRelease)), plain);
Check("and before Tempered Release", busterFirst.Ogcd == Bst.Snarl, busterFirst.Why);

var keepCover = BstRotation.Next(Fight(unavoidable: "Arcane Blast", familiarHp: 0.2f, statuses: [Bst.Covered],
                                       notReady: duties), plain);
Check("a covering familiar keeps covering through the hit, low or not", keepCover.Ogcd == 0, keepCover.Why);

var playerLow = BstRotation.Next(Fight(playerHp: 0.4f, notReady: duties), plain);
Check("low HP hands the hits to the familiar", playerLow.Ogcd == Bst.Snarl, playerLow.Why);

var bothLow = BstRotation.Next(Fight(playerHp: 0.4f, familiarHp: 0.2f, onFamiliar: true, notReady: duties), plain);
Check("not to a familiar that is low itself, and nobody challenges", bothLow.Ogcd == 0, bothLow.Why);

var familiarLow = BstRotation.Next(Fight(familiarHp: 0.3f, statuses: [Bst.Covered], notReady: duties), plain);
Check("a low familiar that covers gives the hits back", familiarLow.Ogcd == Bst.Challenge, familiarLow.Why);

var familiarHit = BstRotation.Next(Fight(familiarHp: 0.3f, onFamiliar: true, notReady: duties), plain);
Check("so does a low familiar the target turned to", familiarHit.Ogcd == Bst.Challenge, familiarHit.Why);

var healthyHit = BstRotation.Next(Fight(onFamiliar: true, notReady: duties), plain);
Check("a healthy familiar under attack is left to it", healthyHit.Ogcd == 0, healthyHit.Why);

var playerTanks = BstRotation.Next(Fight(onFamiliar: true, notReady: duties), plain with { Tank = DutyTank.Player });
Check("unless you are the one who tanks", playerTanks.Ogcd == Bst.Challenge, playerTanks.Why);

var familiarTanks = BstRotation.Next(Fight(notReady: duties), plain with { Tank = DutyTank.Familiar });
Check("or the familiar is, by Snarl", familiarTanks.Ogcd == Bst.Snarl, familiarTanks.Why);

var noFamiliar = BstRotation.Next(Fight(familiarOut: false, unavoidable: "Deadly Thrust",
                                        notReady: AllBut(Bst.Snarl, Bst.Challenge)), plain);
Check("no Snarl without a familiar", noFamiliar.Ogcd != Bst.Snarl, noFamiliar.Why);

var dutyPrePull = BstRotation.Next(Fight(prePull: true, unavoidable: "Deadly Thrust", notReady: duties), plain);
Check("nothing that draws the target before the pull", dutyPrePull.Ogcd == 0, dutyPrePull.Why);

var dutyOff = BstRotation.Next(Fight(unavoidable: "Deadly Thrust", notReady: duties), plain with { DutyActions = false });
Check("and nothing with the duty actions off", dutyOff.Ogcd == 0, dutyOff.Why);

// What cannot be dodged, by the shapes the recording's casts have in the Action sheet.
Check("Deadly Thrust on you cannot be dodged", IncomingHits.Unavoidable(1, 0, false, true));
Check("a single-target hit on someone else is not yours", !IncomingHits.Unavoidable(1, 0, false, false));
Check("Void Flare Star, radius 100, cannot be dodged", IncomingHits.Unavoidable(2, 100, false, false));
Check("Allfire, radius 40, cannot be dodged", IncomingHits.Unavoidable(2, 40, false, false));
Check("Void Blizzard III, radius 5, can", !IncomingHits.Unavoidable(2, 5, false, false));
Check("Venom Web, placed radius 9, can", !IncomingHits.Unavoidable(2, 9, true, false));
Check("Bedrock Uplift's ring can", !IncomingHits.Unavoidable(10, 24, false, false));
Check("Void Aero II's line can", !IncomingHits.Unavoidable(12, 60, false, true));

// The arenas, by where the recordings' fights put the player.
Check("the Banemite spawn is on the arena at (120, -420)",
      CrucibleArena.CentreNear(new System.Numerics.Vector2(120f, -404.1f)) == new System.Numerics.Vector2(120f, -420f));
Check("the Piscodemon spawn is on the arena at (120, 0)",
      CrucibleArena.CentreNear(new System.Numerics.Vector2(120f, 11.8f)) == new System.Numerics.Vector2(120f, 0f));
Check("the boss spawn is on the arena at (520, -420)",
      CrucibleArena.CentreNear(new System.Numerics.Vector2(520f, -404.05f)) == new System.Numerics.Vector2(520f, -420f));
Check("the board is on no arena", CrucibleArena.CentreNear(new System.Numerics.Vector2(-700f, -33f)) == null);
// Dodging by the sheet's shapes, against what the recordings cast.
Check("Bedrock Uplift's first ring has a hole of 6", MathF.Abs(CastShapes.DonutInner("gl_sircle_1005bf", 12) - 6f) < 0.01f);
Check("its last ring has a hole of 18", MathF.Abs(CastShapes.DonutInner("gl_sircle_4836_o0v", 24) - 18f) < 0.01f);
Check("Blood Rain has a hole of 8", MathF.Abs(CastShapes.DonutInner("gl_sircle_4008ah1", 40) - 8f) < 0.01f);
Check("a 120 degree fan is 60 degrees each side",
      MathF.Abs(CastShapes.ConeHalfAngle("gl_fan120_1bf") - (MathF.PI / 3f)) < 0.001f);
Check("a hit on one target is not on the ground",
      CastShapes.Shape(1, 0, 0, "", 1f, Vector2.Zero, 0f, 1f, "x") == null);

var cone = CastShapes.Shape(13, 60, 0, "gl_fan090_1bf", 0f, Vector2.Zero, 0f, 3f, "cone")!;
Check("a cone facing +z holds a point ahead", cone.Contains(new Vector2(0f, 10f)));
Check("but not one behind", !cone.Contains(new Vector2(0f, -10f)));
var line = CastShapes.Shape(12, 60, 8, "", 0f, Vector2.Zero, MathF.PI / 2f, 3f, "line")!;
Check("a line facing +x holds a point ahead within its width", line.Contains(new Vector2(30f, 3.5f)));
Check("but not beside it", !line.Contains(new Vector2(30f, 5f)));

// Bedrock Uplift as the third test saw it: a circle and three rings around the Banemite, cast together.
var arena = new Vector2(120f, -420f);
var mite = new Vector2(120f, -424f);
List<Zone> Uplift(float elapsed)
{
    var zones = new List<Zone>();
    void Add(Zone? zone) { if (zone != null && zone.ActivatesIn > 0f) zones.Add(zone); }
    Add(CastShapes.Shape(2, 6, 0, "", 0f, mite, 0f, 4.7f - elapsed, "Bedrock Uplift"));
    Add(CastShapes.Shape(10, 12, 0, "gl_sircle_1005bf", 0f, mite, 0f, 6.7f - elapsed, "Bedrock Uplift"));
    Add(CastShapes.Shape(10, 18, 0, "gl_sircle_3020bf", 0f, mite, 0f, 8.7f - elapsed, "Bedrock Uplift"));
    Add(CastShapes.Shape(10, 24, 0, "gl_sircle_4836_o0v", 0f, mite, 0f, 10.7f - elapsed, "Bedrock Uplift"));
    return zones;
}

var standing = new Vector2(120f, -426f);
var early = Dodger.Plan(standing, mite, 3f, Uplift(0f), arena, 18f)!;
var earlyDistance = Vector2.Distance(early.Point, mite);
Check("first the player steps just out of the circle", early.Safe && earlyDistance > 6f && earlyDistance < 9f,
      $"{earlyDistance:0.0} y from the Banemite");
Check("and stays in the arena", Vector2.Distance(early.Point, arena) <= 18f);

var afterCircle = Dodger.Plan(early.Point, mite, 3f, Uplift(4.8f), arena, 18f)!;
Check("once the circle has hit, back into its middle",
      afterCircle.Safe && Vector2.Distance(afterCircle.Point, mite) < 6f,
      $"{Vector2.Distance(afterCircle.Point, mite):0.0} y from the Banemite");

var later = Dodger.Plan(afterCircle.Point, mite, 3f, Uplift(6.8f), arena, 18f)!;
Check("and it stays there for the outer rings", later.Safe && Vector2.Distance(later.Point, mite) < 6f,
      later.Why);

var edgeMite = new Vector2(129.9f, -416.6f);
var edge = Dodger.Plan(new Vector2(131f, -414f), edgeMite, 3f,
                       [CastShapes.Shape(2, 6, 0, "", 0f, edgeMite, 0f, 4.7f, "Bedrock Uplift")!], arena, 18f)!;
Check("near the edge the step out stays inside", Vector2.Distance(edge.Point, arena) <= 18f,
      $"{Vector2.Distance(edge.Point, arena):0.0} y from the middle");

var outside = Dodger.Plan(new Vector2(139f, -411f), null, 3f, [], arena, 18f);
Check("outside the safe circle with nothing cast, back in",
      outside != null && Vector2.Distance(outside.Point, arena) < 18f);
Check("inside with nothing cast, nothing to do", Dodger.Plan(standing, mite, 3f, [], arena, 18f) == null);

var clear = Dodger.Plan(new Vector2(120f, -410f), mite, 3f, Uplift(0f), arena, 18f)!;
Check("already clear of what hits first: stay put", clear.Point == new Vector2(120f, -410f), clear.Why);

// The First Master's Board, as recorded.
var morbol = new Vector2(120f, -420.9f);
var breath = CastShapes.Shape(13, 50, 0, "gl_fan090_1bf", 5f, morbol, 0f, 4.7f, "Extremely Bad Breath")!;
Check("standing inside the Morbol counts as in its breath", breath.Contains(new Vector2(120f, -421.5f)));
var breathPlan = Dodger.Plan(new Vector2(120f, -421.5f), morbol, 8f, [breath], arena, 18f)!;
Check("so the player steps out of it, behind the Morbol", breathPlan.Safe && !breath.Contains(breathPlan.Point),
      $"to {breathPlan.Point}");

// Malady: circles of 6 on a 7-yalm grid around (120, 0), eleven of them at once.
var gargoyleArena = new Vector2(120f, 0f);
var malady = new List<Zone>();
foreach (var (x, z) in new[] { (109.5f, -10.5f), (130.5f, -10.5f), (102.5f, 17.5f), (123.5f, 17.5f), (130.5f, -3.5f),
                               (102.5f, -3.5f), (123.5f, -17.5f), (116.5f, 10.5f), (137.5f, 3.5f), (116.5f, 3.5f) })
    malady.Add(CastShapes.Shape(2, 6, 0, "", 0f, new Vector2(x, z), 0f, 4.7f, "Malady")!);

var maladyPlan = Dodger.Plan(new Vector2(116.5f, 3.5f), new Vector2(120f, -8f), 8f, malady, gargoyleArena, 18f)!;
Check("Malady's grid leaves a free cell to stand in", maladyPlan.Safe, $"to {maladyPlan.Point}");

// Sweeping Evisceration, with the positions of the recording's second one.
var gargoyleCast = new Vector2(116.1f, 4.3f);
var sweepCast = SweepingEvisceration.Zones(gargoyleCast, 0f, 3f, 5f, null);
var stretch = Dodger.Plan(new Vector2(112f, 5f), gargoyleCast, 6f, sweepCast, gargoyleArena, 18f)!;
Check("while it casts, the tether is stretched",
      stretch.Safe && Vector2.Distance(stretch.Point, gargoyleCast) >= SweepingEvisceration.StretchRadius &&
      Vector2.Distance(stretch.Point, gargoyleArena) <= 18f, $"to {stretch.Point}");

var dashed = new Vector2(110.3f, 5f);
var dashFacing = SweepingEvisceration.Facing(dashed - gargoyleCast);
var front = new Vector2(107f, 5.4f);
var afterDash = Dodger.Plan(front, dashed, 6f, SweepingEvisceration.Zones(dashed, dashFacing, 3f, null, 0.2f),
                            gargoyleArena, 18f)!;
var ahead = new Vector2(MathF.Sin(dashFacing), MathF.Cos(dashFacing));
Check("after the dash, behind it", afterDash.Safe && Vector2.Dot(afterDash.Point - dashed, ahead) < 0f,
      $"to {afterDash.Point}");

var afterSwing = Dodger.Plan(afterDash.Point, dashed, 6f,
                             SweepingEvisceration.Zones(dashed, dashFacing, 3f, null, 2.1f), gargoyleArena, 18f)!;
Check("after the first swing, back through to its front",
      afterSwing.Safe && Vector2.Dot(afterSwing.Point - dashed, ahead) > 0f, $"to {afterSwing.Point}");
Check("and nothing is left to dodge once both have swung",
      SweepingEvisceration.Zones(dashed, dashFacing, 3f, null, 4.2f).Count == 0);

// The Treant's Sludge, and Borgny's Toxic Breath.
var treant = new Vector2(120f, -433f);
var sludge = new Zone(ZoneKind.Circle, treant, 0f, GroundHazards.Radius(2010106)!.Value, 0f, "ground hazard", Lasting: true);
Check("outside the Sludge with nothing cast, nothing to do",
      Dodger.Plan(new Vector2(120f, -422f), treant, 11f, [sludge], arena, 18f) == null);
var outOfSludge = Dodger.Plan(new Vector2(116.2f, -426f), treant, 10f, [sludge], arena, 18f)!;
Check("standing in it, out of it", outOfSludge.Safe && Vector2.Distance(outOfSludge.Point, treant) > 8.5f,
      $"to {outOfSludge.Point}");
var breeze = CastShapes.Shape(2, 12, 0, "", 0f, treant, 0f, 4.5f, "Arboreal Storm")!;
var sludgeAndStorm = Dodger.Plan(new Vector2(120f, -422f), treant, 11f, [sludge, breeze], arena, 18f)!;
Check("a patch on the ground does not hide a hit to come",
      sludgeAndStorm.Safe && Vector2.Distance(sludgeAndStorm.Point, treant) > 12f, $"to {sludgeAndStorm.Point}");

var borgnyArena = new Vector2(920f, -420f);
Check("Borgny's arena is known", CrucibleArena.CentreNear(new Vector2(920f, -404f)) == borgnyArena);
var breathBefore = ToxicBreath.Zone(borgnyArena, 0f, false, 5f);
var toWall = Dodger.Plan(new Vector2(920f, -421f), borgnyArena, 6f, [breathBefore], borgnyArena, 18f)!;
Check("Toxic Breath: straight behind where Borgny will land, at the south wall",
      MathF.Abs(toWall.Point.X - 920f) < 0.01f && toWall.Point.Y < -439.6f, $"to {toWall.Point}");
Check("Borgny faces the player standing just south, so it leaps north",
      MathF.Abs(MathF.Abs(ToxicBreath.FacingFor(borgnyArena, new Vector2(919.9f, -420.6f))) - MathF.PI) < 0.01f);
var westFacing = ToxicBreath.FacingFor(borgnyArena, new Vector2(904.35f, -413.9f));
Check("and the player 16 yalms west, so it leaps east", MathF.Abs(westFacing + (MathF.PI / 2f)) < 0.01f);
var eastRefuge = Dodger.Plan(new Vector2(904.35f, -413.9f), borgnyArena, 6f,
                             [ToxicBreath.Zone(borgnyArena, westFacing, false, 5f)], borgnyArena, 18f)!;
Check("the refuge is at the east wall", eastRefuge.Point.X > 939f && MathF.Abs(eastRefuge.Point.Y + 420f) < 0.1f,
      $"to {eastRefuge.Point}");
var breathAfter = ToxicBreath.Zone(new Vector2(920f, -439.6f), 0f, true, 1f);
Check("and the cleave is taken to cover the arena in front of it",
      breathAfter.Contains(new Vector2(920f, -429f)) && breathAfter.Contains(new Vector2(930f, -435f)));

// The Morbol's turning breath, and the Corpse Flower's trap.
Check("a breath from 3.14 to -2.36 turns 45 degrees on", MathF.Abs(TurningHits.Step(3.14f, -2.36f) - 0.7832f) < 0.01f);
Check("and from -0.79 to 0 as well", MathF.Abs(TurningHits.Step(-0.79f, 0f) - 0.79f) < 0.01f);
var flower = new Vector2(120f, -422.5f);
var briars = new[] { new Vector2(128.5f, -428.5f), new Vector2(120f, -407f), new Vector2(111.5f, -428.5f) };
var trap = FloralTrap.Zone(flower, new Vector2(118f, -424f), briars, 4.7f)!;
var elsewhere = CastShapes.Shape(2, 6, 0, "", 0f, new Vector2(140f, -440f), 0f, 1f, "elsewhere")!;
var toBriar = Dodger.Plan(new Vector2(118f, -424f), flower, 6f, [elsewhere, trap], arena, 18f)!;
Check("Floral Trap: into the nearest briar, even with another hit sooner", toBriar.Point == briars[2],
      $"to {toBriar.Point}");

// Campsites: the 90% is shared, and what heals past full is lost.
Check("a familiar missing a tenth is not worth half the heal", CampsiteRest.HowMany(0.6f, [0.1f], 2) == 0);
Check("one missing half is", CampsiteRest.HowMany(0.6f, [0.5f, 0.05f], 2) == 1);
Check("two badly hurt are both taken when you are nearly full", CampsiteRest.HowMany(0.1f, [0.6f, 0.5f], 2) == 2);
Check("never more than the campsite takes", CampsiteRest.HowMany(0.1f, [0.6f, 0.5f, 0.5f], 2) <= 2);
Check("the recording's rest heals 45% each", MathF.Abs(CampsiteRest.Healed(0.55f, [0.55f], 1) - 0.9f) < 0.001f);

var noDash = BstRotation.Next(Fight(distance: 10f, notReady: AllBut(Bst.ShieldCharge)) with { MayDash = false }, plain);
Check("no Shield Charge while something is on the ground", noDash.Ogcd != Bst.ShieldCharge, noDash.Why);
var dash = BstRotation.Next(Fight(distance: 10f, notReady: AllBut(Bst.ShieldCharge)), plain);
Check("but otherwise, to close in", dash.Ogcd == Bst.ShieldCharge, dash.Why);

Check("where the Bleeding began is outside the default square",
      CrucibleArena.SquareIsSafe(CrucibleArena.DefaultHalfWidth) && 124.21f - 120f < CrucibleArena.DefaultHalfWidth &&
      -440.06f + 420f < -CrucibleArena.DefaultHalfWidth);

Console.WriteLine();
Console.WriteLine("Interruption, which is what the plugin exists for:");
foreach (var b in beasts.Where(b => b.Inflicts(BeastStatus.Interruption)))
    Console.WriteLine($"   {b.Number,2} {b.Name,-14} {b.ClassificationName,-10} {b.Actions[0].Name} / {b.Actions[1].Name}");

Console.WriteLine();
Console.WriteLine("Traits found across all fifty:");
foreach (var trait in Enum.GetValues<BeastTrait>().Where(t => t != BeastTrait.None))
{
    var have = beasts.Where(b => b.Has(trait)).Select(b => b.Name).ToList();
    Console.WriteLine($"   {trait,-18} {have.Count,2}  {string.Join(", ", have.Take(6))}{(have.Count > 6 ? " …" : "")}");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} FAILED.");
return failures == 0 ? 0 : 1;
