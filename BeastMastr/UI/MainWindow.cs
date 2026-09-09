using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace BeastMastr.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly List<ITab> tabs;

    /// <summary>Set to have the next draw jump to a specific tab, e.g. when opened via the config button.</summary>
    private string? pendingTabId;

    public MainWindow(IEnumerable<ITab> tabs) : base("BeastMastr###BeastMastrMain")
    {
        this.tabs = new List<ITab>(tabs);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(960, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void OpenAt(string tabId)
    {
        pendingTabId = tabId;
        IsOpen = true;
        BringToFront();
    }

    public override void Draw()
    {
        using var bar = ImRaii.TabBar("##BeastMastrTabs");
        if (!bar.Success)
            return;

        foreach (var tab in tabs)
        {
            var flags = ImGuiTabItemFlags.None;
            if (pendingTabId == tab.Id)
            {
                flags |= ImGuiTabItemFlags.SetSelected;
                pendingTabId = null;
            }

            using var item = ImRaii.TabItem($"{tab.Title}###{tab.Id}", flags);
            if (!item.Success)
                continue;

            using var child = ImRaii.Child($"##{tab.Id}Body", Vector2.Zero, false);
            if (child.Success)
                tab.Draw();
        }
    }

    public void Dispose()
    {
        foreach (var tab in tabs)
            tab.Dispose();

        tabs.Clear();
    }
}
