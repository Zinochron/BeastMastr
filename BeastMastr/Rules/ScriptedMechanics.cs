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
/// Borgny the Venomous (the First Master's Board's boss): Toxic Breath. Borgny walks to the middle, casts,
/// turns to the player, leaps backwards 19.6 yalms to the wall, and cleaves everything in front. The one
/// safe spot is right behind Borgny, against the wall — the wall across from where the player stood.
///
/// Its facing as the cast starts is not the one it leaps from. On 2026-09-17 01:13 it cast facing 0.74
/// and leapt due north: the player stood just south of it. At 01:14 it cast facing −3.12 and leapt due
/// east: the player stood 16 yalms to the west. Both leaps went along an axis, away from the player as
/// they stood when the cast began. The leap follows the cast by about 0.6 s and takes one; the cleave
/// comes 2.8 s after the cast ends (its hit, 48808, has no omen to read).
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

    /// <summary>The way Borgny will face: towards the player, along the nearer axis.</summary>
    public static float FacingFor(Vector2 borgny, Vector2 player)
    {
        var towards = player - borgny;
        if (towards.LengthSquared() < 0.0001f)
            return 0f;

        var angle = MathF.Atan2(towards.X, towards.Y);
        return MathF.Round(angle / (MathF.PI / 2f)) * (MathF.PI / 2f);
    }

    /// <param name="facing">The way Borgny faces as it leaps: it leaps backwards and keeps it.</param>
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
/// - 2010106, an event object under the Treant: Sludge (3071) set in 8.0 yalms from its middle, and at
///   8.4–8.7 walking in on 2026-09-17 00:03 — with the status arriving a moment after the step.
/// - 19674, Poison Cloud: left by Borgny's Fuming Vomit, placed circles of 6. The clouds drift, and on
///   2026-09-17 00:40 two hits of about 850 came 6.4 yalms from one.
/// </summary>
public static class GroundHazards
{
    public static float? Radius(uint baseId) => baseId switch
    {
        2010106 => 9.5f,
        19674 => 7.5f,
        _ => null,
    };
}

/// <summary>
/// The Corpse Flower's Floral Trap (48683, 5 s, a circle of 80 that cannot be dodged): it draws the player
/// in and binds them, and Devour (48685, a cone of 8 in front of the flower) eats them. In the recording of
/// 2026-09-16 22:54 that took 1100 HP to 19. Just before, Sapling Pieces leave briar patches (event object
/// 2015458). Briar (5176) "prevents draw-in and knockback effects", so the way out is to stand in one.
/// </summary>
public static class FloralTrap
{
    public const uint Cast = 48683;
    public const uint BriarPatch = 2015458;
    public const string Name = "Floral Trap";

    /// <summary>The briar patch to stand in: the nearest to the player.</summary>
    public static Zone? Zone(Vector2 flower, Vector2 player, IEnumerable<Vector2> patches, float castLeft)
    {
        Vector2? best = null;
        foreach (var patch in patches)
        {
            if (best == null || Vector2.Distance(patch, player) < Vector2.Distance(best.Value, player))
                best = patch;
        }

        return best is { } refuge
                   ? new Zone(ZoneKind.Circle, flower, 0f, 80f, castLeft, Name + " (into the briar)", Refuge: refuge)
                   : null;
    }
}

/// <summary>
/// A hit that turns as it repeats: the Morbol's Extremely Bad Breath, a 90-degree cone of 50, went off
/// every 2.1 seconds, each 45 degrees on from the last (facings 3.14, −2.36, −1.57, −0.79, 0.00 in the
/// recording of 2026-09-16 22:59). The next one is the last one turned by the last step.
/// </summary>
public static class TurningHits
{
    /// <summary>The step between two facings, in (−π, π].</summary>
    public static float Step(float from, float to)
    {
        var step = (to - from) % (2f * MathF.PI);
        if (step > MathF.PI)
            step -= 2f * MathF.PI;
        else if (step <= -MathF.PI)
            step += 2f * MathF.PI;

        return step;
    }
}
