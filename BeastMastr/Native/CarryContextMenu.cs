using System;
using System.Linq;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Game.Gui.ContextMenu;

namespace BeastMastr.Native;

/// <summary>
/// Adds "Add as carry" / "Remove as carry" to a beast's right-click menu in the bestiary.
///
/// Carries are chosen while looking at the beasts, so this is where choosing them belongs — the
/// checkboxes in the Beasts tab ask you to find the same beast twice, once in the game and once in
/// a list beside it.
/// </summary>
public sealed class CarryContextMenu : IDisposable
{
    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly EventRecorder hover;

    public CarryContextMenu(Configuration configuration, BeastCatalog catalog, EventRecorder hover)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.hover = hover;

        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.AddonName != XbmColumns.MonsterNotebook.Addon)
            return;

        if (BeastUnderCursor() is not { } beast)
            return;

        var carrying = configuration.CarryBeasts.Contains(beast.Number);
        var full = configuration.CarryBeasts.Count >= TeamPlanner.MaxCarries;

        // A menu entry that cannot do anything is worse than no entry: it looks like a bug rather
        // than a limit. Past three carries there is nothing left to level, so it is simply not shown.
        if (!carrying && full)
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = carrying ? $"Remove {beast.Name} as carry" : $"Add {beast.Name} as carry",
            PrefixChar = 'B',
            OnClicked = _ => Toggle(beast.Number),
        });
    }

    /// <summary>
    /// The beast whose tile the cursor is on. The window gives no other handle: the menu itself
    /// carries no item id, so the only thing tying a right click to a beast is the tile the cursor
    /// last entered, and the icon that tile is showing.
    /// </summary>
    private Beast? BeastUnderCursor()
    {
        var slot = hover.HoveredNotebookSlot;
        if (slot < 0 || slot >= XbmColumns.MonsterNotebook.TileCount)
            return null;

        var addon = AddonReader.Find(XbmColumns.MonsterNotebook.Addon);
        if (addon.IsNull)
            return null;

        var values = addon.AtkValues.ToList();
        var index = XbmColumns.MonsterNotebook.SlotIconValue(slot);

        if (index >= values.Count || !values[index].TryGet<uint>(out var icon))
            return null;

        return catalog.ByIcon.TryGetValue(icon, out var beast) ? beast : null;
    }

    private void Toggle(uint beastNumber)
    {
        if (!configuration.CarryBeasts.Remove(beastNumber))
        {
            if (configuration.CarryBeasts.Count >= TeamPlanner.MaxCarries)
                return;

            configuration.CarryBeasts.Add(beastNumber);
        }

        configuration.Save();
    }

    public void Dispose() => Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
}
