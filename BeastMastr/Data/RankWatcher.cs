using System;
using System.Linq;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// Learns each beast's progression rank by watching the bestiary while you use it.
///
/// There is no bulk source. The roster window gives ranks for the ten beasts already on a team, and
/// the bestiary's detail page gives one at a time — for whichever beast is selected, and only while
/// a team is being put together, since the panel holding it is hidden the rest of the time.
///
/// Reading all fifty by driving the window would mean fifty selections under the player's hands to
/// answer a question nobody asked yet. Watching costs nothing and fills in as the bestiary is
/// browsed, so that is what this does. What it has not seen, it does not claim to know.
/// </summary>
public sealed class RankWatcher : IDisposable
{
    /// <summary>Frames between samples. The panel changes when you click, not per frame.</summary>
    private const int Interval = 15;

    private readonly Configuration configuration;
    private int ticks;

    public RankWatcher(Configuration configuration)
    {
        this.configuration = configuration;
        Services.Framework.Update += OnUpdate;
    }

    public int KnownCount => configuration.KnownRanks.Count;

    /// <summary>The rank if it has ever been seen, otherwise null. Never a guess.</summary>
    public int? RankOf(uint beastNumber) =>
        configuration.KnownRanks.TryGetValue(beastNumber, out var rank) ? rank : null;

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        SampleDetailPage();
        SampleRoster();
    }

    /// <summary>The one beast the bestiary currently has open.</summary>
    private void SampleDetailPage()
    {
        var addon = XbmColumns.MonsterBookDetail.Addon;
        if (!AddonReader.IsOpen(addon))
            return;

        var number = Digits(AddonReader.TextOf(addon, XbmColumns.MonsterBookDetail.NumberNodeId));
        var rank = Digits(AddonReader.TextOf(addon, XbmColumns.MonsterBookDetail.RankValueNodeId));

        if (number is > 0 and <= 50 && rank > 0)
            Remember((uint)number, rank);
    }

    /// <summary>The ten on the team, which the roster window gives all at once.</summary>
    private void SampleRoster()
    {
        if (!PetPartyReader.IsOpen)
            return;

        foreach (var slot in PetPartyReader.ReadRanks())
            Remember(slot.Number, slot.Rank);
    }

    private void Remember(uint beastNumber, int rank)
    {
        if (rank <= 0 || configuration.KnownRanks.TryGetValue(beastNumber, out var known) && known == rank)
            return;

        configuration.KnownRanks[beastNumber] = rank;
        configuration.Save();
    }

    /// <summary>
    /// Game strings carry icon glyphs from the private use area in front of their numbers, and a
    /// plain parse refuses the lot while the text still looks like a bare number.
    /// </summary>
    private static int Digits(string text)
    {
        // "9/100" is a fraction, and only the part before the slash is wanted.
        var slash = text.IndexOf('/');
        if (slash >= 0)
            text = text[..slash];

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
