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
/// **Nothing here may fight a choice made by hand.** There used to be modes that filled a team or
/// called familiars on their own whenever the state did not match a plan, checked every frame, and
/// they fought every change: swap one beast and the team was emptied and refilled under you. Now a
/// press does one thing once. The only thing that acts by itself is the familiar call as the fight
/// window opens, and that fires once per opening, not every frame.
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

    /// <summary>One per job. The team list carries a different button for each of its two modes.</summary>
    private enum Kind
    {
        /// <summary>Team list, building a run's team.</summary>
        FillFromTeamList,

        /// <summary>Under the bestiary's tiles, while it is open beside the team list.</summary>
        FillFromBestiary,

        /// <summary>Team list, calling a fight's familiars.</summary>
        CallFromTeamList,

        /// <summary>Team list, picking familiars at a shop or a campsite.</summary>
        PickHurtFromTeamList,
    }

    private readonly Configuration configuration;
    private readonly TeamSelector teamSelector;
    private readonly FightSelector fightSelector;
    private readonly HealthSelector healthSelector;
    private readonly Func<bool> nativeUiReady;

    private readonly Dictionary<Kind, TextButtonNode> buttons = [];

    private int ticksUntilRecheck;
    private bool broken;

    public ActionButtons(Configuration configuration, TeamSelector teamSelector, FightSelector fightSelector,
                         HealthSelector healthSelector, Func<bool> nativeUiReady)
    {
        this.configuration = configuration;
        this.teamSelector = teamSelector;
        this.fightSelector = fightSelector;
        this.healthSelector = healthSelector;
        this.nativeUiReady = nativeUiReady;

        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, XbmColumns.MonsterNotebook.Addon, OnFinalize);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, XbmColumns.PetParty.Addon, OnFinalize);
        Services.Framework.Update += OnUpdate;
    }

    private static bool BestiaryOpen => AddonReader.IsOpen(XbmColumns.MonsterNotebook.Addon);

    private void OnUpdate(IFramework framework)
    {
        if (broken || --ticksUntilRecheck > 0)
            return;

        ticksUntilRecheck = RecheckInterval;

        try
        {
            if (!configuration.ShowActionButtons || !nativeUiReady() || !PetPartyReader.IsOpen)
            {
                Detach();
                return;
            }

            // The team list is the screen for every job; its own mode number says which one it is
            // doing, and each job gets its own button. Everything that is neither building a team
            // nor calling a fight is a shop or a campsite, where what you pick is who to look after.
            var mode = PetPartyReader.Mode();
            var team = mode == XbmColumns.PetParty.TeamCompositionMode;
            var fight = mode == XbmColumns.PetParty.FightMode;

            Want(Kind.FillFromTeamList, team);
            Want(Kind.FillFromBestiary, team && BestiaryOpen);
            Want(Kind.CallFromTeamList, fight);
            Want(Kind.PickHurtFromTeamList, mode != null && !team && !fight);
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

    private void Want(Kind kind, bool wanted)
    {
        var have = buttons.ContainsKey(kind);

        if (wanted && !have)
        {
            switch (kind)
            {
                case Kind.FillFromTeamList:
                    AboveTheTeamList(kind, "Fill for levelling", teamSelector.RequestFill);
                    break;

                case Kind.FillFromBestiary:
                    UnderTheTiles(kind, "Fill for levelling", teamSelector.RequestFill);
                    break;

                case Kind.CallFromTeamList:
                    AboveTheTeamList(kind, "Call last familiars", fightSelector.RequestRepeat);
                    break;

                case Kind.PickHurtFromTeamList:
                    AboveTheTeamList(kind, "Pick lowest HP", healthSelector.RequestPick);
                    break;
            }
        }
        else if (!wanted && have)
        {
            Remove(kind);
        }
    }

    /// <summary>
    /// A button left attached to a window being torn down is the kind of leak that corrupts it, so
    /// either window closing takes every button off. The ones still wanted come straight back on the
    /// next check.
    /// </summary>
    private void OnFinalize(AddonEvent type, AddonArgs args) => Detach();

    /// <summary>In the bestiary's gap between its last row of tiles and its footer.</summary>
    private void UnderTheTiles(Kind kind, string label, Action onClick)
    {
        if (!AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon))
            return;

        var lastTile = addon->GetNodeById(
            (uint)XbmColumns.MonsterNotebook.TileNodeId(XbmColumns.MonsterNotebook.TileCount - 1));

        var grid = lastTile == null ? null : lastTile->ParentNode;
        if (grid == null)
            return;

        Attach(kind, grid, new Vector2(0f, lastTile->Y + lastTile->Height + 1f), label, onClick);
    }

    /// <summary>
    /// Above the list in the team list window. Placed against the list component itself and hung off
    /// the same parent, for the same reason as the bestiary's: then the two positions are in the same
    /// units and nothing converts. The capture shows the room for it — the header's separator ends 57
    /// units down and the list starts at 90.
    ///
    /// Where the list sits flush with the top and leaves no room above it, the button goes below it
    /// instead — a button over the list's first row would take the clicks meant for that row.
    /// </summary>
    private void AboveTheTeamList(Kind kind, string label, Action onClick)
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

        Attach(kind, parent, position, label, onClick);
    }

    private void Attach(Kind kind, AtkResNode* parent, Vector2 position, string label, Action onClick)
    {
        var button = new TextButtonNode
        {
            NodeId = (uint)(ButtonNodeIdBase + (int)kind),
            Size = new Vector2(ButtonWidth, ButtonHeight),
            Position = position,
            String = label,
            IsVisible = true,
            OnClick = onClick,
        };

        button.AttachNode(parent, NodePosition.AsLastChild);
        buttons[kind] = button;
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

    private void Remove(Kind kind)
    {
        if (!buttons.Remove(kind, out var button))
            return;

        button.DetachNode();
        button.Dispose();
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
