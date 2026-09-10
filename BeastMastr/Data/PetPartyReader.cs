using System.Collections.Generic;
using System.Linq;
using BeastMastr.Rules;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// The team list window, <c>XBMPetParty</c> — and the only place found so far that carries a beast's
/// **progression rank**, which is what "least advanced" has to be measured on.
///
/// The rank in the sheet is a different number: behemoth reads 5 here against a sheet value of 4.
///
/// One window, several jobs: building a run's team, calling a fight's familiars, and picking
/// familiars at shops and campsites. <see cref="Mode"/> says which.
/// </summary>
public static class PetPartyReader
{
    /// <param name="Rank">Progression rank. Zero when the window did not give one.</param>
    /// <param name="CallSlot">0, 1 or 2 while this beast is called into a fight; otherwise null.</param>
    /// <param name="Hp">Current HP, zero when the window did not give one.</param>
    /// <param name="Beast">Resolved from the icon, which is what the window hands out.</param>
    public sealed record Slot(int Index, uint IconId, string Name, int Rank, int? CallSlot, int Hp, int MaxHp,
                              Beast? Beast)
    {
        public bool IsCalled => CallSlot is not null;

        /// <summary>Share of HP left. 1 when the window gave none, so an unknown is never the most hurt.</summary>
        public float HealthShare => MaxHp > 0 ? (float)Hp / MaxHp : 1f;
    }

    public static bool IsOpen => AddonReader.IsOpen(XbmColumns.PetParty.Addon);

    /// <summary>
    /// Which job the window is doing, or null when it is not up. Read through the pointer because
    /// it is asked every frame, and the managed view of the window's values copies all twelve
    /// hundred of them.
    /// </summary>
    public static unsafe uint? Mode()
    {
        if (!IsOpen || !AddonReader.TryGet(XbmColumns.PetParty.Addon, out var addon))
            return null;

        if (addon->AtkValuesCount <= XbmColumns.PetParty.ModeValue)
            return null;

        var mode = addon->AtkValues[XbmColumns.PetParty.ModeValue];
        return mode.Type is AtkValueType.UInt or AtkValueType.Int ? mode.UInt : null;
    }

    /// <summary>Putting a run's team together. The window's own number decides, not its localised prompt.</summary>
    public static bool IsTeamComposition => Mode() == XbmColumns.PetParty.TeamCompositionMode;

    /// <summary>Calling a fight's familiars — and not a shop or a campsite, which are other modes of the same window.</summary>
    public static bool IsFight => Mode() == XbmColumns.PetParty.FightMode;

    public static List<Slot> Read(BeastCatalog catalog)
    {
        var slots = new List<Slot>();

        var addon = AddonReader.Find(XbmColumns.PetParty.Addon);
        if (addon.IsNull)
            return slots;

        var values = addon.AtkValues.ToList();

        // In team composition only as many blocks as the window's own counter says are the team.
        // Past that it keeps old rows with their names still in them, and reading until the first
        // blank name counted those as members — a team emptied by "Remove all" still read as full.
        //
        // Only there, though. The counter was read in team composition and a fight; what it counts
        // at a shop or a campsite is not known, and a counter reading "0/2" there would hide every row.
        var teamComposition = Number(values, XbmColumns.PetParty.ModeValue) == (int)XbmColumns.PetParty.TeamCompositionMode;
        var members = teamComposition
                          ? Fraction(Text(values, XbmColumns.PetParty.TeamCountValue))?.Left ?? int.MaxValue
                          : int.MaxValue;

        for (var block = 0; block < members; block++)
        {
            var nameIndex = XbmColumns.PetParty.Value(block, XbmColumns.PetParty.NameOffset);
            if (nameIndex >= values.Count)
                break;

            var name = Text(values, nameIndex);
            if (string.IsNullOrWhiteSpace(name))
                break;

            var icon = Number(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.IconOffset)) ?? 0;
            var rank = Digits(Text(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.RankOffset)));
            var hp = Fraction(Text(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.HpTextOffset)));

            var called = Number(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.CallSlotOffset));
            var callSlot = called is null or XbmColumns.PetParty.NotCalled ? (int?)null : called;

            slots.Add(new Slot(block, (uint)icon, name, rank, callSlot, hp?.Left ?? 0, hp?.Right ?? 0,
                               catalog.ByIcon.GetValueOrDefault((uint)icon)));
        }

        return slots;
    }

    /// <summary>
    /// The rank comes with the game's own icon glyphs in front of the digits — private use
    /// characters that survive ToString and make a plain parse fail, which read every rank as zero
    /// while the capture's text looked like a bare number. Only the digits are kept.
    /// </summary>
    private static int Digits(string text)
    {
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    /// <summary>
    /// How many beasts the team holds and how many it can, from the window's own "0/14". Null when
    /// the window is not up or does not say.
    /// </summary>
    public static (int Members, int Capacity)? Count()
    {
        var addon = AddonReader.Find(XbmColumns.PetParty.Addon);
        if (addon.IsNull)
            return null;

        var values = addon.AtkValues.ToList();
        return Fraction(Text(values, XbmColumns.PetParty.TeamCountValue)) is { } count
                   ? (count.Left, count.Right)
                   : null;
    }

    /// <summary>
    /// "0/14" or "2943/2943" into its two numbers. Glyphs and spacing around the digits are ignored;
    /// null when there is no slash or nothing after it.
    /// </summary>
    private static (int Left, int Right)? Fraction(string text)
    {
        var slash = text.IndexOf('/');
        if (slash <= 0)
            return null;

        var right = Digits(text[(slash + 1)..]);
        return right > 0 ? (Digits(text[..slash]), right) : null;
    }

    /// <summary>
    /// What the window is asking for. The same window does several jobs, and its prompt says which
    /// in words — for reading, not for deciding: it is localised.
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
    /// The window's header values, counter and prompt on one line, for the log. Each job it does
    /// is told apart by these, and logging them every time the mode changes is how the modes not yet
    /// captured get pinned down without anyone having to take a capture.
    /// </summary>
    public static string Describe()
    {
        var addon = AddonReader.Find(XbmColumns.PetParty.Addon);
        if (addon.IsNull)
            return "not open";

        var values = addon.AtkValues.ToList();
        var header = string.Join(" ", Enumerable.Range(0, 6).Select(index => $"v{index}={Text(values, index)}"));

        return $"{header} count=\"{Text(values, XbmColumns.PetParty.TeamCountValue)}\" " +
               $"capacity={Text(values, XbmColumns.PetParty.TeamCapacityValue)} " +
               $"prompt=\"{Text(values, XbmColumns.PetParty.PromptValue)}\"";
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
            var rank = Digits(Text(values, XbmColumns.PetParty.Value(block, XbmColumns.PetParty.RankOffset)));

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
