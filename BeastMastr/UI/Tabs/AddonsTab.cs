using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// Raw view of a game window: its AtkValues and its node tree.
///
/// This is what turns "the board shows a room here" into a node id and a screen rectangle the
/// overlay can anchor to, and what says whether the bestiary list recycles its rows.
/// </summary>
public sealed class AddonsTab : ITab
{
    private readonly Configuration configuration;
    private readonly DelayedSweep delayedSweep;
    private int armSeconds = 5;

    private string addonName;
    private List<AddonReader.Node> nodes = [];
    private List<AddonReader.Value> values = [];
    private bool hideInvisible = true;
    private bool liveRefresh;
    private string lastValuePath = string.Empty;
    private string lastNodePath = string.Empty;
    private string lastSweepPath = string.Empty;

    public AddonsTab(Configuration configuration, DelayedSweep delayedSweep)
    {
        this.configuration = configuration;
        this.delayedSweep = delayedSweep;
        addonName = configuration.LastAddon;
    }

    public string Title => "Windows";
    public string Id => "addons";

    public void Draw()
    {
        DrawPicker();
        ImGuiHelpers.ScaledDummy(4f);
        DrawOpenWindows();
        ImGuiHelpers.ScaledDummy(4f);
        DrawSweep();
        ImGuiHelpers.ScaledDummy(4f);
        DrawValues();
        ImGuiHelpers.ScaledDummy(4f);
        DrawNodes();
    }

    private void DrawPicker()
    {
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##addonName", ref addonName, 64);

        ImGui.SameLine();
        if (ImGui.Button("Capture"))
            Capture();

        ImGui.SameLine();
        using (var combo = ImRaii.Combo("##knownAddons", "Beastmaster windows"))
        {
            if (combo.Success)
            {
                foreach (var (addon, note) in BeastmasterData.Addons)
                {
                    var open = AddonReader.IsOpen(addon);
                    if (ImGui.Selectable($"{addon}{(open ? "  (open)" : string.Empty)}"))
                    {
                        addonName = addon;
                        Capture();
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(note);
                }
            }
        }

        ImGui.Checkbox("Hide invisible nodes", ref hideInvisible);
        ImGui.SameLine();
        ImGui.Checkbox("Re-capture every frame", ref liveRefresh);
        Widgets.HelpMarker(
            "Leave this on while scrolling a list. If the same node ids come back holding different " +
            "text, the list recycles its rows, and anything attached to them has to be keyed to the " +
            "content rather than to the row.");

        if (liveRefresh)
            Capture();
    }

    private void Capture()
    {
        nodes = AddonReader.Nodes(addonName);
        values = AddonReader.Values(addonName);

        // Only when it actually changed: live re-capture runs every frame, and saving the config
        // every frame would hammer the disk for nothing.
        if (configuration.LastAddon == addonName)
            return;

        configuration.LastAddon = addonName;
        configuration.Save();
    }

    /// <summary>
    /// Which Beastmaster windows exist right now, asked of the game rather than tested against a
    /// fixed list — that is how windows nobody has named yet show up at all.
    /// </summary>
    private static void DrawOpenWindows()
    {
        if (!ImGui.CollapsingHeader("Beastmaster windows open right now", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var open = AddonReader.OpenAddonNames();
        if (open.Count == 0)
        {
            ImGui.TextDisabled("None. Open one in game, then capture it.");
            return;
        }

        foreach (var addon in open)
        {
            ImGui.BulletText(addon);
            ImGui.SameLine();
            ImGui.TextDisabled(BeastmasterData.NoteFor(addon));
        }
    }

    /// <summary>
    /// Everything open, in one file — now, or after a delay.
    ///
    /// The delay is not a convenience. Several Beastmaster windows only exist while the cursor
    /// rests on something and close the moment you reach for a button, so they cannot be captured
    /// by pressing anything: arm the timer, put the cursor back, and let it fire on its own.
    /// </summary>
    private void DrawSweep()
    {
        if (ImGui.Button("Sweep now"))
            lastSweepPath = DelayedSweep.Capture() ?? "could not be written — see the log";

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
        ImGui.SliderInt("##armSeconds", ref armSeconds, 2, 20, "%d s");

        ImGui.SameLine();
        if (ImGui.Button($"Sweep in {armSeconds}s"))
            delayedSweep.Arm(armSeconds);

        Widgets.HelpMarker(
            "For windows that only exist while you hover: the shop, item descriptions, an enemy's " +
            "detail panel. Arm this, move the cursor onto the thing, and hold it there until the " +
            "countdown reaches zero.");

        if (delayedSweep.SecondsRemaining is { } remaining)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel"))
                delayedSweep.Cancel();

            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f),
                              $"Hold the cursor still — capturing in {remaining:0.0}s");
        }

        if (delayedSweep.LastPath.Length > 0)
            ImGui.TextDisabled(delayedSweep.LastPath);

        if (lastSweepPath.Length > 0)
            ImGui.TextDisabled(lastSweepPath);
    }

    private void DrawValues()
    {
        if (!ImGui.CollapsingHeader($"AtkValues ({values.Count})"))
            return;

        if (values.Count == 0)
        {
            ImGui.TextDisabled("Nothing captured. The window has to be open when you press Capture.");
            return;
        }

        Widgets.CopyButton("Copy values", AddonReader.ToText(addonName, values));
        ImGui.SameLine();
        Widgets.SaveButton("Save values", $"{addonName}-values",
                           AddonReader.ToText(addonName, values), ref lastValuePath);

        using var child = ImRaii.Child("##valueBody", new Vector2(0, 240 * ImGuiHelpers.GlobalScale), true);
        if (!child.Success)
            return;

        using var table = ImRaii.Table("##valueTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 40f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Value");
        ImGui.TableHeadersRow();

        foreach (var value in values)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value.Index.ToString());
            ImGui.TableNextColumn();
            ImGui.TextDisabled(value.Type);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value.Text);
        }
    }

    private void DrawNodes()
    {
        if (!ImGui.CollapsingHeader($"Nodes ({nodes.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (nodes.Count == 0)
        {
            ImGui.TextDisabled("Nothing captured. The window has to be open when you press Capture.");
            return;
        }

        Widgets.CopyButton("Copy node tree", AddonReader.ToText(addonName, nodes));
        ImGui.SameLine();
        Widgets.SaveButton("Save node tree", $"{addonName}-nodes",
                           AddonReader.ToText(addonName, nodes), ref lastNodePath);

        using var child = ImRaii.Child("##nodeBody", Vector2.Zero, true, ImGuiWindowFlags.HorizontalScrollbar);
        if (!child.Success)
            return;

        foreach (var node in nodes)
        {
            if (hideInvisible && !node.Visible)
                continue;

            ImGui.Dummy(new Vector2(node.Depth * 12f * ImGuiHelpers.GlobalScale, 0));
            ImGui.SameLine(0, 0);

            ImGui.TextUnformatted($"[{node.NodeId}]");
            ImGui.SameLine();
            ImGui.TextDisabled(node.Type);
            ImGui.SameLine();
            ImGui.TextDisabled($"@{node.ScreenX:0}/{node.ScreenY:0} {node.Width:0}x{node.Height:0}");

            if (node.Text.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"\"{node.Text}\"");
            }
        }
    }

    public void Dispose() { }
}
