using System;
using System.Collections.Generic;

namespace BeastMastr.Rules;

/// <summary>
/// How many familiars to rest with at a campsite, so that as little of its healing as possible is lost.
///
/// A campsite heals a pool of 90% that the player and the familiars picked share: alone the player gets
/// 90%, with one familiar each gets 45% (the recording of 2026-09-16 21:56: the player 2789 of 6199, the
/// Opo-opo 1571 of 3492), with two each gets 30%. Whatever a share heals past full is lost. So the count
/// is the one under which the most HP is actually restored, counted as shares of each one's HP.
/// </summary>
public static class CampsiteRest
{
    public const float Pool = 0.9f;

    /// <param name="playerMissing">The player's share of HP missing.</param>
    /// <param name="familiarsMissing">The shares missing of the familiars that may be picked, most hurt first.</param>
    /// <param name="most">How many familiars the campsite takes at most.</param>
    /// <returns>How many of the most hurt to pick. Fewer wins a tie: the player's own share is larger then.</returns>
    public static int HowMany(float playerMissing, IReadOnlyList<float> familiarsMissing, int most)
    {
        var best = 0;
        var bestHealed = Healed(playerMissing, familiarsMissing, 0);

        for (var count = 1; count <= Math.Min(most, familiarsMissing.Count); count++)
        {
            var healed = Healed(playerMissing, familiarsMissing, count);
            if (healed > bestHealed + 0.0001f)
            {
                best = count;
                bestHealed = healed;
            }
        }

        return best;
    }

    /// <summary>The shares actually restored when the player rests with the first <paramref name="count"/> familiars.</summary>
    public static float Healed(float playerMissing, IReadOnlyList<float> familiarsMissing, int count)
    {
        var each = Pool / (1 + count);
        var healed = MathF.Min(playerMissing, each);
        for (var i = 0; i < count; i++)
            healed += MathF.Min(familiarsMissing[i], each);

        return healed;
    }
}
