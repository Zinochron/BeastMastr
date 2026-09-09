using Dalamud.Bindings.ImGui;

namespace BeastMastr.UI.Tabs;

public sealed class SettingsTab : ITab
{
    private readonly Configuration configuration;

    public SettingsTab(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public string Title => "Settings";
    public string Id => "settings";

    public void Draw()
    {
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
            "Marks each room on the board with what it holds, and lists the whole board beside it — " +
            "the thing that otherwise costs a click per room to read.");

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

    public void Dispose() { }
}
