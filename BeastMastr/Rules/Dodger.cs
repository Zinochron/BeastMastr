using System;
using System.Collections.Generic;
using System.Numerics;

namespace BeastMastr.Rules;

public enum ZoneKind
{
    Circle,
    Donut,
    Cone,
    Rect,
    Cross,
}

/// <summary>
/// Where an enemy cast will hit, on the ground, as x/z. Built from the <c>Action</c> sheet the way
/// BossMod's automatic hints build it, since the Crucible has no module of its own.
/// </summary>
/// <param name="Rotation">The way a cone, line or cross points: 0 faces +z, as the game has it.</param>
/// <param name="Radius">Outer radius; a line's length ahead; a cross's half-length.</param>
/// <param name="Inner">A donut's safe middle.</param>
/// <param name="HalfWidth">Half a line's or cross's width.</param>
/// <param name="HalfAngle">Half a cone's opening, in radians.</param>
/// <param name="Behind">How far a line reaches behind its origin.</param>
/// <param name="ActivatesIn">Seconds until it hits: the cast's time left.</param>
/// <param name="Apex">
/// Around a cone's origin, everything counts as hit. Standing inside the Morbol, 0.6 yalms behind its
/// middle, was taken for safe from its breath and was not.
/// </param>
/// <param name="Lasting">A patch on the ground that hurts for as long as it is there, not a hit to come.</param>
/// <param name="Refuge">
/// Where to go instead of searching: for a hit whose one safe spot the arena search cannot find — behind
/// Borgny, against the wall, outside the safe circle.
/// </param>
public sealed record Zone(ZoneKind Kind, Vector2 Origin, float Rotation, float Radius, float ActivatesIn, string Name,
                          float Inner = 0f, float HalfWidth = 0f, float HalfAngle = 0f, float Behind = 0f,
                          float Apex = 0f, bool Lasting = false, Vector2? Refuge = null)
{
    public bool Contains(Vector2 point)
    {
        var offset = point - Origin;
        var distance = offset.Length();
        var ahead = new Vector2(MathF.Sin(Rotation), MathF.Cos(Rotation));

        switch (Kind)
        {
            case ZoneKind.Circle:
                return distance <= Radius;

            case ZoneKind.Donut:
                return distance >= Inner && distance <= Radius;

            case ZoneKind.Cone:
                if (distance > Radius)
                    return false;

                if (distance <= MathF.Max(Apex, 0.001f))
                    return true;

                var cos = Vector2.Dot(offset / distance, ahead);
                return MathF.Acos(Math.Clamp(cos, -1f, 1f)) <= HalfAngle;

            case ZoneKind.Rect:
                return InLine(offset, ahead, Radius, Behind);

            case ZoneKind.Cross:
                return InLine(offset, ahead, Radius, Radius) ||
                       InLine(offset, new Vector2(ahead.Y, -ahead.X), Radius, Radius);

            default:
                return false;
        }
    }

    private bool InLine(Vector2 offset, Vector2 ahead, float front, float back)
    {
        var along = Vector2.Dot(offset, ahead);
        var side = MathF.Abs((offset.X * ahead.Y) - (offset.Y * ahead.X));
        return along >= -back && along <= front && side <= HalfWidth;
    }
}

/// <param name="Point">Where to stand.</param>
/// <param name="Safe">False when no spot in the arena avoids everything; the point is then the least bad.</param>
/// <param name="Why">What is being dodged, for the log.</param>
public sealed record DodgePlan(Vector2 Point, bool Safe, string Why);

/// <summary>
/// Where to stand while enemies cast: out of what hits soonest, inside the arena, and as close to where
/// you are and to the target as that allows.
///
/// Only the hits that land soonest are dodged — those within <see cref="Window"/> of the first. That is
/// what gets Bedrock Uplift right: its circle and rings are cast together, and every spot within
/// 24 yalms is in one of them. Dodging all at once runs out of the arena, where Bleeding stacks (the
/// third test died of it). Dodging in turn steps just out of the circle, then back into its middle once
/// it has hit, which is how it was played by hand without a scratch.
/// </summary>
public static class Dodger
{
    /// <summary>Hits landing this soon after the first are dodged together.</summary>
    public const float Window = 1.5f;

    /// <summary>
    /// A spot this close outside a hit's edge still counts as in it: movement is not exact. Kept small:
    /// the Gargoyle's Malady puts circles of 6 on a 7-yalm grid, and the free cells between them are
    /// only a yalm clear.
    /// </summary>
    public const float Margin = 0.5f;

