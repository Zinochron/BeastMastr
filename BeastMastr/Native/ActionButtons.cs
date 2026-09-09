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

    private readonly Configuration configuration;
    private readonly TeamSelector teamSelector;
    private readonly RankPuller rankPuller;
    private readonly Func<bool> nativeUiReady;

    private readonly Dictionary<string, TextButtonNode> buttons = [];

    private int ticksUntilRecheck;
    private bool broken;

    public ActionButtons(Configuration configuration, TeamSelector teamSelector, RankPuller rankPuller,
                         Func<bool> nativeUiReady)
    {
        this.configuration = configuration;
        this.teamSelector = teamSelector;
        this.rankPuller = rankPuller;
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

        var fill = new TextButtonNode
        {
            NodeId = ButtonNodeIdBase,
            Size = new Vector2(150f, 28f),
            // In the gap the window already leaves between the last row of tiles and its footer.
            // Measured from the bottom tile rather than from the window edge: the tile is a child in
            // the same coordinate space, so it stays right whatever the UI scale is doing.
            Position = new Vector2(20f, BelowTheTiles(addon)),
            String = "Fill for levelling",
            IsVisible = true,
            OnClick = teamSelector.RequestFill,
        };

        fill.AttachNode(addon, NodePosition.AsLastChild);
        buttons["fill"] = fill;

        var pull = new TextButtonNode
        {
            NodeId = ButtonNodeIdBase + 1,
            Size = new Vector2(120f, 28f),
            Position = new Vector2(180f, BelowTheTiles(addon)),
            String = "Read ranks",
            IsVisible = true,
            OnClick = rankPuller.Start,
        };

        pull.AttachNode(addon, NodePosition.AsLastChild);
        buttons["pull"] = pull;

        Services.Log.Debug("Team composition button attached.");
    }

    /// <summary>
    /// Just under the last row of tiles, in the window's own coordinates.
    ///
    /// The first attempt offset from the window's height as <c>GetScaledHeight</c> reported it,
    /// which is screen pixels while a child node's position is local — with the UI scaled up the two
    /// disagree by exactly the scale factor, and the button landed that far below. A sibling node's
    /// position needs no conversion at all, so the bottom tile is what it measures from now.
    /// </summary>
    private static float BelowTheTiles(AtkUnitBase* addon)
    {
        var lastTile = addon->GetNodeById(
            (uint)XbmColumns.MonsterNotebook.TileNodeId(XbmColumns.MonsterNotebook.TileCount - 1));

        if (lastTile != null)
            return lastTile->Y + lastTile->Height + 6f;

        var root = addon->RootNode;
        return (root != null && root->Height > 0 ? root->Height : 520f) - 60f;
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
