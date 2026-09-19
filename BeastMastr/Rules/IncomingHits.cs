namespace BeastMastr.Rules;

/// <summary>
/// Which enemy casts no position avoids, judged by the shape the <c>Action</c> sheet gives them.
///
/// BossMod has no module for the Crucible's fights, so it dodges by these same shapes. It cannot dodge
/// a hit aimed at you alone, or a circle wider than the arena. The Crucible casts many of its area hits
/// from hidden helpers, while the enemy you see casts a same-named action with no shape of its own; the
/// caller looks up the helper's shape next to it.
/// </summary>
public static class IncomingHits
{
    /// <summary>The <c>CastType</c> of a hit on a single target.</summary>
    public const byte SingleTarget = 1;

    /// <summary>The <c>CastType</c> of a circle.</summary>
    public const byte Circle = 2;

    /// <summary>A circle at least this wide covers any arena in the recording; those were about 25 yalms across.</summary>
    public const int ArenaWide = 30;

    /// <param name="castType">The action's <c>CastType</c>.</param>
    /// <param name="effectRange">The action's <c>EffectRange</c>, in yalms.</param>
    /// <param name="targetArea">Placed on a spot, rather than cast around the caster or on a target.</param>
    /// <param name="onPlayer">Cast on you.</param>
    public static bool Unavoidable(byte castType, int effectRange, bool targetArea, bool onPlayer) =>
        (onPlayer && castType <= SingleTarget && !targetArea) ||
        (castType == Circle && !targetArea && effectRange >= ArenaWide);
}
