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
    /// The least advanced beasts, so the ones that need the experience get it.
    ///
    /// Ties break on the bestiary number rather than on whatever order the window happened to list
    /// them in, so the same roster produces the same team twice — a plan that shuffles under you is
    /// worse than one that is merely arguable.
    /// </summary>
    public static IReadOnlyList<Candidate> ForLeveling(IEnumerable<Candidate> available, int teamSize) =>
        available.OrderBy(candidate => candidate.Rank)
                 .ThenBy(candidate => candidate.BeastNumber)
                 .Take(teamSize)
                 .ToList();

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
}
