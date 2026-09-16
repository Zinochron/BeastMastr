using System;
using System.Collections.Generic;
using System.Numerics;
using BeastMastr.Data;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace BeastMastr.Native;

/// <summary>
/// The route, drawn onto the game's own board window, and picked there.
///
/// Every room tile gets a small mark — a star for a room picked by hand, an arrow for one the plan
/// takes — and every room on a fork gets a pin to pick it with. The tile itself keeps its own click,
/// which is how a room's enemies are read, so the pin sits in its corner rather than over it.
///
/// The marks hang off the window's root rather than off the tiles. The tiles are components inside
/// the board's own component, and a button nested that deep is not reliably given its clicks; the
/// root is where the team list's buttons live, and those work. Positions are therefore converted from
/// the tile's screen position into the root's units — screen divided by the window's scale, the same
/// conversion the bestiary button taught.
/// </summary>
public sealed unsafe class RouteOverlay : IDisposable
{
    /// <summary>Clear of the bestiary badges, the buttons and the next-room window.</summary>
    private const uint NodeIdBase = 0x42480000;

    private const int RecheckInterval = 10;
    private const float PinSize = 16f;

    /// <summary>
    /// The game font's own arrow. "▶" and "☆" are not in it — the first test showed them as bars and
    /// as empty buttons — while "★" is.
    /// </summary>
    private static readonly string PlannedGlyph = ((char)Dalamud.Game.Text.SeIconChar.ArrowRight).ToString();

    private const string ChosenGlyph = "★";
    private const string PinGlyph = "+";

    /// <summary>A tile's size in screen pixels when the board's grid has not been fitted yet.</summary>
    private const float FallbackTile = 30f;

    private static readonly Vector4 Chosen = new(1f, 0.85f, 0.2f, 1f);
    private static readonly Vector4 Planned = new(0.45f, 0.95f, 0.45f, 1f);
    private static readonly Vector4 Outline = new(0f, 0f, 0f, 1f);

    private static readonly string[] Windows = [XbmColumns.StageDetailList.Addon, XbmColumns.StageMap.Addon];

    private readonly Configuration configuration;
    private readonly BoardModel board;
    private readonly RouteKeeper route;
    private readonly Func<bool> nativeUiReady;

    private readonly Dictionary<uint, Mark> marks = [];
    private string attachedTo = string.Empty;

    /// <summary>Handed out once each, so a mark built after another was removed never reuses its id.</summary>
    private uint nextNodeId = NodeIdBase;
    private int ticksUntilRecheck;
    private bool broken;

    private sealed class Mark
    {
        public required TextNode Label { get; init; }
        public required TextButtonNode Pin { get; init; }
        public int EventIndex { get; set; } = -1;
    }

    public RouteOverlay(Configuration configuration, BoardModel board, RouteKeeper route, Func<bool> nativeUiReady)
    {
        this.configuration = configuration;
        this.board = board;
        this.route = route;
        this.nativeUiReady = nativeUiReady;

        foreach (var window in Windows)
            Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, window, OnFinalize);

        Services.Framework.Update += OnUpdate;
    }

    public string Status { get; private set; } = "Not attached.";

    private void OnUpdate(IFramework framework)
    {
        if (broken || --ticksUntilRecheck > 0)
            return;

        ticksUntilRecheck = RecheckInterval;

        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            broken = true;
            Status = $"Failed and switched off for this session: {ex.Message}";
            Services.Log.Error(ex, "Marking the route on the board failed; leaving the board window alone this session.");

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

    private void Refresh()
    {
        var window = board.Window;

        if (!configuration.ShowRouteOnBoard || !nativeUiReady() || window == null || board.Graph is not { } graph ||
            !AddonReader.IsOpen(window.Addon) || !AddonReader.TryGet(window.Addon, out var addon) ||
            addon->RootNode == null)
        {
            Detach();
            Status = configuration.ShowRouteOnBoard ? "No board window open." : "Turned off.";
            return;
        }

        if (attachedTo != window.Addon)
            Detach();

        attachedTo = window.Addon;

        var root = addon->RootNode;
        var scale = addon->Scale <= 0f ? 1f : addon->Scale;
        var rootScreen = new Vector2(root->ScreenX, root->ScreenY);
        var seen = new HashSet<uint>();

        foreach (var tile in window.Tiles)
        {
            if (!tile.Visible || tile.CellIndex < 0 || tile.CellIndex >= window.Cells.Count)
                continue;

            var cell = window.Cells[tile.CellIndex];
            if (!cell.IsRoom || graph.Node(cell.EventIndex) is not { IsStart: false } node)
                continue;

            seen.Add(tile.NodeId);
            var mark = MarkFor(tile.NodeId, root);
            mark.EventIndex = node.EventIndex;

            // The tile's drawn size is the grid's cell size on screen; its node size is in the component's
            // own units, which the first version mixed up with the window's.
            var side = (board.Projection?.CellSize ?? FallbackTile) / scale;
            var local = (tile.Centre - rootScreen) / scale - new Vector2(side / 2f);

            var chosen = route.IsChosen(node.EventIndex);
            var planned = route.IsPlanned(node.EventIndex);

            mark.Label.Position = local + new Vector2(-4f, side - 12f);
            mark.Label.String = chosen ? ChosenGlyph : planned ? PlannedGlyph : string.Empty;
            mark.Label.TextColor = chosen ? Chosen : Planned;
            mark.Label.IsVisible = chosen || planned;

            var fork = graph.OnMove(node.Move).Count > 1;
            mark.Pin.Position = local + new Vector2(side - (PinSize / 2f), -(PinSize / 2f));
            mark.Pin.String = chosen ? ChosenGlyph : PinGlyph;
            mark.Pin.IsVisible = fork;
        }

        foreach (var id in new List<uint>(marks.Keys))
        {
            if (!seen.Contains(id))
                Remove(id);
        }

        Status = $"Marking {marks.Count} rooms on {window.Addon}.";
    }

    private Mark MarkFor(uint tileNodeId, AtkResNode* root)
    {
        if (marks.TryGetValue(tileNodeId, out var existing))
            return existing;

        var label = new TextNode
        {
            NodeId = nextNodeId++,
            Size = new Vector2(20f, 16f),
            FontSize = 14,
            TextColor = Planned,
            TextOutlineColor = Outline,
            IsVisible = false,
        };

        Mark? mark = null;
        var pin = new TextButtonNode
        {
            NodeId = nextNodeId++,
            Size = new Vector2(PinSize, PinSize),
            String = PinGlyph,
            IsVisible = false,
            OnClick = () =>
            {
                if (mark is { EventIndex: >= 0 })
                    route.Toggle(mark.EventIndex);

                ticksUntilRecheck = 0;
            },
        };

        mark = new Mark { Label = label, Pin = pin };
        label.AttachNode(root, NodePosition.AsLastChild);
        pin.AttachNode(root, NodePosition.AsLastChild);
        marks[tileNodeId] = mark;
        return mark;
    }

    private void Remove(uint tileNodeId)
    {
        if (!marks.Remove(tileNodeId, out var mark))
            return;

        mark.Label.DetachNode();
        mark.Label.Dispose();
        mark.Pin.DetachNode();
        mark.Pin.Dispose();
    }

    /// <summary>Anything left on a window being torn down corrupts it, so its closing takes every mark off.</summary>
    private void OnFinalize(AddonEvent type, AddonArgs args) => Detach();

    private void Detach()
    {
        foreach (var id in new List<uint>(marks.Keys))
            Remove(id);

        attachedTo = string.Empty;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Services.AddonLifecycle.UnregisterListener(OnFinalize);
        Detach();
    }
}
