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
