using System;
using System.Numerics;

namespace BeastMastr.Rules;

/// <summary>
/// Where the Crucible's fight arenas are, in the run's own zone, and how much of each is safe.
///
/// Every fight of the recordings loaded into one of these, the player put 12 to 16 yalms from the
/// middle. Past about 20.5 yalms from the Banemite arena's middle "Bleeding" set in and stacked; that
/// fight cost 70–80% HP and a familiar. BossMod knows none of this, so its pathfinding is kept to a
/// square around the middle that fits inside the safe circle.
/// </summary>
public static class CrucibleArena
{
    /// <summary>
    /// The arenas' middles, as x/z. (120, −420): Banemite, Ogre, Bone Bishop — enemy helpers stand
    /// there and the Bleeding started 20.5 yalms out. (120, 0): Piscodemon, which jumps back to it.
    /// (520, −420): the boss, its adds placed around it. (520, 0): seen once, taken by symmetry.
    /// (920, −420): Borgny, the First Master's Board's boss — its breath and vomit are cast from there.
    /// </summary>
    public static readonly Vector2[] Centres =
    [
        new(120f, -420f),
        new(120f, 0f),
        new(520f, -420f),
        new(520f, 0f),
        new(920f, -420f),
    ];

    /// <summary>How far from a middle an arena is still recognised — its spawn is at most 16 away.</summary>
    public const float RecogniseWithin = 30f;

    /// <summary>The safe circle's radius, just inside where the Bleeding began.</summary>
    public const float SafeRadius = 20f;

    /// <summary>The largest square half-width that stays inside <see cref="SafeRadius"/>, less a margin.</summary>
    public const float DefaultHalfWidth = 13.5f;

    /// <summary>The middle of the arena around a position, or null off every arena.</summary>
    public static Vector2? CentreNear(Vector2 position)
    {
        Vector2? best = null;
        var bestDistance = RecogniseWithin;

        foreach (var centre in Centres)
        {
            var distance = Vector2.Distance(centre, position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = centre;
            }
        }

        return best;
    }

    /// <summary>A square of this half-width around the middle keeps its corners inside the safe circle.</summary>
    public static bool SquareIsSafe(float halfWidth) => halfWidth * MathF.Sqrt(2f) < SafeRadius;
}
