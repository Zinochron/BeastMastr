using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Rules;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BeastMastr.Data;

/// <summary>
/// What the enemies around are casting, and what those casts do by the <c>Action</c> sheet.
///
/// The Crucible casts its area hits from hidden helpers: battle NPCs of sub kind 11 with base 9020,
/// named after the enemy and never targetable. The enemy you see casts a same-named action without a
/// shape, a few rows from the helper's. Every battle NPC that is not a familiar counts, and a cast
/// without a shape takes the widest shape among its same-named neighbours.
/// </summary>
public static class EnemyCasts
{
    /// <summary>How far to either side of a cast its same-named neighbours are looked for.</summary>
    private const int NeighbourRows = 6;

    private const float SearchRange = 50f;

    /// <param name="Id">The row the shape was read from: the cast's own, or the neighbour that lent it.</param>
    /// <param name="CastType">The <c>CastType</c> column: 1 a single target, 2 a circle, 10 a donut, 12 and 13 lines and cones.</param>
    /// <param name="EffectRange">In yalms.</param>
    public sealed record Shape(uint Id, string Name, byte CastType, int EffectRange, int Width, bool TargetArea);

    private static readonly Dictionary<uint, Shape?> Shapes = [];

    /// <summary>
    /// The name of a hit on its way that is aimed at you alone, or null — what Snarl is for. Hits over
    /// the whole arena are not: Snarl's cover did not take them in the master board recordings (713
    /// damage from On the Properties of Quakes, 650 a breath from the Morbol, all while Covered).
    /// </summary>
    public static string? Unavoidable(IPlayerCharacter player)
    {
        foreach (var caster in Casting(player))
        {
            if (ShapeOf(caster.CastActionId) is not { } shape || caster.CastTargetObjectId != player.GameObjectId)
                continue;

            if (IncomingHits.Unavoidable(shape.CastType, shape.EffectRange, shape.TargetArea, true))
                return shape.Name;
        }

        return null;
    }

    /// <summary>A cast seen starting again and again — the Morbol's breath went off every two seconds for a fifth of one.</summary>
    private sealed class Repeat
    {
        public DateTime StartedAt;
        public float Interval;
        public Zone? Last;
    }

    private static readonly Dictionary<(ulong Caster, uint Action), Repeat> Repeats = [];

    /// <summary>A repeat is only trusted this often; one that has stopped is dropped after this many intervals.</summary>
    private const float LongestRepeat = 6f;

    private const float RepeatLapses = 1.6f;

