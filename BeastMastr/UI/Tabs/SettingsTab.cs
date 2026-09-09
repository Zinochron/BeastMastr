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
