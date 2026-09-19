using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <summary>
/// Who goes into the team, and who gets called into a fight. Pure: it decides, it does not click.
/// </summary>
public static class TeamPlanner
{
    /// <param name="Rank">Progression rank. Lower means less advanced, which is what leveling wants.</param>
    public sealed record Candidate(uint BeastNumber, string Name, int Rank);

    /// <summary>
    /// How many beasts a board's team holds. Only the three unlocked so far are known — standard
    /// takes ten, the next two take twelve and fourteen — so anything else falls back to the
    /// smallest, which under-fills rather than trying to pick a beast that has no slot.
    /// </summary>
    public static readonly IReadOnlyList<int> TeamSizes = [10, 12, 14];

    public const int DefaultTeamSize = 10;

    public static int TeamSizeFor(int boardTier) =>
        boardTier >= 0 && boardTier < TeamSizes.Count ? TeamSizes[boardTier] : DefaultTeamSize;

    /// <summary>
    /// How many beasts may be brought along to carry the rest. Three of ten leaves seven levelling,
    /// which is where the point of the exercise stops surviving more.
    /// </summary>
    public const int MaxCarries = 3;

    /// <summary>
    /// The team to level with: the beasts chosen to carry, then the least advanced to fill up.
    ///
    /// Carrying is the whole reason this takes that parameter. A team of nothing but the weakest
    /// beasts levels them slowly or not at all, so a few strong ones are brought to do the work
    /// while the rest collect the experience.
    ///
    /// <paramref name="current"/> is what is on the team already, and it is a **tie-breaker, not a
    /// preference**: among beasts of equal rank the one already there wins. Without that, a rank 1
    /// beast gets swapped for a different rank 1 beast because its number is lower — churn that
    /// levels nobody faster, and that turns a two-change adjustment into a dozen, each one another
    /// chance for the window to refuse.
    ///
    /// The last tie-break is the bestiary number, so the same roster produces the same team twice.
    /// A plan that shuffles under you is worse than one that is merely arguable.
    /// </summary>
    public static IReadOnlyList<Candidate> ForLeveling(IEnumerable<Candidate> available,
                                                       int teamSize,
                                                       IEnumerable<uint>? carry = null,
                                                       IEnumerable<uint>? current = null)
    {
        var pool = available.ToList();
        var wanted = (carry ?? []).Take(MaxCarries).ToHashSet();
        var alreadyThere = (current ?? []).ToHashSet();

        var carried = pool.Where(candidate => wanted.Contains(candidate.BeastNumber))
                          .OrderBy(candidate => candidate.BeastNumber)
                          .Take(teamSize)
                          .ToList();

        var rest = pool.Where(candidate => !carried.Contains(candidate))
                       .OrderBy(candidate => candidate.Rank)
                       .ThenByDescending(candidate => alreadyThere.Contains(candidate.BeastNumber))
                       .ThenBy(candidate => candidate.BeastNumber)
                       .Take(teamSize - carried.Count);

        return carried.Concat(rest).ToList();
    }

    /// <summary>
    /// The same beasts as last time, in the same call order, minus any that are not in the roster
    /// any more. Returns fewer than asked for rather than substituting: a fight you did not choose
    /// the familiars for is worse than one you have to finish choosing yourself.
    /// </summary>
    public static IReadOnlyList<uint> RepeatLast(IEnumerable<Candidate> roster,
                                                 IEnumerable<uint> lastTime,
                                                 int slots)
    {
        var present = roster.Select(candidate => candidate.BeastNumber).ToHashSet();

        return lastTime.Where(present.Contains)
                       .Distinct()
                       .Take(slots)
                       .ToList();
    }

    /// <summary>How many familiars a fight takes: one per Battlehorn.</summary>
    public const int FightSlots = 3;

    /// <summary>
    /// The familiars to call into a fight: the carries that can fight, in the order they were chosen,
    /// then the last fight's in their call order. <paramref name="able"/> is the team without the
    /// familiars that are down.
    ///
    /// A fight is filled up to as many as last time, or as many carries as can come if that is more,
    /// and never past <see cref="FightSlots"/>.
    /// </summary>
    public static IReadOnlyList<uint> ForFight(IEnumerable<Candidate> able, IEnumerable<uint> carries,
                                               IEnumerable<uint> lastTime)
    {
        var present = able.Select(candidate => candidate.BeastNumber).ToHashSet();
        var carried = carries.Where(present.Contains).Distinct().Take(FightSlots).ToList();
        var last = lastTime.Distinct().ToList();
        var slots = Math.Min(FightSlots, Math.Max(last.Count, carried.Count));

        return carried.Concat(last.Where(present.Contains).Where(beast => !carried.Contains(beast)))
                      .Take(slots)
                      .ToList();
    }
}
