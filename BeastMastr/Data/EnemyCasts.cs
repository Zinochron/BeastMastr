using System.Collections.Generic;
using System.Numerics;
using BeastMastr.Rules;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
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

    /// <summary>The name of a hit on its way that no position avoids, or null.</summary>
    public static string? Unavoidable(IPlayerCharacter player)
    {
        foreach (var caster in Casting(player))
        {
            if (ShapeOf(caster.CastActionId) is not { } shape)
                continue;

            var onPlayer = caster.CastTargetObjectId == player.GameObjectId;
            if (IncomingHits.Unavoidable(shape.CastType, shape.EffectRange, shape.TargetArea, onPlayer))
                return shape.Name;
        }

        return null;
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
