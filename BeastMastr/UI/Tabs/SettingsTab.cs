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
    /// Filling a run's team. Acts only while the bestiary and the roster are both open, which is
    /// when a team is being put together and at no other time.
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
            "Adds a \"Fill for levelling\" button to the team list and under the bestiary while a " +
            "team is being put together. It empties the team first, then adds the carries and the " +
            "least advanced beasts. It only appears where it does something.");

        var leveling = configuration.TeamSelection == TeamMode.Leveling;
        if (ImGui.Checkbox("Fill the team for levelling", ref leveling))
        {
            configuration.TeamSelection = leveling ? TeamMode.Leveling : TeamMode.Off;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "Does the same as the button, but on its own as soon as a team is being put together. " +
            "Leave it off if you would rather ask for it — the button is there either way.");

        if (teamSelector.Status.Length > 0)
            ImGui.TextDisabled(teamSelector.Status);
    }

    /// <summary>
    /// The one setting here that changes the game rather than the plugin, so it says plainly what
    /// it will do and shows what it last did.
    /// </summary>
    private void DrawFightAutomation()
    {
        var repeat = configuration.FightSelection == FightMode.RepeatLast;
        if (ImGui.Checkbox("Call the same familiars as the last fight", ref repeat))
        {
            configuration.FightSelection = repeat ? FightMode.RepeatLast : FightMode.Off;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "When the window asks which familiars to call, picks the ones you took last time. It " +
            "only acts when nothing is chosen yet, so it never overrides a choice you started, and " +
            "it stops as soon as a pick does not take.");

        if (configuration.FightPrompt.Length == 0)
        {
            ImGui.TextDisabled("Waiting to see a fight selection — call familiars once by hand first.");
        }
        else if (configuration.LastFightBeasts.Count == 0)
        {
            ImGui.TextDisabled("Nothing remembered yet.");
        }
        else
        {
            ImGui.TextDisabled($"Remembered: {configuration.LastFightBeasts.Count} familiar(s).");
        }

        if (fightSelector.Status.Length > 0)
            ImGui.TextDisabled(fightSelector.Status);
    }

    public void Dispose() { }
}
