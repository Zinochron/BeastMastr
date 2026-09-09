using System;
using System.Linq;
using System.Numerics;
using System.Text;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// What the board readers currently see, and what is standing around in the world.
///
/// The first half says whether the overlay has anything to draw and whether tiles pair with rooms.
/// The second exists because the room markers in the Crucible overworld are not a window at all —
/// nothing in the HUD describes them — so identifying them means looking at the objects in the
/// world instead, and that has to be captured while a run is actually under way.
/// </summary>
public sealed class BoardTab : ITab
{
    private readonly BeastCatalog catalog;

    private string lastPath = string.Empty;
    private float radius = 60f;

    public BoardTab(BeastCatalog catalog)
    {
        this.catalog = catalog;
    }

    public string Title => "Board";
    public string Id => "board";

    public void Draw()
    {
        if (ImGui.Button("Save all of this to a file"))
            lastPath = CaptureStore.Save("board", Report()) ?? "could not be written — see the log";

        if (lastPath.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(lastPath);
        }

        ImGuiHelpers.ScaledDummy(4f);
        DrawOpenAddons();
        ImGuiHelpers.ScaledDummy(4f);
        DrawReaders();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRoster();
        ImGuiHelpers.ScaledDummy(4f);
        DrawMarkers();
        ImGuiHelpers.ScaledDummy(4f);
        DrawObjects();
    }

