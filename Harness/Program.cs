using Lumina;
using Lumina.Data;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;

var game = new GameData(@"U:\SteamLibrary\steamapps\common\Final Fantasy XIV\game\sqpack",
                        new LuminaOptions { DefaultExcelLanguage = Language.English });
ExcelSheet<RawRow> S(string n) => game.Excel.GetSheet<RawRow>(null, n);
static string Cell(RawRow r, int c) => r.Columns[c].Type == ExcelColumnDataType.String
    ? r.ReadStringColumn(c).ExtractText() : r.ReadColumn(c)?.ToString() ?? "";

void Row(string sheet, uint id, int maxCols = 20)
{
    try
    {
        var s = S(sheet);
        if (!s.TryGetRow(id, out var r)) { Console.WriteLine($"  {sheet}#{id}: no row"); return; }
        var parts = new List<string>();
        for (var c = 0; c < Math.Min(r.Columns.Count, maxCols); c++)
        {
            var v = Cell(r, c);
            if (!string.IsNullOrWhiteSpace(v) && v != "0" && v != "False") parts.Add($"{c}={v}");
        }
        Console.WriteLine($"  {sheet}#{id}: " + string.Join(" ", parts));
    }
    catch (Exception e) { Console.WriteLine($"  {sheet}#{id}: {e.GetType().Name}"); }
}

Console.WriteLine("=== What is XBMPet col5? (45187 shared by Cu Sith/diremite/wespe/puk; 48643 squirrel) ===");
foreach (var id in new uint[] { 45187, 45188, 48643, 49696 })
{
    Row("Action", id, 12);
    Row("Status", id, 8);
    Row("ActionTimeline", id, 6);
    Console.WriteLine();
}

Console.WriteLine("=== What is XBMPet col2? (squirrel=35, pugil=45, coblyn=43, opo-opo=18, dodo=5) ===");
foreach (var id in new uint[] { 35, 45, 43, 18, 5 })
{
    Row("TerritoryType", id, 8);
    Row("PlaceName", id, 3);
    Console.WriteLine();
}

Console.WriteLine("=== XBMPet col1 (kin class) distinct values, with a member each ===");
var seen = new Dictionary<uint, string>();
foreach (var r in S("XBMPet"))
{
    if (r.RowId == 0) continue;
    var k = Convert.ToUInt32(r.ReadColumn(1));
    if (seen.ContainsKey(k)) continue;
    var pet = S("Pet"); pet.TryGetRow(Convert.ToUInt32(r.ReadColumn(0)), out var p);
    seen[k] = Cell(p, 4) is { Length: > 0 } t ? t : Cell(p, 0);
}
foreach (var kv in seen.OrderBy(k => k.Key)) Console.WriteLine($"  c1={kv.Key} e.g. {kv.Value}");

Console.WriteLine();
Console.WriteLine("=== XBMPet col3 and col7 distribution ===");
foreach (var c in new[] { 3, 7 })
    Console.WriteLine($"  col{c}: " + string.Join(" ", S("XBMPet").Where(r => r.RowId != 0)
        .Select(r => Convert.ToUInt32(r.ReadColumn(c))).GroupBy(v => v).OrderBy(g => g.Key)
        .Select(g => $"{g.Key}x{g.Count()}")));
