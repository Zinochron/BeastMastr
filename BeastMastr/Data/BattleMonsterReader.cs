using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// One enemy's detail panel — the window that already answers everything the room cards were asked
/// to show, and the only place that answers it.
///
/// It exists only while the cursor rests on an enemy, so it cannot be fetched on demand: it is read
/// while it is up and kept. Everything is in the node tree as plain text; the two AtkValues carry
/// nothing.
/// </summary>
public static unsafe class BattleMonsterReader
{
    /// <param name="Stars">Star count as shown, one to five.</param>
    public sealed record Stat(string Label, int Stars);

    /// <param name="Interruption">What the window says about interrupting it, e.g. "Ineffective".</param>
    /// <param name="Nullified">The window's own hidden note that the current team already covers this.</param>
    public sealed record EnemyAction(
        string Name,
        string Target,
        string DamageType,
        string Interruption,
        string AreaOfEffect,
        string Status,
        bool Nullified);

    public sealed record Enemy(
        string Name,
        string Weakness,
        IReadOnlyList<Stat> Stats,
        IReadOnlyList<EnemyAction> Actions)
    {
        /// <summary>The one line worth putting on a card when there is only room for one.</summary>
        public string Summary
        {
            get
            {
                var statuses = Actions.Where(action => action.Status.Length > 0)
                                      .Select(action => action.Status)
                                      .Distinct();

                var parts = new List<string>();
                if (Weakness.Length > 0)
                    parts.Add($"weak {Weakness}");

                parts.AddRange(statuses);

                if (Actions.Any(action => Interruptible(action.Interruption)))
                    parts.Add("interruptible");

                return string.Join(" · ", parts);
            }
        }

        /// <summary>
        /// The window words this rather than flagging it, and the only value seen so far is
        /// "Ineffective". Anything else is treated as an interrupt being worth bringing, which errs
        /// toward showing the option rather than hiding it.
        /// </summary>
        private static bool Interruptible(string interruption) =>
            interruption.Length > 0
            && !interruption.Contains("ineffective", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOpen => AddonReader.IsOpen(XbmColumns.BattleMonsterDetail.Addon);

    /// <summary>The enemy under the cursor, or null when the panel is not up.</summary>
    public static Enemy? Read()
    {
        if (!AddonReader.TryGet(XbmColumns.BattleMonsterDetail.Addon, out var addon))
            return null;

        var name = Text(addon, XbmColumns.BattleMonsterDetail.NameNodeId);
        if (name.Length == 0)
            return null;

        return new Enemy(name,
                         Clean(Text(addon, XbmColumns.BattleMonsterDetail.WeaknessNodeId)),
                         ReadStats(addon),
                         ReadActions(addon));
    }

    private static List<Stat> ReadStats(AtkUnitBase* addon)
    {
        var stats = new List<Stat>();

        for (var i = 0; i < XbmColumns.BattleMonsterDetail.StatCount; i++)
        {
            var component = Component(addon, (uint)(XbmColumns.BattleMonsterDetail.FirstStatNodeId + i));
            if (component == null)
                continue;

            var label = ChildText(component, XbmColumns.BattleMonsterDetail.StatLabelChildNodeId);
            var stars = ChildText(component, XbmColumns.BattleMonsterDetail.StatStarsChildNodeId);

            if (label.Length > 0)
                stats.Add(new Stat(label, stars.Length));
        }

        return stats;
    }

    /// <summary>
    /// Action blocks are found by shape rather than by a list of node ids: a component that has both
    /// a name and something to say about interrupting it is one. The ids differ with how many
    /// actions an enemy has, and hardcoding the two-action case would silently drop the third.
    /// </summary>
    private static List<EnemyAction> ReadActions(AtkUnitBase* addon)
    {
        var actions = new List<EnemyAction>();

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (uint)node->Type < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component == null)
                continue;

            var name = ChildText(component, XbmColumns.BattleMonsterDetail.ActionNameChildNodeId);
            var interruption = ChildText(component, XbmColumns.BattleMonsterDetail.ActionInterruptionChildNodeId);

            if (name.Length == 0 || interruption.Length == 0)
                continue;

            actions.Add(new EnemyAction(
                            name,
                            ChildText(component, XbmColumns.BattleMonsterDetail.ActionTargetChildNodeId),
                            Clean(ChildText(component, XbmColumns.BattleMonsterDetail.ActionDamageTypeChildNodeId)),
                            interruption,
                            ChildText(component, XbmColumns.BattleMonsterDetail.ActionAreaOfEffectChildNodeId),
                            ChildText(component, XbmColumns.BattleMonsterDetail.ActionStatusChildNodeId),
                            ChildText(component, XbmColumns.BattleMonsterDetail.ActionNullificationChildNodeId).Length > 0));
        }

        return actions;
    }

    private static AtkComponentBase* Component(AtkUnitBase* addon, uint nodeId)
    {
        var node = addon->GetNodeById(nodeId);
        return node == null || (uint)node->Type < 1000 ? null : ((AtkComponentNode*)node)->Component;
    }

    private static string ChildText(AtkComponentBase* component, int nodeId)
    {
        var node = component->GetTextNodeById((uint)nodeId);
        return node == null ? string.Empty : node->NodeText.ToString();
    }

    private static string Text(AtkUnitBase* addon, int nodeId) =>
        AddonReader.TextOf(XbmColumns.BattleMonsterDetail.Addon, (uint)nodeId);

    /// <summary>Start of Unicode's private use area, where the game keeps its own glyphs.</summary>
    private const char FirstPrivateGlyph = (char)0xE000;
    private const char LastPrivateGlyph = (char)0xF8FF;

    /// <summary>
    /// Damage types and weaknesses come with the game's own element glyph in front of them. Those
    /// are private use characters: they render as a box or as nothing at all outside the game's
    /// font, and they survive every string operation until something drops them on purpose. The
    /// beast ranks were read as zero for a whole round of captures because of exactly this.
    /// </summary>
    private static string Clean(string text) =>
        new string(text.Where(c => c < FirstPrivateGlyph || c > LastPrivateGlyph).ToArray()).Trim();
}
