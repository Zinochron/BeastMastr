using Dalamud.Bindings.ImGui;

namespace BeastMastr.UI.Tabs;

public sealed class SettingsTab : ITab
{
    private readonly Configuration configuration;
    private readonly Automation.FightSelector fightSelector;

    public SettingsTab(Configuration configuration, Automation.FightSelector fightSelector)
    {
        this.configuration = configuration;
        this.fightSelector = fightSelector;
    }

    public string Title => "Settings";
    public string Id => "settings";

    public void Draw()
    {
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