    public const float GridStep = 1f;

    /// <summary>How much a yalm further from the target weighs against a yalm more to walk.</summary>
    public const float TargetWeight = 0.5f;

    /// <param name="reach">Distance to the target that counts as in reach.</param>
    /// <param name="arenaCentre">The arena's middle; the spot stays within <paramref name="arenaRadius"/> of it.</param>
    /// <returns>A plan, or null when nothing needs dodging and you are inside the arena.</returns>
    public static DodgePlan? Plan(Vector2 player, Vector2? target, float reach, IReadOnlyList<Zone> zones,
                                 Vector2 arenaCentre, float arenaRadius)
    {
        var active = Soonest(zones);
        var outside = Vector2.Distance(player, arenaCentre) > arenaRadius;

        foreach (var zone in active)
        {
            if (zone.Refuge is { } refuge)
                return new DodgePlan(refuge, true, zone.Name);
        }

        // Only lasting patches, and none underfoot: nothing to do but not walk into them.
        if (active.Count > 0 && active.TrueForAll(zone => zone.Lasting) && !outside && Hits(active, player, Margin) == 0)
            return null;

        if (active.Count == 0)
        {
            if (!outside)
                return null;

            var back = arenaCentre + (Direction(player - arenaCentre) * (arenaRadius - GridStep));
            return new DodgePlan(back, true, "back inside the arena");
        }

        var names = string.Join(", ", Names(active));
        if (!outside && Hits(active, player, Margin) == 0)
            return new DodgePlan(player, true, $"already clear of {names}");

        DodgePlan? best = null;
        var bestScore = float.MaxValue;
        var bestHits = int.MaxValue;

        for (var x = -arenaRadius; x <= arenaRadius; x += GridStep)
        {
            for (var z = -arenaRadius; z <= arenaRadius; z += GridStep)
            {
                var offset = new Vector2(x, z);
                if (offset.Length() > arenaRadius)
                    continue;

                var point = arenaCentre + offset;
                var hits = Hits(active, point, Margin);
                var score = Vector2.Distance(point, player) +
                            (target is { } t ? TargetWeight * MathF.Max(0f, Vector2.Distance(point, t) - reach) : 0f);

                if (hits < bestHits || (hits == bestHits && score < bestScore))
                {
                    bestHits = hits;
                    bestScore = score;
                    best = new DodgePlan(point, hits == 0, names);
                }
            }
        }

        return best;
    }

    /// <summary>The hits that land within <see cref="Window"/> of the first, every lasting patch, and every refuge.</summary>
    public static List<Zone> Soonest(IReadOnlyList<Zone> zones)
    {
        var first = float.MaxValue;
        foreach (var zone in zones)
        {
            if (!zone.Lasting && zone.Refuge == null)
                first = MathF.Min(first, zone.ActivatesIn);
        }

        var soonest = new List<Zone>();
        foreach (var zone in zones)
        {
            // A refuge is walked to from the moment it is known: the wall behind Borgny and a briar
            // patch can be far.
            if (zone.Lasting || zone.Refuge != null || zone.ActivatesIn <= first + Window)
                soonest.Add(zone);
        }

        return soonest;
    }

    /// <summary>How many of the zones a point is in, or within <paramref name="margin"/> of.</summary>
    public static int Hits(IReadOnlyList<Zone> zones, Vector2 point, float margin)
    {
        var count = 0;
        foreach (var zone in zones)
        {
            if (zone.Contains(point) || Near(zone, point, margin))
                count++;
        }

        return count;
    }

    /// <summary>A point within the margin of a zone's edge, tested by the points around it.</summary>
    private static bool Near(Zone zone, Vector2 point, float margin)
    {
        if (margin <= 0f)
            return false;

        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4f;
            if (zone.Contains(point + (new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * margin)))
                return true;
        }

        return false;
    }

    private static Vector2 Direction(Vector2 offset) =>
        offset.LengthSquared() > 0.0001f ? Vector2.Normalize(offset) : new Vector2(0f, 1f);

    private static IEnumerable<string> Names(List<Zone> zones)
    {
        var seen = new HashSet<string>();
        foreach (var zone in zones)
        {
            if (seen.Add(zone.Name))
                yield return zone.Name;
        }
    }
}
