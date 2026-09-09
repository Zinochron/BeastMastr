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

        var fill = new TextButtonNode
        {
            NodeId = ButtonNodeIdBase,
            Size = new Vector2(150f, 28f),
            // Under the window rather than inside it: the bestiary's own area is full of tiles, and
            // a button dropped among them would cover one.
            Position = new Vector2(20f, addon->GetScaledHeight(true) + 4f),
            String = "Fill for levelling",
            IsVisible = true,
            OnClick = teamSelector.RequestFill,
        };

        fill.AttachNode(addon, NodePosition.AsLastChild);
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
