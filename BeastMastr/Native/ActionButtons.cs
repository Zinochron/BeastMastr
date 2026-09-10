using System;
using System.Collections.Generic;
using System.Numerics;
using BeastMastr.Automation;
using BeastMastr.Data;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

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
    /// Sized to the gap the bestiary already leaves. Measured off a capture rather than judged by
    /// eye: the bottom row of tiles ends twenty-five units above the "Beasts Captured" caption, so
    /// a twenty-four high button starting one unit under the tiles fills that gap exactly.
    ///
    /// Earlier attempts missed for the same underlying reason — a position compared against
    /// something measured in different units. First against the window height as
    /// <c>GetScaledHeight</c> reports it, which is screen pixels while a child's position is local,
    /// so the button landed a whole UI scale factor too low. Then against a tile's own <c>Y</c>,
    /// which is measured from the container the tiles sit in rather than from the window.
    ///
    /// The fix is not better arithmetic, it is not needing any: each button hangs off the same
    /// container as the thing it is placed against, so their positions are already in the same units.
    /// </summary>
    private const float ButtonHeight = 24f;

    private const float ButtonWidth = 140f;

    private readonly Configuration configuration;
    private readonly TeamSelector teamSelector;
    private readonly Func<bool> nativeUiReady;

    /// <summary>Keyed by the window each one is attached to.</summary>
    private readonly Dictionary<string, TextButtonNode> buttons = [];

    private int ticksUntilRecheck;
    private bool broken;

    public ActionButtons(Configuration configuration, TeamSelector teamSelector, Func<bool> nativeUiReady)
    {
        this.configuration = configuration;
        this.teamSelector = teamSelector;
        this.nativeUiReady = nativeUiReady;

        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, XbmColumns.MonsterNotebook.Addon, OnFinalize);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, XbmColumns.PetParty.Addon, OnFinalize);
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

            if (!buttons.ContainsKey(XbmColumns.MonsterNotebook.Addon))
                AttachUnderTheTiles();

            if (!buttons.ContainsKey(XbmColumns.PetParty.Addon))
                AttachAboveTheRoster();
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

    /// <summary>
    /// Both windows close together, and a button left attached to a window being torn down is the
    /// kind of leak that corrupts it — so either one closing takes both off. They come back on the
    /// next check if the other is somehow still up.
    /// </summary>
    private void OnFinalize(AddonEvent type, AddonArgs args) => Detach();

    /// <summary>In the bestiary's gap between its last row of tiles and its footer.</summary>
    private void AttachUnderTheTiles()
    {
        if (!AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon))
            return;

        var lastTile = addon->GetNodeById(
            (uint)XbmColumns.MonsterNotebook.TileNodeId(XbmColumns.MonsterNotebook.TileCount - 1));

        var grid = lastTile == null ? null : lastTile->ParentNode;
        if (grid == null)
            return;

        Attach(XbmColumns.MonsterNotebook.Addon, grid, ButtonNodeIdBase,
               new Vector2(0f, lastTile->Y + lastTile->Height + 1f));
    }

    /// <summary>
    /// Above the team list in the roster window, where the team being filled is the thing in front
    /// of you. Placed against the list component itself and hung off the same parent, for the same
    /// reason as the bestiary's: then the two positions are in the same units and nothing converts.
    ///
    /// Where the list sits flush with the top and leaves no room above it, the button goes below it
    /// instead — a button over the list's first row would take the clicks meant for that row.
    /// </summary>
    private void AttachAboveTheRoster()
    {
        if (!AddonReader.TryGet(XbmColumns.PetParty.Addon, out var addon))
            return;

        var list = FindList(addon);
        var parent = list == null ? null : list->ParentNode;
        if (parent == null)
            return;

        var above = list->Y - ButtonHeight - 4f;
        var position = above >= 0f
                           ? new Vector2(list->X, above)
                           : new Vector2(list->X, list->Y + list->Height + 4f);

        Attach(XbmColumns.PetParty.Addon, parent, ButtonNodeIdBase + 1, position);
        Services.Log.Debug($"Roster button placed at {position.X:0}/{position.Y:0} against a list at " +
                           $"{list->X:0}/{list->Y:0} {list->Width}x{list->Height}.");
    }

    private void Attach(string addonName, AtkResNode* parent, int nodeId, Vector2 position)
    {
        var button = new TextButtonNode
        {
            NodeId = (uint)nodeId,
            Size = new Vector2(ButtonWidth, ButtonHeight),
            Position = position,
            String = "Fill for levelling",
            IsVisible = true,
            OnClick = teamSelector.RequestFill,
        };

        button.AttachNode(parent, NodePosition.AsLastChild);
        buttons[addonName] = button;
    }

    /// <summary>The window's list component node, found by what it is rather than by an id nobody has captured.</summary>
    private static AtkResNode* FindList(AtkUnitBase* addon)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (uint)node->Type < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && component->GetComponentType() == ComponentType.List)
                return node;
        }

        return null;
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
