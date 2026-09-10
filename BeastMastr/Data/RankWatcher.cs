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
///
/// Two guards, both learned from getting it wrong. The first reading of fifty beasts produced
/// fifteen at rank 1 in contiguous blocks, which is not a save file — it is a stale panel. The rank
/// lives under a node that is **hidden** while the bestiary is merely being browsed, and a hidden
/// text node still returns whatever it held when it was last shown. So: only read while that panel
/// is visible, and only believe a number that is still the same a moment later.
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

        if (configuration.KnownRanksVersion != Configuration.CurrentRanksVersion)
        {
            var dropped = configuration.KnownRanks.Count;
            configuration.KnownRanks.Clear();
            configuration.KnownRanksVersion = Configuration.CurrentRanksVersion;
            configuration.Save();

            if (dropped > 0)
                Services.Log.Information($"Discarded {dropped} rank(s) read the old way; they will fill in again.");
        }

        Services.Framework.Update += OnUpdate;
    }

    /// <summary>Throw away everything learned, for when the numbers look wrong.</summary>
    public void Forget()
    {
        configuration.KnownRanks.Clear();
        configuration.Save();
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

    /// <summary>Last reading of the detail page, kept to see whether it has settled.</summary>
    private (int Number, int Rank) previous;

    /// <summary>The one beast the bestiary currently has open.</summary>
    private void SampleDetailPage()
    {
        var addon = XbmColumns.MonsterBookDetail.Addon;

        // The panel is hidden while the bestiary is only being browsed, and its text nodes keep the
        // last beast's numbers while it is. Reading them then attributes one beast's rank to
        // another, which is exactly how fifteen beasts ended up sharing a rank.
        if (!AddonReader.IsOpen(addon)
            || !AddonReader.IsNodeVisible(addon, (uint)XbmColumns.MonsterBookDetail.RankPanelNodeId))
        {
            previous = default;
            return;
        }

        var number = Digits(AddonReader.TextOf(addon, XbmColumns.MonsterBookDetail.NumberNodeId));
        var rank = Digits(AddonReader.TextOf(addon, XbmColumns.MonsterBookDetail.RankValueNodeId));

        if (number is <= 0 or > 50 || rank <= 0)
        {
            previous = default;
            return;
        }

        // The number and the rank are separate nodes and do not update in the same instant, so a
        // reading is only believed once it has repeated. Moving the cursor across the grid repaints
        // this panel constantly; without this, most of what is caught is mid-update.
        var reading = (number, rank);
        if (previous != reading)
        {
            previous = reading;
            return;
        }

        Remember((uint)number, rank);
    }

    /// <summary>The ten on the team, which the roster window gives all at once.</summary>
    private void SampleRoster()
    {
        if (!PetPartyReader.IsOpen)
            return;

        // One save for the lot. After a run a whole team's ranks change at once, and saving the
        // config once per beast stalled a frame for 75 ms — Dalamud flagged it as a hitch.
        var changed = false;
        foreach (var slot in PetPartyReader.ReadRanks())
            changed |= Store(slot.Number, slot.Rank);

        if (changed)
            configuration.Save();
    }

    /// <summary>
    /// Record a rank read by something else — the sweep, which reads faster than this watcher's own
    /// timer allows. Returns whether it was new or changed, so the sweep can count what it learned.
    /// </summary>
    public bool Learn(uint beastNumber, int rank)
    {
        if (rank <= 0)
            return false;

        if (configuration.KnownRanks.TryGetValue(beastNumber, out var known) && known == rank)
            return false;

        configuration.KnownRanks[beastNumber] = rank;
        configuration.Save();
        return true;
    }

    private void Remember(uint beastNumber, int rank)
    {
        if (Store(beastNumber, rank))
            configuration.Save();
    }

    /// <summary>Records a rank without saving, for callers that learn several at once and save once.</summary>
    private bool Store(uint beastNumber, int rank)
    {
        if (rank <= 0 || configuration.KnownRanks.TryGetValue(beastNumber, out var known) && known == rank)
            return false;

        configuration.KnownRanks[beastNumber] = rank;
        return true;
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
