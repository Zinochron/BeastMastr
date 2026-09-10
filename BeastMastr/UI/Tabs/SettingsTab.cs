using Dalamud.Bindings.ImGui;

namespace BeastMastr.UI.Tabs;

public sealed class SettingsTab : ITab
{
    private readonly Configuration configuration;
    private readonly Automation.FightSelector fightSelector;
    private readonly Automation.TeamSelector teamSelector;
    private readonly Native.NextRoomPanel nextRoom;

    public SettingsTab(Configuration configuration, Automation.FightSelector fightSelector,
                       Automation.TeamSelector teamSelector, Native.NextRoomPanel nextRoom)
    {
        this.configuration = configuration;
        this.fightSelector = fightSelector;
        this.teamSelector = teamSelector;
        this.nextRoom = nextRoom;
    }

    public string Title => "Settings";
    public string Id => "settings";

    public void Draw()
    {
        DrawTeamAutomation();
        ImGui.Separator();

        DrawFightAutomation();
        ImGui.Separator();

        var decorate = configuration.DecorateNotebook;
        if (ImGui.Checkbox("Decorate the game's bestiary", ref decorate))
        {
            configuration.DecorateNotebook = decorate;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "Puts a status tag on each tile of the Master's Bestiary and dims the beasts the filter " +
            "in the Beasts tab excludes. Turning this off hands the window back exactly as the game " +
            "draws it, the next time it opens.");

        var briefRoom = configuration.ShowNextRoom;
        if (ImGui.Checkbox("Brief the next room during a run", ref briefRoom))
        {
            configuration.ShowNextRoom = briefRoom;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "A window of the game's own showing what the room you are about to enter holds, and " +
            "nothing about the other eleven. Drag it where you want it; it stays there. The arrows " +
            "look further ahead. What it knows about enemies comes from the board — click through " +
            "the rooms once and it fills in. /beastmastr room opens it at any time.");

        ImGui.TextDisabled(nextRoom.Status);

        var overlay = configuration.ShowBoardOverlay;
        if (ImGui.Checkbox("Show room cards on the Crucible board", ref overlay))
        {
            configuration.ShowBoardOverlay = overlay;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "Hangs a card under each room icon out in the world. Off by default: the placement is " +
            "right but a card under every floating icon across a whole board is not yet a good way " +
            "to read one. The reading behind it happens either way — the Board tab shows it.");

        ImGui.Separator();

        var showData = configuration.ShowDataTab;
        if (ImGui.Checkbox("Show the Data tab", ref showData))
        {
            configuration.ShowDataTab = showData;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "The raw sheet and addon explorer. It exists to pin down the Beastmaster sheets, whose " +
            "columns are unnamed upstream. Takes effect the next time the plugin loads.");

        var pageSize = configuration.SheetPageSize;
        if (ImGui.SliderInt("Sheet rows per page", ref pageSize, 10, 500))
        {
            configuration.SheetPageSize = pageSize;
            configuration.Save();
        }
    }

    /// <summary>
    /// Filling a run's team, on its button in the team list and at no other time.
    /// </summary>
    private void DrawTeamAutomation()
    {
        var showButtons = configuration.ShowActionButtons;
        if (ImGui.Checkbox("Put buttons in the game's own windows", ref showButtons))
        {
            configuration.ShowActionButtons = showButtons;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "Adds a \"Fill for levelling\" button to the team list whenever a team is being put " +
            "together, and under the bestiary while that is open too. It empties the team with the " +
            "game's own \"Remove all\", opens the bestiary if it is not already up, then adds the " +
            "carries and the least advanced beasts. Only on the press — it never fills a team on its own.");

        if (teamSelector.Status.Length > 0)
            ImGui.TextDisabled(teamSelector.Status);
    }

    /// <summary>
    /// The one setting here that changes the game rather than the plugin, so it says plainly what
    /// it will do and shows what it last did.
    /// </summary>
    private void DrawFightAutomation()
    {
        var onOpen = configuration.CallLastFamiliarsOnOpen;
        if (ImGui.Checkbox("Call the last fight's familiars when the fight window opens", ref onOpen))
        {
            configuration.CallLastFamiliarsOnOpen = onOpen;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "Once per fight, as the window opens, and only if nothing is called yet — in the fight " +
            "window only, never at shops or campsites. After that it leaves the window alone, so " +
            "whatever you change stays changed. The \"Call last familiars\" button on the team " +
            "list does the same again whenever you press it. At shops and campsites the team list " +
            "gets a \"Pick lowest HP\" button instead.");

        ImGui.TextDisabled(configuration.LastFightBeasts.Count == 0
                               ? "Nothing remembered yet — call familiars by hand once."
                               : $"Remembered: {configuration.LastFightBeasts.Count} familiar(s).");

        if (fightSelector.Status.Length > 0)
            ImGui.TextDisabled(fightSelector.Status);
    }

    public void Dispose() { }
}
