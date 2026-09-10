using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <param name="Interruption">
/// What the enemy panel says about interrupting this action, in the player's own words — the game
/// writes a sentence here rather than setting a flag, so the sentence is carried through and
/// <see cref="RoomBriefing.Interruptible"/> is the one place that judges it.
/// </param>
/// <param name="Covered">
/// The panel's own note that the team as it stands already nullifies this status. It is the game's
/// answer to "do I need to bring anything for this", so it decides whether the status is listed as
/// something to bring for at all.
/// </param>
public sealed record BriefAction(string Name, string Status, string Interruption, bool Covered);

public sealed record BriefEnemy(string Name, string Weakness, IReadOnlyList<BriefAction> Actions);

/// <summary>
/// What one room asks you to bring, reduced from what the board window said about its enemies.
///
/// Pure: this takes strings the readers already pulled out and gives back the lines a panel draws.
/// The enemy names, statuses and weaknesses arrive in the player's language and are passed through
/// untranslated — writing our own words for them would mean maintaining a table per client.
/// </summary>
public static class RoomBriefing
{
    /// <summary>
    /// The moves worth having on the team for this room, most decisive first.
    ///
    /// Only two are derivable today. Interruption the game states per action; cleansing follows from
    /// a status the enemy applies that the team does not already nullify. Dispel has no marker in
    /// any window found so far, so it is not claimed.
    /// </summary>
    public static IReadOnlyList<string> Needs(IReadOnlyList<BriefEnemy> enemies)
    {
        var needs = new List<string>();

        if (enemies.Any(enemy => enemy.Actions.Any(action => Interruptible(action.Interruption))))
            needs.Add("Interrupt");

        var uncovered = Statuses(enemies, coveredToo: false);
        if (uncovered.Count > 0)
            needs.Add($"Cleanse ({string.Join(", ", uncovered)})");

        return needs;
    }

    /// <summary>Every damage type the room's enemies are weak to, each named once.</summary>
    public static IReadOnlyList<string> Weaknesses(IReadOnlyList<BriefEnemy> enemies) =>
        enemies.Where(enemy => enemy.Weakness.Length > 0)
               .Select(enemy => enemy.Weakness)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>
    /// The statuses the room's enemies inflict, each named once.
    /// </summary>
    /// <param name="coveredToo">
    /// Include the ones the team already nullifies. The briefing wants only the uncovered ones —
    /// those are what is missing — while a full list is what you want when reading the room itself.
    /// </param>
    public static IReadOnlyList<string> Statuses(IReadOnlyList<BriefEnemy> enemies, bool coveredToo = true) =>
        enemies.SelectMany(enemy => enemy.Actions)
               .Where(action => action.Status.Length > 0 && (coveredToo || !action.Covered))
               .Select(action => action.Status)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>
    /// One block per enemy: what it is weak to, then a line per action that does something worth
    /// preparing for. Actions that neither apply a status nor can be interrupted are left out —
    /// they are the ones nothing on the team changes.
    /// </summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<BriefEnemy> enemies)
    {
        var lines = new List<string>();

        foreach (var enemy in enemies)
        {
            lines.Add(enemy.Weakness.Length > 0
                          ? $"{enemy.Name} — weak to {enemy.Weakness}"
                          : enemy.Name);

            foreach (var action in enemy.Actions)
            {
                var notes = new List<string>();

                if (action.Status.Length > 0)
                    notes.Add(action.Covered ? $"{action.Status} (covered)" : action.Status);

                if (Interruptible(action.Interruption))
                    notes.Add("interruptible");

                if (notes.Count > 0)
                    lines.Add($"    {action.Name}: {string.Join(", ", notes)}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Whether an interrupt is worth bringing for an action, from what the panel says about it.
    ///
    /// The game words this rather than flagging it, and the only value seen so far is
    /// "Ineffective". Everything else counts as interruptible, which errs towards offering the
    /// option — a room briefed as needing an interrupt it does not need costs a team slot, while
    /// one that hides a needed interrupt costs the run.
    /// </summary>
    public static bool Interruptible(string interruption) =>
        interruption.Length > 0
        && !interruption.Contains("ineffective", StringComparison.OrdinalIgnoreCase);
}
