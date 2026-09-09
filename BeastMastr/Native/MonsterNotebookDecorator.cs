using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
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

    /// <summary>True once KamiToolKit is ready. Building a node before that throws in its constructor.</summary>
    private readonly Func<bool> nativeUiReady;

    /// <summary>
    /// Set once anything here throws, and never cleared. A node constructor that fails leaves the
    /// runtime holding a half-built object whose finalizer takes the game down, so retrying every
    /// frame turns one mistake into a crash. One failure disables the feature for the session.
    /// </summary>
    private bool broken;

    /// <summary>Frames between two checks for an already-open window. Half a second is soon enough.</summary>
    private const int RecheckInterval = 30;

    private int ticksUntilRecheck;

    public MonsterNotebookDecorator(Configuration configuration, BeastCatalog catalog, BeastFilter filter,
                                    Func<bool> nativeUiReady)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.filter = filter;
        this.nativeUiReady = nativeUiReady;

        var addon = XbmColumns.MonsterNotebook.Addon;
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, OnUpdate);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, addon, OnUpdate);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, addon, OnUpdate);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addon, OnFinalize);

        // The lifecycle events only fire when the window does something. A window that is already
        // open and sitting still sends nothing, so enabling the plugin — or the setting — while the
        // bestiary is open would leave it undecorated until it was closed and reopened.
        Services.Framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    /// Catches the two cases the addon events cannot: the window was already open and sitting still
    /// when the plugin loaded, and the setting being turned on or off while it is open. Throttled,
    /// because it is a window lookup and nothing here is urgent.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (broken || --ticksUntilRecheck > 0)
            return;

        ticksUntilRecheck = RecheckInterval;

        var open = AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon) ? addon : null;

        if (!configuration.DecorateNotebook || !nativeUiReady())
        {
            // Turned off, or never ready. Either way anything still attached has to come off, and
            // the window will not tell us to do it if nobody is touching it.
            if (badges.Count > 0)
                Detach(open);

            return;
        }

        if (badges.Count == 0 && open != null)
            Decorate(open);
    }

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;

        if (broken)
            return;

        if (!configuration.DecorateNotebook || !nativeUiReady())
        {
            Detach(addon);
            return;
        }

        Decorate(addon);
    }

    /// <summary>
    /// The only place nodes are built or written to, so the circuit breaker only has to sit here.
    /// </summary>
    private void Decorate(AtkUnitBase* addon)
    {
        try
        {
            Attach(addon);
            Refresh(addon);
        }
        catch (Exception ex)
        {
            broken = true;
            Services.Log.Error(ex, "Decorating the bestiary failed; leaving the window alone for the rest of this session.");

            try
            {
                Detach(addon);
            }
            catch (Exception cleanup)
            {
                Services.Log.Error(cleanup, "Could not clean up after the failure either.");
            }
        }
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
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.AddonLifecycle.UnregisterListener(OnUpdate, OnFinalize);

        // The window may still be open, in which case its tiles are still dimmed and still carry
        // nodes this plugin owns. Both have to go now or they outlive the plugin.
        Detach(AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon) ? addon : null);
    }
}
