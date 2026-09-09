using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace BeastMastr.Native;

/// <summary>
/// Writes into the game's own bestiary: a status tag on each tile, and everything that does not
/// match the current filter dimmed.
///
/// The one thing this must get right is that a tile is a slot, not a beast. The overview is a fixed
/// five by five grid of node ids 27..51 and the fifty beasts page through it, so a tag attached once
/// and left alone would follow the slot and describe the wrong beast the moment the page turned.
/// Everything here is recomputed on every update, keyed to whichever beast the window currently has
/// in that slot — which it reports as an icon id in its AtkValues, the only thing it gives out that
/// identifies the beast at all.
/// </summary>
public sealed unsafe class MonsterNotebookDecorator : IDisposable
{
    /// <summary>Well clear of the window's own ids, which run to the low fifties.</summary>
    private const int BadgeNodeIdBase = 0x42450000;

    private const byte DimmedAlpha = 70;

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly BeastFilter filter;

    private readonly Dictionary<int, TextNode> badges = [];

    public MonsterNotebookDecorator(Configuration configuration, BeastCatalog catalog, BeastFilter filter)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.filter = filter;

        var addon = XbmColumns.MonsterNotebook.Addon;
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, OnUpdate);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, addon, OnUpdate);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, addon, OnUpdate);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addon, OnFinalize);
    }

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;

        if (!configuration.DecorateNotebook)
        {
            Detach(addon);
            return;
        }

        Attach(addon);
        Refresh(addon);
    }

    private void OnFinalize(AddonEvent type, AddonArgs args) => Detach((AtkUnitBase*)args.Addon.Address);

    // ---- Attaching --------------------------------------------------------

    private void Attach(AtkUnitBase* addon)
    {
        if (badges.Count > 0 || addon == null)
            return;

        for (var slot = 0; slot < XbmColumns.MonsterNotebook.TileCount; slot++)
        {
            var tile = addon->GetNodeById((uint)XbmColumns.MonsterNotebook.TileNodeId(slot));
            if (tile == null)
                continue;

            var badge = new TextNode
            {
                NodeId = (uint)(BadgeNodeIdBase + slot),
                Size = new Vector2(50f, 14f),
                Position = new Vector2(0f, 36f),
                FontSize = 12,
                TextColor = new Vector4(1f, 0.90f, 0.55f, 1f),
                TextOutlineColor = new Vector4(0f, 0f, 0f, 1f),
                IsVisible = false,
            };

            badge.AttachNode(tile, NodePosition.AsLastChild);
            badges[slot] = badge;
        }

        if (badges.Count > 0)
            Services.Log.Debug($"Bestiary decorated: {badges.Count} tiles.");
    }

    private void Detach(AtkUnitBase* addon)
    {
        // Undim first. The tiles belong to the game and have to be handed back as they were found.
        if (addon != null)
        {
            for (var slot = 0; slot < XbmColumns.MonsterNotebook.TileCount; slot++)
            {
                var tile = addon->GetNodeById((uint)XbmColumns.MonsterNotebook.TileNodeId(slot));
                if (tile != null)
                    tile->Color.A = byte.MaxValue;
            }
        }

        foreach (var badge in badges.Values)
        {
            badge.DetachNode();
            badge.Dispose();
        }

        badges.Clear();
    }

    // ---- Refreshing -------------------------------------------------------

    private void Refresh(AtkUnitBase* addon)
    {
        if (addon == null || badges.Count == 0)
            return;

        var filtering = !filter.IsEmpty;

        for (var slot = 0; slot < XbmColumns.MonsterNotebook.TileCount; slot++)
        {
            if (!badges.TryGetValue(slot, out var badge))
                continue;

            var tile = addon->GetNodeById((uint)XbmColumns.MonsterNotebook.TileNodeId(slot));
            var beast = BeastInSlot(addon, slot);

            if (beast == null)
            {
                badge.IsVisible = false;
                if (tile != null)
                    tile->Color.A = byte.MaxValue;

                continue;
            }

            var tag = Tag(beast);
            badge.String = tag;
            badge.IsVisible = tag.Length > 0;

            if (tile != null)
                tile->Color.A = !filtering || filter.Matches(beast) ? byte.MaxValue : DimmedAlpha;
        }
    }

    /// <summary>
    /// The beast currently rendered in a slot. The window gives out an icon id per slot and nothing
    /// else that names the beast, and that icon is one for one with the sheet's icon column.
    /// </summary>
    private Beast? BeastInSlot(AtkUnitBase* addon, int slot)
    {
        var index = XbmColumns.MonsterNotebook.SlotIconValue(slot);
        if (index < 0 || index >= addon->AtkValuesCount)
            return null;

        var value = addon->AtkValues[index];
        if (value.Type is not (AtkValueType.UInt or AtkValueType.Int))
            return null;

        return catalog.ByIcon.GetValueOrDefault((uint)value.UInt);
    }

    /// <summary>
    /// Two tags at most. Nearly every beast has none or one, the tile is fifty pixels wide, and a
    /// tag that overflows its tile is worse than a tag left off.
    /// </summary>
    private static string Tag(Beast beast)
    {
        var tags = BeastStatusNames.DisplayOrder
                                   .Where(beast.Inflicts)
                                   .Select(status => status.Short())
                                   .ToList();

        foreach (var trait in new[] { BeastTrait.Cleanse, BeastTrait.Dispel })
        {
            if (beast.Has(trait) && trait.Short() is { } tag)
                tags.Add(tag);
        }

        return tags.Count switch
        {
            0 => string.Empty,
            1 => tags[0],
            2 => $"{tags[0]} {tags[1]}",
            _ => $"{tags[0]} +{tags.Count - 1}",
        };
    }

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(OnUpdate, OnFinalize);

        // The window may still be open, in which case its tiles are still dimmed and still carry
        // nodes this plugin owns. Both have to go now or they outlive the plugin.
        Detach(AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon) ? addon : null);
    }
}
