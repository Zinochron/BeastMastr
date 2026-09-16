using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastMastr.Rules;

/// <summary>
/// The Gargoyle's Sweeping Evisceration (First Master's Board), as the player plays it: while the
/// Gargoyle casts, stretch its tether; it dashes at you about a second after the cast; get behind it;
/// after its first swing, go back through it, since the second swing is behind it.
///
/// Its hits are not cast, so they cannot be read like the others. The timings are from the recording of
/// 2026-09-16 22:25. The dash takes 0.45 seconds. The swings come 1.96 and 4.0 seconds after it ends, and
/// the player was hit at both. Action 48718, a 60-yalm cone with no omen, is taken for a half-circle.
/// </summary>
public static class SweepingEvisceration
{
    public const uint Cast = 48717;
    public const string Name = "Sweeping Evisceration";

    /// <summary>How far from the Gargoyle to stand while it casts, to stretch the tether.</summary>
    public const float StretchRadius = 14f;

    /// <summary>The dash, after the cast ends; used when no dash is seen.</summary>
    public const float DashAfterCast = 1.1f;

    public const float FirstSwing = 1.96f;
    public const float SecondSwing = 4.0f;

    /// <summary>Each swing covers the half of the arena in front of — then behind — the Gargoyle.</summary>
    public const float SwingHalfAngle = MathF.PI / 2f;

    /// <param name="castLeft">Seconds of the cast left, or null once it has ended.</param>
    /// <param name="sinceDash">Seconds since the dash ended, or null until it has.</param>
    /// <param name="facing">The dash's direction, as a game rotation.</param>
    public static List<Zone> Zones(Vector2 gargoyle, float facing, float hitbox, float? castLeft, float? sinceDash)
    {
        var zones = new List<Zone>();

        if (sinceDash is not { } since)
        {
            var dueIn = castLeft is { } left ? left + DashAfterCast : 0.3f;
            zones.Add(new Zone(ZoneKind.Circle, gargoyle, facing, StretchRadius, dueIn, Name + " (stretch the tether)"));
            return zones;
        }

        var apex = MathF.Max(hitbox, CastShapes.MinimumApex);
        if (since < FirstSwing)
        {
            zones.Add(new Zone(ZoneKind.Cone, gargoyle, facing, 60f, FirstSwing - since, Name + " (in front)",
                               HalfAngle: SwingHalfAngle, Apex: apex));
        }

        if (since < SecondSwing)
        {
            zones.Add(new Zone(ZoneKind.Cone, gargoyle, facing + MathF.PI, 60f, SecondSwing - since,
                               Name + " (behind)", HalfAngle: SwingHalfAngle, Apex: apex));
        }

        return zones;
    }

    /// <summary>The game's rotation for a direction on the ground: 0 faces +z.</summary>
    public static float Facing(Vector2 direction) => MathF.Atan2(direction.X, direction.Y);
}

/// <summary>
/// Borgny the Venomous (the First Master's Board's boss): Toxic Breath, then a leap back to the wall and a
/// cleave over everything in front. The one safe spot is right behind Borgny, against the wall, as the
/// player describes it. In the recording of 2026-09-16 22:34 Borgny leapt from (920, −420) to the south
/// wall at z −439.6. The cleave hit 2.8 and 2.9 seconds after the cast (48807, 3 s) ended; its hit, 48808,
/// has no omen to read.
/// </summary>
public static class ToxicBreath
{
    public const uint Cast = 48807;
    public const string Name = "Toxic Breath";

    /// <summary>How far back Borgny leaps, from where it cast.</summary>
    public const float LeapDistance = 19.6f;

    public const float CleaveAfterCast = 2.8f;

    /// <summary>A leap is seen once Borgny is this far from where it cast.</summary>
    public const float LeapSeenAt = 5f;

    /// <summary>
    /// How far past Borgny to head. The wall stops the walk before it; the aim only has to lie beyond
    /// Borgny, straight back.
    /// </summary>
    public const float PastBy = 6f;

    /// <param name="facing">Borgny's facing as it cast; it leaps backwards and keeps it.</param>
    /// <param name="landed">Borgny's position is already the landing.</param>
    public static Zone Zone(Vector2 borgny, float facing, bool landed, float untilCleave)
    {
        var back = -new Vector2(MathF.Sin(facing), MathF.Cos(facing));
        var landing = landed ? borgny : borgny + (back * LeapDistance);
        return new Zone(ZoneKind.Cone, landing, facing, 60f, untilCleave, Name + " (behind Borgny, at the wall)",
                        HalfAngle: MathF.PI * 0.9f, Refuge: landing + (back * PastBy));
    }
}

/// <summary>
/// What stays on the ground and hurts, by base id, with the radius it hurts in. Both from the First
/// Master's Board:
/// - 2010106, an event object under the Treant: Sludge (3071) set in 8.0 yalms from its middle. The player
///   stood there after walking in to 2.5 yalms of the Treant's middle, and died four seconds later.
/// - 19674, Poison Cloud: left by Borgny's Fuming Vomit, placed circles of 6.
/// </summary>
public static class GroundHazards
{
    public static float? Radius(uint baseId) => baseId switch
    {
        2010106 => 8.5f,
        19674 => 6.5f,
        _ => null,
    };
}
