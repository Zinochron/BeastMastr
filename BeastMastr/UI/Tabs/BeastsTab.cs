using System;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// The beasts, filterable. The plan puts the real bestiary work inside the game's own notebook and
/// keeps this as the fallback — but it is also how the derived data gets checked against what the
/// book says, so it earns its place either way.
/// </summary>
public sealed class BeastsTab : ITab
{
    private readonly BeastCatalog catalog;

    /// <summary>Shared with the notebook decorator, so filtering here dims the game's own tiles.</summary>
    private readonly BeastFilter filter;

    private string search = string.Empty;

    public BeastsTab(BeastCatalog catalog, BeastFilter filter)
    {
        this.catalog = catalog;
        this.filter = filter;
    }

    public string Title => "Beasts";
    public string Id => "beasts";

    public void Draw()
    {
        DrawFilter();
        ImGuiHelpers.ScaledDummy(4f);
        DrawTable();
    }

    private void DrawFilter()
    {
        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##search", "name, action, or anything an action says", ref search, 64))
            filter.Search = search;

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            filter.Clear();
            search = string.Empty;
        }

        ImGui.SameLine();
        var total = catalog.Beasts.Count;
        var shown = filter.Apply(catalog.Beasts).Count();
        ImGui.TextDisabled(shown == total ? $"{total} beasts" : $"{shown} of {total}");

        // Statuses in the game's own display order, so the row reads like the window it came from.
        ImGui.TextUnformatted("Inflicts:");
        foreach (var status in BeastStatusNames.DisplayOrder)
            Toggle(status.Label(), filter.Statuses.Contains(status), () => Flip(filter.Statuses, status));

        ImGui.NewLine();
        ImGui.TextUnformatted("Also does:");
        foreach (var trait in Enum.GetValues<BeastTrait>().Where(t => t != BeastTrait.None))
            Toggle(Spaced(trait.ToString()), filter.Traits.Contains(trait), () => Flip(filter.Traits, trait));

        ImGui.NewLine();
        ImGui.TextUnformatted("Classification:");
        foreach (var group in catalog.Beasts.GroupBy(b => b.Classification).OrderBy(g => g.Key))
        {
            Toggle(group.First().ClassificationName,
                   filter.Classifications.Contains(group.Key),
                   () => Flip(filter.Classifications, group.Key));
        }

        ImGui.NewLine();
    }

    /// <summary>
    /// Toggles wrap on their own, because eleven statuses plus fifteen traits will not fit a line
    /// at any window width worth using.
    /// </summary>
    private static void Toggle(string label, bool active, Action onClick)
    {
        var width = ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.GetContentRegionAvail().X < width)
            ImGui.NewLine();
        else
            ImGui.SameLine();

        using var colour = ImRaii.PushColor(ImGuiCol.Button,
                                            ImGui.GetColorU32(ImGuiCol.ButtonActive), active);

        if (ImGui.SmallButton(label))
            onClick();
    }

    private static void Flip<T>(System.Collections.Generic.HashSet<T> set, T value)
    {
        if (!set.Add(value))
            set.Remove(value);
    }

    private static string Spaced(string pascalCase) =>
        string.Concat(pascalCase.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

    private void DrawTable()
    {
        using var child = ImRaii.Child("##beastBody", Vector2.Zero, true);
        if (!child.Success)
            return;

        using var table = ImRaii.Table("##beasts", 6,
                                       ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                       ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("No.", ImGuiTableColumnFlags.WidthFixed, 34f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 26f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Beast", ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Kin", ImGuiTableColumnFlags.WidthFixed, 80f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Inflicts", ImGuiTableColumnFlags.WidthFixed, 190f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Actions");
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var beast in filter.Apply(catalog.Beasts))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextDisabled(beast.Number.ToString());

            ImGui.TableNextColumn();
            Widgets.Icon(beast.IconId, 20f);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(beast.Name);

            ImGui.TableNextColumn();
            ImGui.TextDisabled(beast.ClassificationName);

            ImGui.TableNextColumn();
            ImGui.TextWrapped(beast.Statuses.Count == 0
                                  ? "—"
                                  : string.Join(", ", BeastStatusNames.DisplayOrder
                                                                     .Where(beast.Inflicts)
                                                                     .Select(s => s.Label())));

            ImGui.TableNextColumn();
            foreach (var action in beast.Actions)
            {
                ImGui.TextUnformatted($"{action.Name}");
                if (!ImGui.IsItemHovered())
                    continue;

                using var tooltip = ImRaii.Tooltip();
                ImGui.TextUnformatted($"{action.Slot}");
                if (action.Description.Length > 0)
                    ImGui.TextWrapped(action.Description);
                if (action.Damage != DamageType.None)
                    ImGui.TextDisabled($"{action.Damage}");
                if (action.Traits != BeastTrait.None)
                    ImGui.TextDisabled(Spaced(action.Traits.ToString()).Replace(" ,", ","));
            }
        }
    }

    public void Dispose() { }
}
