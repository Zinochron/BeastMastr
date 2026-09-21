using Dalamud.Bindings.ImGui;

namespace BeastMastr.UI.Tabs;

public sealed class SettingsTab : ITab
{
    private readonly Configuration configuration;
    private readonly Automation.FightSelector fightSelector;
    private readonly Automation.TeamSelector teamSelector;
    private readonly Automation.DifficultySelector difficultySelector;
    private readonly Native.NextRoomPanel nextRoom;

    public SettingsTab(Configuration configuration, Automation.FightSelector fightSelector,
                       Automation.TeamSelector teamSelector, Automation.DifficultySelector difficultySelector,
                       Native.NextRoomPanel nextRoom)
    {
        this.configuration = configuration;
        this.fightSelector = fightSelector;
        this.teamSelector = teamSelector;
        this.difficultySelector = difficultySelector;
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

        DrawCrucibleMode();
        ImGui.Separator();

        var decorate = configuration.DecorateNotebook;
        if (ImGui.Checkbox("Decorate the game's bestiary", ref decorate))
        {
            configuration.DecorateNotebook = decorate;
            configuration.Save();
        }

        Widgets.HelpMarker("Status tags on the bestiary's tiles; the Beasts tab's filter dims the rest.");

        var briefRoom = configuration.ShowNextRoom;
        if (ImGui.Checkbox("Brief the next room during a run", ref briefRoom))
        {
            configuration.ShowNextRoom = briefRoom;
            configuration.Save();
        }

        Widgets.HelpMarker("A game window with the room ahead; drag it anywhere, the arrows look further. " +
                           "Also /beastmastr room.");

        ImGui.TextDisabled(nextRoom.Status);

        ImGui.Separator();

        var showDebug = configuration.ShowDataTab;
        if (ImGui.Checkbox("Show the Debug tab", ref showDebug))
        {
            configuration.ShowDataTab = showDebug;
            configuration.Save();
        }

        Widgets.HelpMarker("Recorder, single steps, fight internals, map and the data explorers.");

        if (configuration.ShowDataTab)
        {
            var pageSize = configuration.SheetPageSize;
            if (ImGui.SliderInt("Sheet rows per page", ref pageSize, 10, 500))
            {
                configuration.SheetPageSize = pageSize;
                configuration.Save();
            }
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

        Widgets.HelpMarker("\"Fill for levelling\" on the team list and under the bestiary: empties the team, then " +
                           "adds the carries and the least advanced. Only when pressed.");

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
        if (ImGui.Checkbox("Call the last fight's familiars when a fight asks", ref onOpen))
        {
            configuration.CallLastFamiliarsOnOpen = onOpen;
            configuration.Save();
        }

        Widgets.HelpMarker("Once per fight window, only if nothing is called yet. The team list's \"Call last " +
                           "familiars\" repeats it; shops and campsites get \"Pick lowest HP\".");

        var carriesFirst = configuration.CallCarriesFirst;
        if (ImGui.Checkbox("Call the carries first, unless they are down", ref carriesFirst))
        {
            configuration.CallCarriesFirst = carriesFirst;
            configuration.Save();
        }

        Widgets.HelpMarker("Carries go first unless down; a downed familiar is replaced by the healthiest.");

        ImGui.TextDisabled(configuration.CarryBeasts.Count == 0
                               ? "No carries marked."
                               : $"Carries: {configuration.CarryBeasts.Count}.");

        ImGui.TextDisabled(configuration.LastFightBeasts.Count == 0
                               ? "Nothing remembered yet — call familiars by hand once."
                               : $"Remembered: {configuration.LastFightBeasts.Count} familiar(s).");

        if (fightSelector.Status.Length > 0)
            ImGui.TextDisabled(fightSelector.Status);
    }

    /// <summary>
    /// The Crucible mode, which the game itself forgets between visits.
    /// </summary>
    private void DrawCrucibleMode()
    {
        var remember = configuration.RememberCrucibleMode;
        if (ImGui.Checkbox("Restore the last Crucible mode when the board opens", ref remember))
        {
            configuration.RememberCrucibleMode = remember;
            configuration.Save();
        }

        Widgets.HelpMarker("The game resets it to Standard every visit; this restores yours once per opening.");

        if (difficultySelector.Status.Length > 0)
            ImGui.TextDisabled(difficultySelector.Status);
    }

    public void Dispose() { }
}
