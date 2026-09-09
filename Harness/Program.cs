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
