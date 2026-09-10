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
    /// <param name="CallSlot">0, 1 or 2 while this beast is called into a fight; otherwise null.</param>
    /// <param name="Beast">Resolved from the icon, which is what the window hands out.</param>
    public sealed record Slot(int Index, uint IconId, string Name, int Rank, int? CallSlot, Beast? Beast)
    {
        public bool IsCalled => CallSlot is not null;
    }

    public static bool IsOpen => AddonReader.IsOpen(XbmColumns.PetParty.Addon);

    /// <summary>
    /// The window is up and putting a run's team together, rather than calling a fight's familiars.
    /// The window's own mode number decides it, not its prompt: the prompt is a localised sentence.
    /// Read through the pointer because this is asked every frame, and the managed view of the
    /// window's values copies all twelve hundred of them.
    /// </summary>
    public static unsafe bool IsTeamComposition
    {
        get
        {
            if (!IsOpen || !AddonReader.TryGet(XbmColumns.PetParty.Addon, out var addon))
                return false;

            if (addon->AtkValuesCount <= XbmColumns.PetParty.ModeValue)
                return false;

            var mode = addon->AtkValues[XbmColumns.PetParty.ModeValue];
            return mode.Type is FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt
                                or FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int
                   && mode.UInt == XbmColumns.PetParty.TeamCompositionMode;
        }
    }

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
            var rank = Rank(Text(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.RankOffset)));

            var called = Number(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.CallSlotOffset));
            var callSlot = called is null or XbmColumns.PetParty.NotCalled ? (int?)null : called;

            slots.Add(new Slot(block, (uint)icon, name, rank, callSlot,
                               catalog.ByIcon.GetValueOrDefault((uint)icon)));
        }

        return slots;
    }

    /// <summary>
    /// The rank comes with the game's own icon glyphs in front of the digits — private use
    /// characters that survive ToString and make a plain parse fail, which read every rank as zero
    /// while the capture's text looked like a bare number. Only the digits are kept.
    /// </summary>
    private static int Rank(string text)
    {
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var rank) ? rank : 0;
    }

    /// <summary>
    /// What the window is asking for. The same window fills a run's team and a fight's call slots,
    /// and only its prompt tells them apart.
    /// </summary>
    public static string Prompt()
    {
        var addon = AddonReader.Find(XbmColumns.PetParty.Addon);
        if (addon.IsNull)
            return string.Empty;

        var values = addon.AtkValues.ToList();
        return Text(values, XbmColumns.PetParty.PromptValue);
    }

    /// <summary>
    /// Just the beast numbers and ranks, for the watcher — it needs no catalogue and runs on a
    /// timer, so it should not build one every time it looks.
    /// </summary>
    public static IEnumerable<(uint Number, int Rank)> ReadRanks()
    {
        var addon = AddonReader.Find(XbmColumns.PetParty.Addon);
        if (addon.IsNull)
            yield break;

        var values = addon.AtkValues.ToList();

        for (var block = 0; ; block++)
        {
            var nameIndex = XbmColumns.PetParty.Value(block, XbmColumns.PetParty.NameOffset);
            if (nameIndex >= values.Count || string.IsNullOrWhiteSpace(Text(values, nameIndex)))
                yield break;

            var number = Number(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.SheetRowOffset));
            var rank = Rank(Text(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.RankOffset)));

            if (number is > 0 && rank > 0)
                yield return ((uint)number, rank);
        }
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
