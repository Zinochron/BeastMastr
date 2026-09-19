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
        public float Turn;
        public Zone? Last;
    }

    /// <summary>
    /// By caster and name, not action: the Morbol's breath opens with one action (48673) and repeats as
    /// another (48675).
    /// </summary>
    private static readonly Dictionary<(ulong Caster, string Name), Repeat> Repeats = [];

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
        var casting = new HashSet<(ulong, string)>();

        foreach (var caster in Casting(player))
        {
            if (caster.CastActionId == ToxicBreath.Cast && !Breaths.ContainsKey(caster.GameObjectId))
            {
                var from = new Vector2(caster.Position.X, caster.Position.Z);
                var here = new Vector2(player.Position.X, player.Position.Z);
                var follows = Vector2.Distance(from, here) < ToxicBreath.TurnsToPlayerBeyond;
                var facing = follows ? ToxicBreath.Snap(caster.Rotation) : ToxicBreath.FacingFor(from, here);
                Breaths[caster.GameObjectId] = new Breath
                {
                    CastEnd = now + TimeSpan.FromSeconds(caster.TotalCastTime - caster.CurrentCastTime),
                    From = from,
                    Facing = facing,
                    FollowsBorgny = follows,
                };
                Services.Log.Information($"{ToxicBreath.Name}: Borgny faces {facing:0.00} " +
                                         (follows ? "(its own way; the player stands on it)" : "towards the player") +
                                         "; behind it is the wall to go to.");
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

            if (RustlingBreeze.Turn(caster.CastActionId) is { } turn)
                zone = zone with { Rotation = zone.Rotation + turn };

            if (zone.Kind == ZoneKind.Donut)
                zone = WithHole(zone, caster);

            if (zone.Kind == ZoneKind.Cone)
                zone = zone with { Apex = MathF.Max(zone.Apex, HitboxAt(zone.Origin)) };

            zones.Add(zone);
            Remember(caster, zone, now);
            casting.Add((caster.GameObjectId, zone.Name));
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
                    Rotation = last.Rotation + repeat.Turn,
                    Name = last.Name + " (repeating)",
                });
            }
        }

        AddSweeps(zones, now);
        AddBreaths(zones, now);
        AddHazards(zones, player, now);
        AddTraps(zones, player, now);
        AddVomit(zones, player, now);
        AddPhlegm(zones, player);
        return zones;
    }

    /// <summary>
    /// The largest hitbox standing at a point: a helper casts a cone from the Morbol's middle, and the
    /// Morbol's body around it is part of what the breath hits.
    /// </summary>
    private static float HitboxAt(Vector2 point)
    {
        var largest = 0f;
        foreach (var obj in Services.Objects)
        {
            if (obj.ObjectKind == ObjectKind.BattleNpc && obj.SubKind != (byte)BattleNpcSubKind.Pet &&
                Vector2.Distance(new Vector2(obj.Position.X, obj.Position.Z), point) < 1.5f)
                largest = MathF.Max(largest, obj.HitboxRadius);
        }

        return largest;
    }

    /// <summary>
    /// Toxic Vomit on the player leaves tornadoes where it lands: it is carried to the arena's edge, on the
    /// far side from Borgny, so they rise out of the way.
    /// </summary>
    private static void AddVomit(List<Zone> zones, IPlayerCharacter player, DateTime now)
    {
        var here = new Vector2(player.Position.X, player.Position.Z);
        foreach (var borgny in Casting(player))
        {
            if (borgny.CastActionId != ToxicVomit.Cast || borgny.CastTargetObjectId != player.GameObjectId)
                continue;

            var left = borgny.TotalCastTime - borgny.CurrentCastTime;
            var at = new Vector2(borgny.Position.X, borgny.Position.Z);
            if (Vomit is { } running && running.Borgny == at && running.Spots.Count > 0)
            {
                running.CastEnd = now + TimeSpan.FromSeconds(left);
                continue;
            }

            // The T is laid out once, as the cast starts, and the tornadoes already down are noted so
            // only this vomit's are counted.
            var arenaCentre = CrucibleArena.CentreNear(here) ?? at;
            Vomit = new VomitState
            {
                CastEnd = now + TimeSpan.FromSeconds(left),
                Borgny = at,
                Spots = ToxicVomit.TSpots(arenaCentre, at, borgny.HitboxRadius, CrucibleArena.SafeRadius - 2f,
                                          zones.Where(zone => zone.Lasting).ToList()),
                Before = Tornadoes().ToHashSet(),
            };
            Services.Log.Information($"{ToxicVomit.Name}: laying the tornadoes in a T round Borgny: " +
                                     string.Join(" ", Vomit.Spots.Select(spot => $"{spot.X:0.0}/{spot.Y:0.0}")));
        }

        if (Vomit is not { } vomit)
            return;

        // How many of this vomit's tornadoes are down: the next one goes on the next spot of the T.
        var dropped = Tornadoes().Count(tornado => !vomit.Before.Contains(tornado));
        var sinceEnd = (float)(now - vomit.CastEnd).TotalSeconds;
        if (dropped >= ToxicVomit.Drops || sinceEnd - ToxicVomit.LandsAfterCast > ToxicVomit.ChaseFor)
        {
            Vomit = null;
            return;
        }

        var left2 = MathF.Max(0f, ToxicVomit.LandsAfterCast - sinceEnd);
        zones.Add(ToxicVomit.Drop(vomit.Spots[dropped], dropped, left2));
    }

    /// <summary>The tornadoes Toxic Vomit leaves ("Magitek Armor", 2012932) that are standing now.</summary>
    private static IEnumerable<ulong> Tornadoes() =>
        Services.Objects.Where(obj => obj.BaseId == ToxicVomit.Tornado).Select(obj => obj.GameObjectId);

    /// <summary>
    /// Wriggling Phlegm: carried to the edge while Borgny casts it, until its circle is placed on the
    /// player. From then the circle is dodged like any other.
    /// </summary>
    private static void AddPhlegm(List<Zone> zones, IPlayerCharacter player)
    {
        var casters = Casting(player).ToList();
        if (casters.Any(caster => caster.CastActionId == EdgeBait.PhlegmPlaced))
            return;

        var here = new Vector2(player.Position.X, player.Position.Z);
        foreach (var borgny in casters)
        {
            if (borgny.CastActionId != EdgeBait.PhlegmCast || CrucibleArena.CentreNear(here) is not { } centre)
                continue;

            var spot = EdgeBait.Spot(centre, new Vector2(borgny.Position.X, borgny.Position.Z), here,
                                     zones.Where(zone => zone.Lasting).ToList());
            zones.Add(EdgeBait.Zone(spot, borgny.TotalCastTime - borgny.CurrentCastTime, EdgeBait.PhlegmName));
        }
    }

    private sealed class VomitState
    {
        public DateTime CastEnd;
        public Vector2 Borgny;

        /// <summary>Where the four tornadoes go, in order.</summary>
        public List<Vector2> Spots = [];

        /// <summary>The tornadoes standing as the cast began: an earlier vomit's.</summary>
        public HashSet<ulong> Before = [];
    }

    private static VomitState? Vomit;

    /// <summary>Floral Trap: into the nearest briar patch before it resolves.</summary>
    private static void AddTraps(List<Zone> zones, IPlayerCharacter player, DateTime now)
    {
        var here = new Vector2(player.Position.X, player.Position.Z);
        foreach (var flower in Casting(player))
        {
            if (flower.CastActionId != FloralTrap.Cast)
                continue;

            var left = flower.TotalCastTime - flower.CurrentCastTime;
            Trap = (now + TimeSpan.FromSeconds(left), new Vector2(flower.Position.X, flower.Position.Z));
        }

        if (Trap is not { } trap)
            return;

        // The briar is held until Devour has gone by: walking back to the flower at once was walking into it.
        var sinceEnd = (float)(now - trap.CastEnd).TotalSeconds;
        if (sinceEnd > FloralTrap.DevourAfterTrap)
        {
            Trap = null;
            return;
        }

        var patches = Services.Objects.Where(obj => obj.BaseId == FloralTrap.BriarPatch)
                              .Select(obj => new Vector2(obj.Position.X, obj.Position.Z));
        if (FloralTrap.Zone(trap.Flower, here, patches, MathF.Max(0f, -sinceEnd), sinceEnd > 0f) is { } zone)
            zones.Add(zone);
    }

    /// <summary>The Floral Trap under way or just over: when it ends, and where the flower stands.</summary>
    private static (DateTime CastEnd, Vector2 Flower)? Trap;

    /// <summary>The widest lasting patch centred on a point, or 0: walking in to a target stops outside it.</summary>
    public static float HazardAround(Vector3 point)
    {
        var widest = 0f;
        foreach (var obj in Services.Objects)
        {
            if (GroundHazards.Radius(obj.BaseId) is { } radius &&
                Vector2.Distance(new Vector2(obj.Position.X, obj.Position.Z), new Vector2(point.X, point.Z)) < 2f)
                widest = MathF.Max(widest, radius);
        }

        return widest;
    }

    /// <summary>Patches on the ground that hurt while they are there, by <see cref="GroundHazards"/>.</summary>
    private static void AddHazards(List<Zone> zones, IPlayerCharacter player, DateTime now)
    {
        // Eight clouds rise on one spot, and four tornadoes on another: one zone each is enough.
        var seen = new HashSet<(int, int, int, int, uint)>();
        foreach (var obj in Services.Objects)
        {
            if (GroundHazards.Radius(obj.BaseId) is not { } radius ||
                Vector3.Distance(obj.Position, player.Position) > SearchRange)
                continue;

            var position = new Vector2(obj.Position.X, obj.Position.Z);
            var velocity = obj.BaseId == GroundHazards.PoisonCloud
                               ? DriftOf(obj.GameObjectId, position, now)
                               : Vector2.Zero;

            if (!seen.Add(((int)MathF.Round(position.X * 2f), (int)MathF.Round(position.Y * 2f),
                           (int)MathF.Round(velocity.X), (int)MathF.Round(velocity.Y), obj.BaseId)))
                continue;

            zones.Add(GroundHazards.Drifting(position, velocity, radius));
        }

        if (Drifts.Count > 100)
        {
            foreach (var (id, drift) in Drifts.ToList())
            {
                if (now - drift.SeenAt > TimeSpan.FromSeconds(30))
                    Drifts.Remove(id);
            }
        }
    }

    private sealed class Drift
    {
        public Vector2 Position;
        public DateTime SeenAt;
        public Vector2 Velocity;
    }

    private static readonly Dictionary<ulong, Drift> Drifts = [];

    /// <summary>A cloud's velocity, from where it was when last looked at a tenth of a second or more ago.</summary>
    private static Vector2 DriftOf(ulong id, Vector2 position, DateTime now)
    {
        if (!Drifts.TryGetValue(id, out var drift))
        {
            Drifts[id] = new Drift { Position = position, SeenAt = now };
            return Vector2.Zero;
        }

        var dt = (float)(now - drift.SeenAt).TotalSeconds;
        if (dt < 0.1f)
            return drift.Velocity;

        var measured = dt > 2f ? Vector2.Zero : (position - drift.Position) / dt;
        drift.Velocity = (drift.Velocity * 0.4f) + (measured * 0.6f);
        drift.Position = position;
        drift.SeenAt = now;
        return drift.Velocity;
    }

    private sealed class Breath
    {
        public DateTime CastEnd;
        public Vector2 From;
        public float Facing;

        /// <summary>The player stood too close to turn Borgny: its own facing counts, followed until it leaps.</summary>
        public bool FollowsBorgny;
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

            // Once it leaps, its way is known for certain: straight back. Before that, with the player on
            // top of it, its own facing is followed as it settles.
            if (landed)
                breath.Facing = SweepingEvisceration.Facing(breath.From - here);
            else if (breath.FollowsBorgny)
                breath.Facing = ToxicBreath.Snap(borgny.Rotation);
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
        var key = (caster.GameObjectId, zone.Name);

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
            repeat.Turn = repeat.Last is { } previous ? TurningHits.Step(previous.Rotation, zone.Rotation) : 0f;
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
