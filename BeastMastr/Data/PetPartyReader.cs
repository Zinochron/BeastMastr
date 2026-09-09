using System.Collections.Generic;
using System.Linq;
using BeastMastr.Rules;

namespace BeastMastr.Data;

/// <summary>
/// The roster window, <c>XBMPetParty</c> — and the only place found so far that carries a beast's
/// **progression rank**, which is what "least advanced" has to be measured on.
///
/// The rank in the sheet is a different number: behemoth reads 5 here against a sheet value of 4.
/// </summary>
public static class PetPartyReader
{
    /// <param name="Rank">Progression rank. Zero when the window did not give one.</param>
    /// <param name="Beast">Resolved from the icon, which is what the window hands out.</param>
    public sealed record Slot(int Index, uint IconId, string Name, int Rank, Beast? Beast);

    public static bool IsOpen => AddonReader.IsOpen(XbmColumns.PetParty.Addon);

    public static List<Slot> Read(BeastCatalog catalog)
    {
        var slots = new List<Slot>();

        var addon = AddonReader.Find(XbmColumns.PetParty.Addon);
        if (addon.IsNull)
            return slots;

        var values = addon.AtkValues.ToList();

        for (var block = 0; ; block++)
        {
            var nameIndex = XbmColumns.PetParty.Value(block, XbmColumns.PetParty.NameOffset);
            if (nameIndex >= values.Count)
                break;

            var name = Text(values, nameIndex);
            if (string.IsNullOrWhiteSpace(name))
                break;

            var icon = Number(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.IconOffset)) ?? 0;

            // The rank arrives as a string even though it is a number.
            _ = int.TryParse(Text(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.RankOffset)),
                             out var rank);

            slots.Add(new Slot(block, (uint)icon, name, rank,
                               catalog.ByIcon.GetValueOrDefault((uint)icon)));
        }

        return slots;
    }

    private static string Text(IReadOnlyList<Dalamud.Game.NativeWrapper.AtkValuePtr> values, int index) =>
        index < 0 || index >= values.Count
            ? string.Empty
            : values[index].GetValue()?.ToString() ?? string.Empty;

    /// <summary>Numbers here are UInt; asking only for an Int gets a refusal rather than a conversion.</summary>
    private static int? Number(IReadOnlyList<Dalamud.Game.NativeWrapper.AtkValuePtr> values, int index)
    {
        if (index < 0 || index >= values.Count)
            return null;

        if (values[index].TryGet<int>(out var signed))
            return signed;

        return values[index].TryGet<uint>(out var unsigned) ? (int)unsigned : null;
    }
}
