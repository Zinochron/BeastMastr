using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// Everything for testing and mapping, in one place and hidden unless switched on in Settings: the run
/// automation's insides, the sheet explorer, the window inspector and the board captures.
/// </summary>
public sealed class DebugTab : ITab
{
    private readonly Configuration configuration;
    private readonly List<ITab> pages;

    public DebugTab(Configuration configuration, IEnumerable<ITab> pages)
    {
        this.configuration = configuration;
        this.pages = new List<ITab>(pages);
    }

    public string Title => "Debug";
    public string Id => "debug";

    public bool Visible => configuration.ShowDataTab;

    public void Draw()
    {
        using var bar = ImRaii.TabBar("##BeastMastrDebugTabs");
        if (!bar.Success)
            return;

        foreach (var page in pages)
        {
            using var item = ImRaii.TabItem($"{page.Title}###{page.Id}");
            if (!item.Success)
                continue;

            using var child = ImRaii.Child($"##{page.Id}Body", System.Numerics.Vector2.Zero, false);
            if (child.Success)
                page.Draw();
        }
    }

    public void Dispose()
    {
        foreach (var page in pages)
            page.Dispose();

        pages.Clear();
    }
}
