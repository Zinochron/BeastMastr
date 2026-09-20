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

    /// <summary>
    /// How far from the Gargoyle to stand while it casts, to stretch the tether. 14 was not enough (the
    /// user: at least 20); the square arena leaves room for it.
    /// </summary>
    public const float StretchRadius = 20f;

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

/// <summary>Bosses some rules single out, by base id.</summary>
public static class Bosses
{
    /// <summary>Borgny the Venomous, the First Master's Board's boss.</summary>
    public const uint Borgny = 19672;

    /// <summary>
    /// With this many familiars summoned in the fight, Borgny's Parting Blow is kept for finishing it: the
    /// third familiar's blow is not spent on Borgny unless it kills it before the add phase.
    /// </summary>
    public const int BorgnyKeepsBlowFromHorn = 3;
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

    /// <summary>The cast, 2.7 s as recorded.</summary>
    public const float CastTime = 2.7f;

    /// <summary>
    /// Borgny turns for up to 0.65 s after the cast starts (sixteen breaths in four recordings). Its facing is
    /// trusted once it has held this long…
    /// </summary>
    public const float SettleFor = 0.3f;

    /// <summary>…or once the cast has gone on this long.</summary>
    public const float TrustAfter = 1f;

    /// <summary>
    /// How far past Borgny to head. The wall stops the walk before it; the aim only has to lie beyond
    /// Borgny, straight back.
    /// </summary>
    public const float PastBy = 6f;

    /// <summary>
    /// Closer than this to Borgny as the cast begins, the player gives no direction to turn to — on
    /// 2026-09-17 01:30 the player stood half a yalm from it, Borgny kept the way it had walked in, and
    /// leapt towards its back.
    /// </summary>
    public const float TurnsToPlayerBeyond = 3f;

    /// <summary>A facing along the nearer axis.</summary>
    public static float Snap(float facing) => MathF.Round(facing / (MathF.PI / 2f)) * (MathF.PI / 2f);