    /// <summary>
    /// Every window the game has loaded, not only the XBM ones. The Crucible's overworld markers
    /// are not objects — the object table holds nothing but the player out there — and they are not
    /// in the HUD, so if any window draws them it is one nobody has named yet, and it will be in
    /// here.
    /// </summary>
    private static void DrawOpenAddons()
    {
        if (!ImGui.CollapsingHeader("Every window open right now", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var open = AddonReader.OpenAddonNames(string.Empty);
        ImGui.TextDisabled($"{open.Count} loaded");

        foreach (var chunk in open.Chunk(6))
            ImGui.TextUnformatted(string.Join("   ", chunk));
    }

    private void DrawReaders()
    {
        if (!ImGui.CollapsingHeader("Board readers", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var mapOpen = StageMapReader.IsOpen;
        var detailOpen = StageDetailReader.IsOpen;

        ImGui.TextUnformatted($"XBMStageMap open: {mapOpen}");
        ImGui.TextUnformatted($"XBMStageDetailList open: {detailOpen}");

        var rooms = StageMapReader.Read();
        var details = StageDetailReader.Read();

        ImGui.TextUnformatted($"Tiles read: {rooms.Count}   Rooms described: {details.Count}");

        if (rooms.Count > 0 && details.Count > 0 && rooms.Any(r => r.DetailIndex >= details.Count))
        {
            ImGui.TextColored(new Vector4(0.95f, 0.6f, 0.3f, 1f),
                              "Some tiles point past the end of the room list — the pairing is wrong.");
        }

        foreach (var room in rooms)
        {
            var info = details.ElementAtOrDefault(room.DetailIndex);
            ImGui.TextUnformatted(
                $"  tile idx {room.DetailIndex,2}{(room.IsCurrent ? " (here)" : "")} " +
                $"@{room.ScreenPosition.X:0}/{room.ScreenPosition.Y:0} {room.Size.X:0}x{room.Size.Y:0}" +
                $"  ->  {(info == null ? "(no room at that index)" : $"move {info.Move} {info.Kind} {info.Label}")}");
        }

        foreach (var room in details)
            ImGui.TextDisabled($"  [{room.Index,2}] move {room.Move,2} {room.Kind,-22} {room.Label} — {room.Detail}");
    }

    /// <summary>
    /// Everything nearby, with the screen position the game would put it at. If the Crucible's room
    /// markers are world objects, they are in here — and whatever identifies them is what a card
    /// would have to anchor to.
    /// </summary>
    private void DrawObjects()
    {
        if (!ImGui.CollapsingHeader("Objects in the world", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("within", ref radius, 5f, 200f, "%.0f yalms");

        var player = Services.Objects.LocalPlayer;
        if (player == null)
        {
            ImGui.TextDisabled("No local player.");
            return;
        }

        using var table = ImRaii.Table("##objects", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Kind");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("DataId");
        ImGui.TableSetupColumn("Dist");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Screen");
        ImGui.TableHeadersRow();

        foreach (var obj in Services.Objects)
        {
            var distance = Vector3.Distance(obj.Position, player.Position);
            if (distance > radius)
                continue;

            var onScreen = Services.GameGui.WorldToScreen(obj.Position, out var screen);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(obj.ObjectKind.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(obj.Name.TextValue.Length > 0 ? obj.Name.TextValue : "(unnamed)");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(obj.DataId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{distance:0.0}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled($"{obj.Position.X:0}/{obj.Position.Y:0}/{obj.Position.Z:0}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(onScreen ? $"{screen.X:0}/{screen.Y:0}" : "off screen");
        }
    }

    /// <summary>
    /// The roster, with the progression rank that leveling mode has to sort on.
    /// </summary>
    private void DrawRoster()
    {
        if (!ImGui.CollapsingHeader("Roster", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var slots = PetPartyReader.Read(catalog);
        if (slots.Count == 0)
        {
            ImGui.TextDisabled("XBMPetParty is not open.");
            return;
        }

        foreach (var slot in slots)
        {
            ImGui.TextUnformatted(
                $"  {slot.Index,2}  {slot.Name,-16} rank {slot.Rank,-3} icon {slot.IconId}" +
                $"  {(slot.Beast == null ? "(not matched to a beast)" : $"-> No. {slot.Beast.Number} {slot.Beast.ClassificationName}")}");
        }
    }

    /// <summary>
    /// The map's markers, raw. The room icons float over the platforms and appear on the minimap,
    /// and they are neither objects nor a window — so if they are anywhere, they are here.
    /// </summary>
    private static void DrawMarkers()
    {
        if (!ImGui.CollapsingHeader("Map markers", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var player = Services.Objects.LocalPlayer;
        if (player != null)
            ImGui.TextDisabled($"player world {player.Position.X:0.0}/{player.Position.Y:0.0}/{player.Position.Z:0.0}");

        ImGui.TextWrapped(MapMarkerReader.DescribeTransform());

        var markers = MapMarkerReader.Read();
        if (markers.Count == 0)
        {
            ImGui.TextDisabled("None. If the room icons are visible right now, they are not map markers either.");
            return;
        }

        using var table = ImRaii.Table("##markers", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("From");
        ImGui.TableSetupColumn("Icon");
        ImGui.TableSetupColumn("Map X / Y");
        ImGui.TableSetupColumn("World / screen");
        ImGui.TableHeadersRow();

        foreach (var marker in markers)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled(marker.Kind?.ToString() ?? marker.Source);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(marker.IconId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{marker.MapX} / {marker.MapY}");
            ImGui.TableNextColumn();

            if (marker.World is not { } world)
            {
                ImGui.TextDisabled(marker.Subtext);
                continue;
            }

            var onScreen = Services.GameGui.WorldToScreen(world, out var screen);
            ImGui.TextUnformatted($"{world.X:0.0}/{world.Z:0.0}  ->  " +
                                  (onScreen ? $"{screen.X:0}/{screen.Y:0}" : "off screen"));
        }
    }

    private string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"# Board report {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"XBMStageMap open: {StageMapReader.IsOpen}");
        text.AppendLine($"XBMStageDetailList open: {StageDetailReader.IsOpen}");
        text.AppendLine();

        foreach (var room in StageMapReader.Read())
            text.AppendLine($"tile idx={room.DetailIndex} current={room.IsCurrent} " +
                            $"pos={room.ScreenPosition.X:0}/{room.ScreenPosition.Y:0} size={room.Size.X:0}x{room.Size.Y:0}");

        text.AppendLine();
        foreach (var room in StageDetailReader.Read())
            text.AppendLine($"room [{room.Index}] move={room.Move} kind={room.Kind} \"{room.Label}\" \"{room.Detail}\"");

        text.AppendLine();
        text.AppendLine("# Every window open right now");
        foreach (var name in AddonReader.OpenAddonNames(string.Empty))
            text.AppendLine(name);

        text.AppendLine();
        text.AppendLine("# Roster");
        foreach (var slot in PetPartyReader.Read(catalog))
            text.AppendLine($"{slot.Index}	{slot.Name}	rank={slot.Rank}	icon={slot.IconId}	" +
                            $"beast={(slot.Beast == null ? "?" : slot.Beast.Number.ToString())}");

        text.AppendLine();
        text.AppendLine("# Map markers");
        text.AppendLine(MapMarkerReader.DescribeTransform());
        var self = Services.Objects.LocalPlayer;
        if (self != null)
            text.AppendLine($"player world {self.Position.X:0.0}/{self.Position.Y:0.0}/{self.Position.Z:0.0}");

        foreach (var marker in MapMarkerReader.Read())
            text.AppendLine($"{marker.Source}	icon={marker.IconId}	kind={marker.Kind}	" +
                            $"map={marker.MapX}/{marker.MapY}	world={marker.World?.X:0.0}/{marker.World?.Z:0.0}	\"{marker.Subtext}\"");

        text.AppendLine();
        text.AppendLine("# Objects in the world");

        var player = Services.Objects.LocalPlayer;
        foreach (var obj in Services.Objects)
        {
            var distance = player == null ? 0f : Vector3.Distance(obj.Position, player.Position);
            if (player != null && distance > radius)
                continue;

            var onScreen = Services.GameGui.WorldToScreen(obj.Position, out var screen);
            text.AppendLine($"{obj.ObjectKind}\t{obj.Name.TextValue}\tdataId={obj.DataId}\tdist={distance:0.0}\t" +
                            $"world={obj.Position.X:0}/{obj.Position.Y:0}/{obj.Position.Z:0}\t" +
                            $"screen={(onScreen ? $"{screen.X:0}/{screen.Y:0}" : "off")}");
        }

        return text.ToString();
    }

    public void Dispose() { }
}
