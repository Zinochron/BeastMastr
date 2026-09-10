using System;
using System.Collections.Generic;
using BeastMastr.Automation;
using BeastMastr.Data;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using System.Numerics;

namespace BeastMastr.Native;

/// <summary>
/// Puts the plugin's actions into the windows they belong to, as buttons the game drew itself.
///
/// A button is a better home for these than a mode that fires the moment a window opens: you press
/// it when you mean it, pressing it again is how you retry, and nothing happens while you are just
/// looking. The mode switches stay for anyone who wants it to happen on its own.
/// </summary>
public sealed unsafe class ActionButtons : IDisposable
{
    /// <summary>Well clear of the windows' own ids, and clear of the bestiary badges at 0x42450000.</summary>
    private const int ButtonNodeIdBase = 0x42460000;

    private const int RecheckInterval = 30;

    /// <summary>
    /// Sized to the gap the window already leaves. Measured off a capture rather than judged by
    /// eye: the bottom row of tiles ends twenty-five units above the "Beasts Captured" caption, so
    /// a twenty-four high button starting one unit under the tiles fills that gap exactly.
    ///
    /// Two earlier attempts missed for the same underlying reason — a position compared against
    /// something measured in different units. First against the window height as
    /// <c>GetScaledHeight</c> reports it, which is screen pixels while a child's position is local,
    /// so the button landed a whole UI scale factor too low. Then against a tile's own <c>Y</c>,
    /// which is measured from the container the tiles sit in rather than from the window.
    ///
    /// The fix is not better arithmetic, it is not needing any: the button hangs off the same
    /// container as the tiles, so their positions are already in the same units.
    /// </summary>
    private const float ButtonHeight = 24f;

    private const float ButtonWidth = 140f;

    private readonly Configuration configuration;
    private readonly TeamSelector teamSelector;
    private readonly Func<bool> nativeUiReady;

    private readonly Dictionary<string, TextButtonNode> buttons = [];

    private int ticksUntilRecheck;
    private bool broken;

    public ActionButtons(Configuration configuration, TeamSelector teamSelector, Func<bool> nativeUiReady)
    {
        this.configuration = configuration;
        this.teamSelector = teamSelector;
        this.nativeUiReady = nativeUiReady;

        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, XbmColumns.MonsterNotebook.Addon, OnFinalize);
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>
    /// The bestiary is the team composition screen, but only while the roster is open beside it.
    /// Outside that pairing the button would do nothing useful, so it is not offered.
    /// </summary>
    private static bool ComposingTeam =>
        AddonReader.IsOpen(XbmColumns.MonsterNotebook.Addon) && PetPartyReader.IsOpen;

    private void OnUpdate(IFramework framework)
    {
        if (broken || --ticksUntilRecheck > 0)
            return;

        ticksUntilRecheck = RecheckInterval;

        try
        {
            if (!configuration.ShowActionButtons || !nativeUiReady() || !ComposingTeam)
            {
                Detach();
                return;
            }

            Attach();
        }
        catch (Exception ex)
        {
            broken = true;
            Services.Log.Error(ex, "Adding the buttons failed; leaving the windows alone this session.");

            try
            {
                Detach();
            }
            catch (Exception cleanup)
            {
                Services.Log.Error(cleanup, "Could not clean up after that either.");
            }
        }
    }

    private void OnFinalize(AddonEvent type, AddonArgs args) => Detach();

    private void Attach()
    {
        if (buttons.Count > 0)
            return;

        if (!AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon))
            return;

        // The grid is the button's parent, not the window: a position under a tile then needs no
        // conversion, because it is written in the same units the tile's own position is in.
        var lastTile = addon->GetNodeById(
            (uint)XbmColumns.MonsterNotebook.TileNodeId(XbmColumns.MonsterNotebook.TileCount - 1));

        var grid = lastTile == null ? null : lastTile->ParentNode;
        if (grid == null)
            return;

        var fill = new TextButtonNode
        {
            NodeId = ButtonNodeIdBase,
            Size = new Vector2(ButtonWidth, ButtonHeight),
            Position = new Vector2(0f, lastTile->Y + lastTile->Height + 1f),
            String = "Fill for levelling",
            IsVisible = true,
            OnClick = teamSelector.RequestFill,
        };

        fill.AttachNode(grid, NodePosition.AsLastChild);
        buttons["fill"] = fill;

        Services.Log.Debug("Team composition button attached.");
    }

    private void Detach()
    {
        foreach (var button in buttons.Values)
        {
            button.DetachNode();
            button.Dispose();
        }

        buttons.Clear();
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Services.AddonLifecycle.UnregisterListener(OnFinalize);

        Detach();
    }
}