    /// <summary>The way Borgny will face: towards the player, along the nearer axis.</summary>
    public static float FacingFor(Vector2 borgny, Vector2 player)
    {
        var towards = player - borgny;
        if (towards.LengthSquared() < 0.0001f)
            return 0f;

        return Snap(MathF.Atan2(towards.X, towards.Y));
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
/// - 2012932, "Magitek Armor": the tornadoes. Four of them rise, three seconds apart, where Borgny's Toxic
///   Vomit (48809, a circle of 6 on the player) landed, and stay until the next Toxic Vomit. One rose
///   under the player at 01:32:18 and the player died.
/// </summary>
public static class GroundHazards
{
    public const uint PoisonCloud = 19674;

    public static float? Radius(uint baseId) => baseId switch
    {
        2010106 => 9.5f,
        2012932 => 6.5f,
        PoisonCloud => 6.5f,
        _ => null,
    };

    /// <summary>
    /// How far ahead a drifting patch is followed. The Poison Clouds rise eight at a time where Fuming Vomit
    /// landed, stay for about 2.3 seconds, then drift outwards along the eight compass ways at about 2.1
    /// yalms a second until they reach the arena's edge (recording of 2026-09-17 01:49). A hit came 4.6–5.6
    /// yalms from a cloud's worked-out position, so the radius is 6.5 once the drift is followed.
    /// </summary>
    public const float DriftAhead = 1.5f;

    /// <summary>A patch where it is and where it drifts to in <see cref="DriftAhead"/> seconds, as one zone.</summary>
    public static Zone Drifting(Vector2 position, Vector2 velocity, float radius)
    {
        var travel = velocity * DriftAhead;
        if (travel.Length() < 0.3f)
            return new Zone(ZoneKind.Circle, position, 0f, radius, 0f, "ground hazard", Lasting: true);

        return new Zone(ZoneKind.Capsule, position, MathF.Atan2(travel.X, travel.Y), travel.Length(), 0f,
                        "drifting hazard", HalfWidth: radius, Lasting: true);
    }
}

/// <summary>
/// The Treant's Rustling Breeze, two ways (First Master's Board):
/// - 48776, with one helper casting 48778: a 90-degree cone of 60 ahead. The sides are safe. On
///   2026-09-17 01:48 the player stood 49 degrees off its front and was not hit.
/// - 48777, with helpers casting 48779 and 48780: two 150-degree cones. The player stood 76 degrees off the
///   front on both 01:48 and 02:01 and was hit both times, so the cones point to the sides and the middle
///   in front is safe, as the user plays it.
/// The helpers all read facing 0, and the Treant turns to 0 during the cast; the sheet gives the cones'
/// width but not their turn, which is added here.
/// </summary>
public static class RustlingBreeze
{
    public static float? Turn(uint actionId) => actionId switch
    {
        48779 => MathF.PI / 2f,
        48780 => -MathF.PI / 2f,
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

    /// <summary>
    /// How long the briar is held once the trap has gone off. The flower turns to the player and Devour (a
    /// cone of 8 in front of it) comes about five seconds after the trap: on 2026-09-17 19:15 the trap
    /// ended at 21.0, the player walked straight back, and was Devoured 5 yalms in front of it at 25.96.
    /// </summary>
    public const float DevourAfterTrap = 6.5f;

    /// <summary>The briar patch to stand in: the nearest to the player.</summary>
    /// <param name="waitingOutDevour">The trap has gone off; the briar is held until Devour has too.</param>
    public static Zone? Zone(Vector2 flower, Vector2 player, IEnumerable<Vector2> patches, float castLeft,
                             bool waitingOutDevour = false)
    {
        if (waitingOutDevour)
        {
            var near = Nearest(player, patches);
            return near is { } patch
                       ? new Zone(ZoneKind.Circle, flower, 0f, 9f, castLeft, Name + " (in the briar until Devour)",
                                  Refuge: patch)
                       : null;
        }

        return Nearest(player, patches) is { } refuge
                   ? new Zone(ZoneKind.Circle, flower, 0f, 80f, castLeft, Name + " (into the briar)", Refuge: refuge)
                   : null;
    }

    private static Vector2? Nearest(Vector2 player, IEnumerable<Vector2> patches)
    {
        Vector2? best = null;
        foreach (var patch in patches)
        {
            if (best == null || Vector2.Distance(patch, player) < Vector2.Distance(best.Value, player))
                best = patch;
        }

        return best;
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

/// <summary>
/// Borgny's Toxic Vomit (48809): a circle of 6 on the player, after which four tornadoes rise where it landed
/// and stay. They are best left at the arena's edge, away from Borgny and from where the fight goes on.
/// </summary>
public static class ToxicVomit
{
    public const uint Cast = 48809;
    public const string Name = "Toxic Vomit";

    /// <summary>How far out from the middle to leave the tornadoes: inside the safe circle, clear of the middle.</summary>
    public const float EdgeRadius = 15f;

    /// <summary>
    /// The vomit lands this long after the cast ends: at 01:49 the cast ended at 29.5 and the hit came at
    /// 31.8. Shield Charge in between took the player back to Borgny.
    /// </summary>
    public const float LandsAfterCast = 2.5f;

    /// <summary>
    /// After it lands, a tornado rises where the player stood, every three seconds, four in all (01:49:32,
    /// 34.1, 37.2, 40.6; at 22:00 until 10.3 s after). A player standing still gets all four underfoot; one
    /// on the move leaves them behind.
    /// </summary>
    public const float ChaseFor = 11f;

    /// <summary>How far round the ring each step of the chase aims, in radians.</summary>
    public const float ChaseStep = 0.7f;

    /// <summary>
    /// The next spot while the tornadoes follow: on the ring at <see cref="EdgeRadius"/>, a step on round in
    /// <paramref name="turn"/>'s sense (+1 or −1), avoiding patches — shorter or longer steps, or the other
    /// way round, if the first is covered.
    /// </summary>
    public static Vector2 ChasePoint(Vector2 centre, Vector2 player, int turn, IReadOnlyList<Zone> hazards)
    {
        var offset = player - centre;
        var angle = offset.LengthSquared() > 0.01f ? MathF.Atan2(offset.X, offset.Y) : 0f;
        Vector2 At(float a) => centre + (new Vector2(MathF.Sin(a), MathF.Cos(a)) * EdgeRadius);

        foreach (var sense in new[] { turn, -turn })
        {
            foreach (var scale in new[] { 1f, 0.6f, 1.5f, 2f })
            {
                var point = At(angle + (sense * ChaseStep * scale));
                if (Dodger.Hits(hazards, point, Dodger.Margin) == 0)
                    return point;
            }
        }

        return At(angle + (turn * ChaseStep));
    }

    /// <summary>The way round the ring that leads away from Borgny.</summary>
    public static int ChaseTurn(Vector2 centre, Vector2 borgny, Vector2 player)
    {
        var offset = player - centre;
        var angle = MathF.Atan2(offset.X, offset.Y);
        var ahead = centre + (new Vector2(MathF.Sin(angle + ChaseStep), MathF.Cos(angle + ChaseStep)) * EdgeRadius);
        var back = centre + (new Vector2(MathF.Sin(angle - ChaseStep), MathF.Cos(angle - ChaseStep)) * EdgeRadius);
        return Vector2.Distance(ahead, borgny) >= Vector2.Distance(back, borgny) ? 1 : -1;
    }

    /// <summary>The spot to run to while the tornadoes follow, as a refuge.</summary>
    public static Zone Chase(Vector2 point, float left) =>
        new(ZoneKind.Circle, point, 0f, 0f, left, Name + " (keep moving, tornadoes follow)", Refuge: point);

    /// <summary>How many tornadoes follow a Toxic Vomit: four, three seconds apart.</summary>
    public const int Drops = 4;

    /// <summary>The tornadoes' base id: event objects named "Magitek Armor".</summary>
    public const uint Tornado = 2012932;

    /// <summary>How far past Borgny's hitbox the drops are laid: in melee reach, to keep hitting it.</summary>
    public const float DropPastHitbox = 2.5f;

    /// <summary>
    /// Where to lay the four tornadoes, in order, so the fight goes on (the user: "around the boss, so
    /// uptime is kept — about a T"). A T on Borgny: the bar left and right of it, the stem out on one
    /// side in two steps. The side across from the stem stays clear, and the player fights from there.
    /// Of the eight ways the T can point, the one that stays inside the arena and off the patches wins.
    /// </summary>
    public static List<Vector2> TSpots(Vector2 centre, Vector2 borgny, float hitbox, float arenaRadius,
                                       IReadOnlyList<Zone> hazards)
    {
        var reach = MathF.Max(4f, hitbox + DropPastHitbox);
        List<Vector2>? best = null;
        var bestScore = float.MaxValue;

        for (var k = 0; k < 8; k++)
        {
            var angle = k * MathF.PI / 4f;
            var stem = new Vector2(MathF.Sin(angle), MathF.Cos(angle));
            var bar = new Vector2(stem.Y, -stem.X);
            List<Vector2> spots = [borgny + (bar * reach), borgny - (bar * reach), borgny + (stem * reach),
                                   borgny + (stem * reach * 2f)];
            var free = borgny - (stem * reach);

            var score = 0f;
            foreach (var spot in new List<Vector2>(spots) { free })
            {
                score += MathF.Max(0f, Vector2.Distance(spot, centre) - (arenaRadius - 1f)) * 100f;
                score += Dodger.Hits(hazards, spot, Dodger.Margin) * 10f;
            }

            // The free side towards the middle, so the fight is not pinned to the wall.
            score += Vector2.Distance(free, centre) * 0.1f;

            if (score < bestScore)
            {
                bestScore = score;
                best = spots;
            }
        }

        return best!;
    }

    /// <summary>The spot to stand on for the next drop, as a refuge.</summary>
    public static Zone Drop(Vector2 spot, int index, float left) =>
        new(ZoneKind.Circle, spot, 0f, 0f, left, $"{Name} (tornado {index + 1} of {Drops}, in a T round Borgny)",
            Refuge: spot);

    /// <param name="castLeft">Seconds of the cast left; below 0 once it has ended and the vomit is on its way.</param>
    /// <param name="hazards">Patches on the ground the spot at the edge keeps clear of.</param>
    public static Zone Zone(Vector2 centre, Vector2 borgny, Vector2 player, float castLeft,
                            IReadOnlyList<Zone>? hazards = null)
    {
        return new Zone(ZoneKind.Circle, player, 0f, 6f, MathF.Max(0f, castLeft + LandsAfterCast),
                        Name + " (to the edge, away from Borgny)",
                        Refuge: EdgeBait.Spot(centre, borgny, player, hazards ?? []));
    }
}

/// <summary>
/// What Borgny drops where the player stands is best dropped at the arena's edge (the user): Toxic Vomit's
/// tornadoes, and Wriggling Phlegm (48817 on Borgny; its helper places 48819, a circle of 6, on the player
/// 5.3 s in, and a Toxic Mass rises there — 19:30:38 on 2026-09-17, right under the player). The spot is on
/// a ring inside the safe circle, on the far side from Borgny, and clear of patches already down.
/// </summary>
public static class EdgeBait
{
    public const uint PhlegmCast = 48817;
    public const uint PhlegmPlaced = 48819;
    public const string PhlegmName = "Wriggling Phlegm";

    public const float Radius = ToxicVomit.EdgeRadius;

    /// <summary>How far either way round the ring a covered spot is swapped for, in radians.</summary>
    private static readonly float[] Turns = [0f, 0.4f, -0.4f, 0.8f, -0.8f, 1.2f, -1.2f, 1.6f, -1.6f];

    /// <param name="across">
    /// Something to drop across from, such as the tornadoes already laid: the add that rises from Wriggling
    /// Phlegm walks to them and bursts, so the further it has to walk, the better (the user).
    /// </param>
    public static Vector2 Spot(Vector2 centre, Vector2 source, Vector2 player, IReadOnlyList<Zone> hazards,
                               IReadOnlyList<Vector2>? across = null)
    {
        var away = player - source;
        if (across is { Count: > 0 })
        {
            var middle = Vector2.Zero;
            foreach (var spot in across)
                middle += spot;

            away = centre - (middle / across.Count);
        }

        if (away.LengthSquared() < 0.01f)
            away = player - source;
        if (away.LengthSquared() < 0.01f)
            away = player - centre;
        if (away.LengthSquared() < 0.01f)
            away = new Vector2(0f, 1f);

        var angle = MathF.Atan2(away.X, away.Y);
        Vector2 At(float a) => centre + (new Vector2(MathF.Sin(a), MathF.Cos(a)) * Radius);

        foreach (var turn in Turns)
        {
            var spot = At(angle + turn);
            if (Dodger.Hits(hazards, spot, Dodger.Margin) == 0)
                return spot;
        }

        return At(angle);
    }

    /// <summary>The spot to carry a drop to, as a refuge until it is placed.</summary>
    public static Zone Zone(Vector2 spot, float placedIn, string name) =>
        new(ZoneKind.Circle, spot, 0f, 0f, placedIn, name + " (to the edge)", Refuge: spot);
}

/// <summary>
/// The Strix (the First Master's Board's first fight): after Plummet it leaves three puddles on three of
/// the four spots (110|130, −410|−430), then casts On the Properties of Quakes (48657, the whole arena).
/// One puddle lifts the player off the ground and so out of the quake; the other two protect against later
/// mechanics but stop the player attacking (the user). The puddles' event objects are 2004354, 2015456 and
/// 2015457, shuffled over the spots from fight to fight. Which one levitates is not in the data: 2004354
/// uses a shared battle effect (<c>btl/shared/…b0483</c>), the other two effects of this board, so it is
/// tried first, and what the player is given on stepping in is learned.
/// </summary>
public static class StrixPuddles
{
    public const uint Strix = 19638;
    public const uint Quakes = 48657;
    public const string Name = "Levitation puddle";

    public static readonly uint[] Puddles = [2004354, 2015456, 2015457];

    /// <summary>The puddle to stand in: the one learned to levitate, else the first not learned to be wrong.</summary>
    public static uint? Levitating(uint learned, ICollection<uint> wrong)
    {
        if (learned != 0)
            return learned;

        foreach (var puddle in Puddles)
        {
            if (!wrong.Contains(puddle))
                return puddle;
        }

        return null;
    }

    public static Zone Zone(Vector2 puddle, float left) =>
        new(ZoneKind.Circle, puddle, 0f, 0f, left, Name + " (float over the quake)", Refuge: puddle);

    /// <summary>A status that floats the player, by its name.</summary>
    public static bool Floats(string statusName) =>
        statusName.Contains("Levitat", System.StringComparison.OrdinalIgnoreCase) ||
        statusName.Contains("Float", System.StringComparison.OrdinalIgnoreCase) ||
        statusName.Contains("Airborne", System.StringComparison.OrdinalIgnoreCase);
}
