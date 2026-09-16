using System;
using System.Numerics;

namespace BeastMastr.Rules;

/// <summary>
/// An enemy cast's ground shape from the <c>Action</c> sheet's columns, the way BossMod's
/// <c>AIHintsBuilder.GuessShape</c> reads them (BossMod 7.5.6.5, read from its IL).
/// </summary>
public static class CastShapes
{
    /// <summary>BossMod pads every edge by this much.</summary>
    private const float Pad = 0.030518044f;

    /// <summary>The cone angle BossMod assumes when an omen does not name one.</summary>
    public const float FallbackConeDegrees = 90f;

    /// <param name="castType">The <c>CastType</c> column.</param>
    /// <param name="effectRange">The <c>EffectRange</c> column.</param>
    /// <param name="xAxis">The <c>XAxisModifier</c> column: a line's or cross's full width.</param>
    /// <param name="omenPath">The omen's path, which carries a donut's inner radius and a cone's angle.</param>
    /// <param name="hitbox">The caster's hitbox radius, which some shapes reach past.</param>
    /// <returns>A zone, or null for a shape that is not on the ground (a hit on one target, a charge).</returns>
    public static Zone? Shape(byte castType, int effectRange, int xAxis, string omenPath, float hitbox,
                              Vector2 origin, float rotation, float activatesIn, string name)
    {
        var range = (float)effectRange;
        var halfWidth = (xAxis * 0.5f) + Pad;

        return castType switch
        {
            2 => new Zone(ZoneKind.Circle, origin, rotation, range + Pad, activatesIn, name),
            3 => new Zone(ZoneKind.Cone, origin, rotation, range + hitbox, activatesIn, name,
                          HalfAngle: ConeHalfAngle(omenPath)),
            4 => new Zone(ZoneKind.Rect, origin, rotation, range + hitbox + Pad, activatesIn, name,
                          HalfWidth: halfWidth, Behind: Pad),
            5 => new Zone(ZoneKind.Circle, origin, rotation, range + hitbox + Pad, activatesIn, name),
            10 => new Zone(ZoneKind.Donut, origin, rotation, range + Pad, activatesIn, name,
                           Inner: MathF.Max(0f, DonutInner(omenPath, effectRange) - Pad)),
            11 => new Zone(ZoneKind.Cross, origin, rotation, range + Pad, activatesIn, name, HalfWidth: halfWidth),
            12 => new Zone(ZoneKind.Rect, origin, rotation, range + Pad, activatesIn, name,
                           HalfWidth: halfWidth, Behind: Pad),
            13 => new Zone(ZoneKind.Cone, origin, rotation, range, activatesIn, name,
                           HalfAngle: ConeHalfAngle(omenPath)),
            _ => null,
        };
    }

    private static readonly string[] DonutTags = ["sircle_", "sicle_", "circle_", "circle"];

    /// <summary>
    /// A donut's inner radius from its omen: "gl_sircle_1005bf" draws a ring of 10 with a hole of 5,
    /// scaled to the action's range — Bedrock Uplift's 12-yalm ring has a 6-yalm hole. 0 when the omen
    /// does not say; BossMod then takes the donut for a full circle, and so does this.
    /// </summary>
    public static float DonutInner(string omenPath, int effectRange)
    {
        foreach (var tag in DonutTags)
        {
            var at = omenPath.IndexOf(tag, StringComparison.Ordinal);
            if (at < 0 || at + tag.Length + 4 > omenPath.Length)
                continue;

            if (!int.TryParse(omenPath.AsSpan(at + tag.Length, 2), out var outer) ||
                !int.TryParse(omenPath.AsSpan(at + tag.Length + 2, 2), out var inner) || outer == 0)
                continue;

            return inner * ((float)effectRange / outer);
        }

        return 0f;
    }

    /// <summary>Half a cone's opening from its omen, "gl_fan120_1bf" being 120 degrees, in radians.</summary>
    public static float ConeHalfAngle(string omenPath)
    {
        var degrees = FallbackConeDegrees;
        var at = omenPath.IndexOf("fan", StringComparison.Ordinal);
        if (at >= 0 && at + 6 <= omenPath.Length && int.TryParse(omenPath.AsSpan(at + 3, 3), out var parsed))
            degrees = parsed;

        return degrees * MathF.PI / 360f;
    }
}