    /// <summary>
    /// Whether an enemy is casting something that a position can avoid — what BossMod has to be free to
    /// dodge. Hits aimed at you alone and circles over the whole arena are left out: no step helps.
    /// </summary>
    public static bool Dodging(IPlayerCharacter player)
    {
        foreach (var caster in Casting(player))
        {
            if (ShapeOf(caster.CastActionId) is not { } shape || shape.CastType <= IncomingHits.SingleTarget ||
                shape.EffectRange <= 0)
                continue;

            if (!IncomingHits.Unavoidable(shape.CastType, shape.EffectRange, shape.TargetArea,
                                          caster.CastTargetObjectId == player.GameObjectId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Where the casts under way will hit, for dodging. Only the cast's own shape counts here: the
    /// helpers cast the real hits, and the visible enemy's shapeless half is left out. Hits no position
    /// avoids, and ones placed on you — they follow you — are left out too.
    /// </summary>
    public static unsafe List<Zone> Zones(IPlayerCharacter player)
    {
        var zones = new List<Zone>();
        var sheet = Services.Data.GetExcelSheet<LuminaAction>();
        var now = DateTime.Now;
        var casting = new HashSet<(ulong, uint)>();

        foreach (var caster in Casting(player))
        {
            if (caster.CastActionId == ToxicBreath.Cast && !Breaths.ContainsKey(caster.GameObjectId))
            {
                Breaths[caster.GameObjectId] = new Breath
                {
                    CastEnd = now + TimeSpan.FromSeconds(caster.TotalCastTime - caster.CurrentCastTime),
                    From = new Vector2(caster.Position.X, caster.Position.Z),
                    Facing = caster.Rotation,
                };
            }

            if (caster.CastActionId == SweepingEvisceration.Cast)
                Sweeps[caster.GameObjectId] = new Sweep
                {
                    CastEnd = now + TimeSpan.FromSeconds(caster.TotalCastTime - caster.CurrentCastTime),
                };

            if (sheet.GetRowOrDefault(caster.CastActionId) is not { } row)
                continue;

            var target = caster.CastTargetObjectId;
            var onPlayer = target == player.GameObjectId;
            if (row.CastType <= IncomingHits.SingleTarget ||
                IncomingHits.Unavoidable(row.CastType, row.EffectRange, row.TargetArea, onPlayer) ||
                (onPlayer && !row.TargetArea))
                continue;

            var info = &((BattleChara*)caster.Address)->CastInfo;
            var origin = new Vector2(caster.Position.X, caster.Position.Z);
            if (row.TargetArea)
            {
                origin = new Vector2(info->TargetLocation.X, info->TargetLocation.Z);
            }
            else if (target != caster.GameObjectId && Services.Objects.SearchById(target) is { } aimed)
            {
                origin = new Vector2(aimed.Position.X, aimed.Position.Z);
            }

            // The caster's own facing: CastInfo.Rotation read 0 for every cast of the master board
            // recording, which pointed every cone and line north.
            var omen = row.Omen.ValueNullable?.Path.ExtractText() ?? string.Empty;
            var zone = CastShapes.Shape(row.CastType, row.EffectRange, row.XAxisModifier, omen, caster.HitboxRadius,
                                        origin, caster.Rotation, caster.TotalCastTime - caster.CurrentCastTime,
                                        row.Name.ExtractText());
            if (zone == null)
                continue;

            if (zone.Kind == ZoneKind.Donut)
                zone = WithHole(zone, caster);

            zones.Add(zone);
            Remember(caster, zone, now);
            casting.Add((caster.GameObjectId, caster.CastActionId));
        }

        // A repeating hit between its casts: the next one is expected an interval after the last began,
        // where the last one pointed. Short casts like the Morbol's breath leave no time to react to.
        foreach (var (key, repeat) in Repeats.ToList())
        {
            var since = (float)(now - repeat.StartedAt).TotalSeconds;
            if (repeat.Interval <= 0f || since > repeat.Interval * RepeatLapses)
            {
                if (since > LongestRepeat * RepeatLapses)
                    Repeats.Remove(key);

                continue;
            }

            if (!casting.Contains(key) && repeat.Last is { } last)
            {
                zones.Add(last with
                {
                    ActivatesIn = MathF.Max(0.3f, repeat.Interval - since + last.ActivatesIn),
                    Name = last.Name + " (repeating)",
                });
            }
        }

        AddSweeps(zones, now);
        AddBreaths(zones, now);
        AddHazards(zones, player);
        return zones;
    }

    /// <summary>Patches on the ground that hurt while they are there, by <see cref="GroundHazards"/>.</summary>
    private static void AddHazards(List<Zone> zones, IPlayerCharacter player)
    {
        foreach (var obj in Services.Objects)
        {
            if (GroundHazards.Radius(obj.BaseId) is not { } radius ||
                Vector3.Distance(obj.Position, player.Position) > SearchRange)
                continue;

            zones.Add(new Zone(ZoneKind.Circle, new Vector2(obj.Position.X, obj.Position.Z), 0f, radius, 0f,
                               "ground hazard", Lasting: true));
        }
    }

    private sealed class Breath
    {
        public DateTime CastEnd;
        public Vector2 From;
        public float Facing;
    }

    private static readonly Dictionary<ulong, Breath> Breaths = [];

    /// <summary>Borgny's Toxic Breath, followed from its cast through the leap to the cleave.</summary>
    private static void AddBreaths(List<Zone> zones, DateTime now)
    {
        foreach (var (id, breath) in Breaths.ToList())
        {
            var untilCleave = (float)(breath.CastEnd - now).TotalSeconds + ToxicBreath.CleaveAfterCast;
            if (untilCleave < -0.3f || Services.Objects.SearchById(id) is not IBattleChara { IsDead: false } borgny)
            {
                Breaths.Remove(id);
                continue;
            }

            var here = new Vector2(borgny.Position.X, borgny.Position.Z);
            var landed = Vector2.Distance(here, breath.From) > ToxicBreath.LeapSeenAt;
            zones.Add(ToxicBreath.Zone(landed ? here : breath.From, breath.Facing, landed, MathF.Max(0f, untilCleave)));
        }
    }

    /// <summary>The Gargoyle's Sweeping Evisceration, followed from its cast through its dash and two swings.</summary>
    private sealed class Sweep
    {
        public DateTime CastEnd;
        public Vector2? DashFrom;
        public Vector2 LastPosition;
        public bool Dashing;
        public DateTime? DashEnd;
        public float Facing;
    }

    private static readonly Dictionary<ulong, Sweep> Sweeps = [];

    /// <summary>With no dash seen this long after the cast, it is taken to have happened in place.</summary>
    private const float DashWait = 3f;

    private static void AddSweeps(List<Zone> zones, DateTime now)
    {
        foreach (var (id, sweep) in Sweeps.ToList())
        {
            if (Services.Objects.SearchById(id) is not IBattleChara { IsDead: false } gargoyle)
            {
                Sweeps.Remove(id);
                continue;
            }

            var here = new Vector2(gargoyle.Position.X, gargoyle.Position.Z);
            float? castLeft = null;
            float? sinceDash = null;

            if (now < sweep.CastEnd)
            {
                castLeft = (float)(sweep.CastEnd - now).TotalSeconds;
            }
            else if (sweep.DashEnd == null)
            {
                sweep.DashFrom ??= here;
                var moved = Vector2.Distance(here, sweep.DashFrom.Value);

                if (moved > 1f)
                {
                    if (sweep.Dashing && Vector2.Distance(here, sweep.LastPosition) < 0.05f)
                    {
                        sweep.DashEnd = now;
                        sweep.Facing = SweepingEvisceration.Facing(here - sweep.DashFrom.Value);
                        Services.Log.Information($"{SweepingEvisceration.Name}: the dash ended at ({here.X:0.0}, {here.Y:0.0}).");
                    }

                    sweep.Dashing = true;
                }
                else if ((now - sweep.CastEnd).TotalSeconds > DashWait)
                {
                    sweep.DashEnd = sweep.CastEnd + TimeSpan.FromSeconds(SweepingEvisceration.DashAfterCast);
                    sweep.Facing = gargoyle.Rotation;
                }

                sweep.LastPosition = here;
            }

            if (sweep.DashEnd is { } dashEnd)
            {
                sinceDash = (float)(now - dashEnd).TotalSeconds;
                if (sinceDash > SweepingEvisceration.SecondSwing + 0.5f)
                {
                    Sweeps.Remove(id);
                    continue;
                }
            }

            zones.AddRange(SweepingEvisceration.Zones(here, sweep.DashEnd == null ? gargoyle.Rotation : sweep.Facing,
                                                      gargoyle.HitboxRadius, castLeft, sinceDash));
        }
    }

    /// <summary>Holes of donuts whose omen does not give one, by caster and action.</summary>
    private static readonly Dictionary<(ulong, uint), float> Holes = [];

    /// <summary>
    /// A donut without an omen has no hole to read, and would be taken for a full circle. The
    /// Gargoyle's Rippling Evisceration is a circle of 13 and then a ring out to 30 from the same
    /// spot; the ring's hole is the circle it follows. A circle of the same name cast from the same
    /// spot gives the hole, remembered for as long as the ring is cast.
    /// </summary>
    private static Zone WithHole(Zone donut, IBattleChara caster)
    {
        if (donut.Inner > 0f)
            return donut;

        var key = (caster.GameObjectId, caster.CastActionId);
        if (!Holes.TryGetValue(key, out var hole))
        {
            foreach (var other in Casting(Services.Objects.LocalPlayer!))
            {
                if (ShapeOf(other.CastActionId) is not { CastType: IncomingHits.Circle } circle ||
                    circle.Name != donut.Name || circle.EffectRange >= donut.Radius ||
                    Vector2.Distance(new Vector2(other.Position.X, other.Position.Z), donut.Origin) > 1f)
                    continue;

                hole = MathF.Max(hole, circle.EffectRange);
            }

            if (hole <= 0f)
                return donut;

            Holes[key] = hole;
            if (Holes.Count > 64)
                Holes.Clear();
        }

        return donut with { Inner = hole };
    }

    private static void Remember(IBattleChara caster, Zone zone, DateTime now)
    {
        var started = now - TimeSpan.FromSeconds(caster.CurrentCastTime);
        var key = (caster.GameObjectId, caster.CastActionId);

        if (!Repeats.TryGetValue(key, out var repeat))
        {
            Repeats[key] = new Repeat { StartedAt = started, Last = zone with { ActivatesIn = caster.TotalCastTime } };
            return;
        }

        var gap = (float)(started - repeat.StartedAt).TotalSeconds;
        if (gap > 0.3f)
        {
            repeat.Interval = gap <= LongestRepeat ? gap : 0f;
            repeat.StartedAt = started;
        }

        repeat.Last = zone with { ActivatesIn = caster.TotalCastTime };
    }

    /// <summary>Every battle NPC in reach that is casting, familiars left out.</summary>
    public static IEnumerable<IBattleChara> Casting(IPlayerCharacter player)
    {
        foreach (var obj in Services.Objects)
        {
            if (obj is not IBattleChara { IsCasting: true, IsDead: false } caster ||
                caster.ObjectKind != ObjectKind.BattleNpc || caster.SubKind == (byte)BattleNpcSubKind.Pet ||
                caster.OwnerId == player.EntityId || caster.CastActionType != 1)
                continue;

            if (Vector3.Distance(caster.Position, player.Position) <= SearchRange)
                yield return caster;
        }
    }

    /// <summary>
    /// A cast's shape, or that of its widest same-named neighbour when the cast itself has none — the
    /// visible enemy's half of a hit its helper deals.
    /// </summary>
    public static Shape? ShapeOf(uint id)
    {
        if (Shapes.TryGetValue(id, out var known))
            return known;

        var sheet = Services.Data.GetExcelSheet<LuminaAction>();
        Shape? shape = null;

        if (sheet.GetRowOrDefault(id) is { } row)
        {
            shape = Read(row);

            if (shape.EffectRange == 0 && !row.CanTargetHostile)
            {
                for (var other = id - NeighbourRows; other <= id + NeighbourRows; other++)
                {
                    if (other == id || sheet.GetRowOrDefault(other) is not { } near ||
                        near.Name.ExtractText() != shape.Name)
                        continue;

                    var candidate = Read(near);
                    if (!candidate.TargetArea && candidate.EffectRange > shape.EffectRange)
                        shape = candidate;
                }
            }
        }

        Shapes[id] = shape;
        return shape;
    }

    private static Shape Read(LuminaAction row) =>
        new(row.RowId, row.Name.ExtractText(), row.CastType, row.EffectRange, row.XAxisModifier, row.TargetArea);
}
