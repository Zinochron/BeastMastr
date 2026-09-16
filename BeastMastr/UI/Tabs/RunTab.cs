using System;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Ipc;
using BeastMastr.Rules;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// The board automation, from the ground up: what the plugins it leans on say, the board as a graph
/// placed in the world, the ground scanned under it, and the route through it.
/// </summary>
public sealed class RunTab : ITab
{
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(0.95f, 0.45f, 0.35f, 1f);
    private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);

    private readonly Configuration configuration;
    private readonly BoardModel board;
    private readonly BoardTerrain terrain;
    private readonly RouteKeeper route;
    private readonly Native.RouteOverlay overlay;
    private readonly RunRecorder recorder;

    private string imageResult = string.Empty;

    public RunTab(Configuration configuration, BoardModel board, BoardTerrain terrain, RouteKeeper route,
                  Native.RouteOverlay overlay, RunRecorder recorder)
    {
        this.configuration = configuration;
        this.board = board;
        this.terrain = terrain;
        this.route = route;
        this.overlay = overlay;
        this.recorder = recorder;
    }

    public string Title => "Run";
    public string Id => "run";

    public void Draw()
    {
        DrawHelpers();
        ImGuiHelpers.ScaledDummy(4f);
        DrawBoard();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRoute();
        ImGuiHelpers.ScaledDummy(4f);
        DrawMap();
    }

    private void DrawHelpers()
    {
        if (!ImGui.CollapsingHeader("Plugins and recording", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var nav = NavmeshIpc.IsLoaded;
        ImGui.TextColored(nav ? Good : Bad, nav ? $"vnavmesh {NavmeshIpc.Version}" : "vnavmesh is not loaded");
        if (nav)
        {
            ImGui.SameLine();
            var ready = NavmeshIpc.IsReady();
            var progress = NavmeshIpc.BuildProgress();
            ImGui.TextColored(ready ? Good : Muted,
                              ready ? "mesh ready" : progress >= 0 ? $"building {progress:P0}" : "no mesh");

            if (NavmeshIpc.LastError.Length > 0)
                ImGui.TextColored(Bad, NavmeshIpc.LastError);
        }

        var bossMod = PluginPresence.Version("BossMod");
        ImGui.TextColored(bossMod.Length > 0 ? Good : Muted,
                          bossMod.Length > 0 ? $"BossMod {bossMod}" : "BossMod is not loaded (optional)");

        if (ImGui.Button(recorder.Recording ? "Stop recording" : "Record a run"))
            recorder.Toggle();

        Widgets.HelpMarker(
            "Writes everything that happens to captures/run-*.txt: windows and their values, every click " +
            "the game sends, every action used, the gauge, objects and your position. Watches only. " +
            "Same as /beastmastr record.");

        if (recorder.Recording)
        {
            ImGui.SameLine();
            ImGui.TextColored(Bad, $"recording {recorder.Elapsed:hh\\:mm\\:ss}, {recorder.LinesWritten} lines");
        }
        else if (recorder.LastPath.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(recorder.LastPath);
        }
    }

    private void DrawBoard()
    {
        if (!ImGui.CollapsingHeader("Board and ground", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextWrapped(board.Status);

        if (board.Graph is { IsValid: false } broken)
            ImGui.TextColored(Bad, string.Join(" ", broken.Problems.Take(3)));

        if (board.Join is { IsComplete: false } join)
            ImGui.TextColored(Bad, string.Join(" ", join.Problems.Take(3)));

        if (board.TriggerPosition is { } trigger)
            ImGui.TextDisabled($"Room trigger at {trigger.X:0.0}/{trigger.Z:0.0} → event {board.TriggerEvent}");

        ImGui.TextDisabled(board.Window == null
                               ? "Board window closed."
                               : $"Board window: {board.Window.Addon}, grid {board.Window.GridSize}, " +
                                 $"{(board.Projection == null ? "not fitted" : board.Projection.KnowsWorld ? "fitted to the world" : "fitted to the screen")}");

        ImGuiHelpers.ScaledDummy(2f);

        using (ImRaii.Disabled(terrain.Busy))
        {
            if (ImGui.Button("Scan the ground"))
                terrain.RequestScan();
        }

        Widgets.HelpMarker(
            "Asks vnavmesh about the floor under every room, a half-yalm grid around the board, and every " +
            "link as a walk that keeps clear of the rooms it is not heading for. Moves nothing. Only inside " +
            "the run, once all rooms are placed. Kept per board.");

        if (terrain.Busy)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                terrain.Cancel();
        }

        ImGui.SameLine();
        if (ImGui.Button("Rebuild the mesh"))
            NavmeshIpc.Rebuild();

        Widgets.HelpMarker("Tells vnavmesh to build this zone's mesh again, for when rooms are reported off the mesh.");

        ImGui.SameLine();
        if (ImGui.Button("Save a mesh image"))
            imageResult = terrain.WriteMeshImage();

        if (imageResult.Length > 0)
            ImGui.TextDisabled(imageResult);

        if (terrain.Busy)
            ImGui.ProgressBar(terrain.Progress, new Vector2(-1f, 0f), terrain.Status);
        else
            ImGui.TextColored(terrain.Current is { AllSafe: true } ? Good : Muted, terrain.Status);

        if (terrain.Current != null && !terrain.IsCurrent && board.Join is { IsComplete: true })
            ImGui.TextColored(Bad, "The stored scan was taken with the rooms placed differently — scan again.");

        var radius = configuration.RoomTriggerRadius;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Keep this far from other rooms", ref radius, 0.5f, 4f, "%.1f y"))
        {
            configuration.RoomTriggerRadius = radius;
            configuration.Save();
        }

        Widgets.HelpMarker("How close to a room counts as stepping onto it. A walk keeps at least this far " +
                           "(plus half a yalm) from every room it is not heading for. Scan again after changing it.");
    }

    private void DrawRoute()
    {
        if (!ImGui.CollapsingHeader("Route", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (board.Graph is not { IsValid: true } graph)
        {
            ImGui.TextDisabled("No board to plan on yet.");
            return;
        }

        var plan = route.Plan;
        ImGui.TextUnformatted(plan.Events.Count == 0
                                  ? "No route."
                                  : $"Next: {Describe(graph, plan.Next)}. {plan.Events.Count} rooms to the boss.");

        foreach (var note in plan.Notes)
            ImGui.TextColored(Bad, note);

        ImGui.TextDisabled($"Most hurt familiar seen: {route.LowestFamiliarHpShare:P0}. " +
                           $"Campsites go first below {configuration.CampsiteBelowHpShare:P0}.");

        var mark = configuration.ShowRouteOnBoard;
        if (ImGui.Checkbox("Mark the route on the board window", ref mark))
        {
            configuration.ShowRouteOnBoard = mark;
            configuration.Save();
        }

        Widgets.HelpMarker("Puts an arrow on every room the route takes and a star on every room you picked, " +
                           "and a pin in the corner of every room on a fork: click the pin to pick that room.");

        ImGui.SameLine();
        ImGui.TextDisabled(overlay.Status);

        if (ImGui.SmallButton("Clear picks"))
            route.ClearChoices();

        Widgets.HelpMarker("Rooms you pick bind their move. Everything else follows the preferences below. " +
                           "Picking a room again takes the pick back.");

        DrawPreferences();

        using var table = ImRaii.Table("##route", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthFixed, 50f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rooms");

        for (var move = 1; move <= graph.LastMove; move++)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(move.ToString());
            ImGui.TableNextColumn();

            var rooms = graph.OnMove(move);
            foreach (var room in rooms)
            {
                if (room != rooms[0])
                    ImGui.SameLine();

                var planned = route.IsPlanned(room.EventIndex);
                var chosen = route.IsChosen(room.EventIndex);
                var current = room.EventIndex == board.CurrentEvent;

                var label = $"{(chosen ? "★ " : planned ? "→ " : "   ")}{Name(room.Kind)}" +
                            $"{(current ? " (here)" : string.Empty)}###room{room.EventIndex}";

                using var color = ImRaii.PushColor(ImGuiCol.Text, planned ? Good : Muted);
                if (rooms.Count > 1)
                {
                    if (ImGui.SmallButton(label))
                        route.Toggle(room.EventIndex);
                }
                else
                {
                    ImGui.TextUnformatted(label.Split("###")[0]);
                }
            }
        }
    }

    private void DrawPreferences()
    {
        using var node = ImRaii.TreeNode("Preferences");
        if (!node.Success)
            return;

        var order = configuration.BuildRoutePreferences().Order.ToList();
        for (var i = 0; i < order.Count; i++)
        {
            ImGui.TextUnformatted($"{i + 1}. {Name(order[i])}");
            ImGui.SameLine(160f * ImGuiHelpers.GlobalScale);

            using (ImRaii.Disabled(i == 0))
            {
                if (ImGui.SmallButton($"up###up{i}"))
                    Swap(order, i, i - 1);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(i == order.Count - 1))
            {
                if (ImGui.SmallButton($"down###down{i}"))
                    Swap(order, i, i + 1);
            }
        }

        var share = configuration.CampsiteBelowHpShare * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Campsite first below", ref share, 0f, 100f, "%.0f%% HP"))
        {
            configuration.CampsiteBelowHpShare = share / 100f;
            configuration.Save();
        }

        var avoid = configuration.AvoidElite;
        if (ImGui.Checkbox("Avoid elite rooms", ref avoid))
        {
            configuration.AvoidElite = avoid;
            configuration.Save();
        }
    }

    private void Swap(System.Collections.Generic.List<BoardRoomKind> order, int a, int b)
    {
        (order[a], order[b]) = (order[b], order[a]);
        configuration.RouteOrder = order.Select(kind => (int)kind).ToList();
        configuration.Save();
        route.Replan();
    }

    /// <summary>
    /// The board from above: the scanned ground, the rooms, the links as they would be walked, the
    /// route, and where the player and the room trigger stand. A room on a fork is picked by clicking it.
    /// </summary>
    private void DrawMap()
    {
        if (!ImGui.CollapsingHeader("Map", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (board.Graph is not { IsValid: true } graph)
        {
            ImGui.TextDisabled("No board yet.");
            return;
        }

        var centres = graph.Nodes.Select(node => (node, world: board.WorldOf(node.EventIndex)))
                           .Where(pair => pair.world != null)
                           .ToDictionary(pair => pair.node.EventIndex, pair => pair.world!.Value);

        if (centres.Count < 2)
        {
            ImGui.TextDisabled("The rooms are placed once you are in the run's zone.");
            return;
        }

        var grid = terrain.IsCurrent ? terrain.Current?.Grid : null;
        var minX = centres.Values.Min(c => c.X) - 5f;
        var maxX = centres.Values.Max(c => c.X) + 5f;
        var minZ = centres.Values.Min(c => c.Z) - 5f;
        var maxZ = centres.Values.Max(c => c.Z) + 5f;

        var scale = 7f * ImGuiHelpers.GlobalScale;
        var size = new Vector2((maxX - minX) * scale, (maxZ - minZ) * scale);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##map", size);
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        var mouse = ImGui.GetMousePos();

        Vector2 At(float x, float z) => origin + new Vector2((x - minX) * scale, (z - minZ) * scale);

        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF1A1A1A);

        if (grid != null)
        {
            for (var gz = 0; gz < grid.Depth; gz++)
            {
                for (var gx = 0; gx < grid.Width; gx++)
                {
                    if (float.IsNaN(grid.At(gx, gz)))
                        continue;

                    var corner = At(grid.OriginX + ((gx - 0.5f) * grid.Step), grid.OriginZ + ((gz - 0.5f) * grid.Step));
                    draw.AddRectFilled(corner, corner + new Vector2(grid.Step * scale), 0xFF3A3A3A);
                }
            }
        }

        foreach (var (from, to) in graph.Edges)
        {
            var check = terrain.IsCurrent ? terrain.Edge(from, to) : null;
            var onRoute = (route.IsPlanned(to) && (route.IsPlanned(from) || from == board.CurrentEvent ||
                                                  (board.CurrentEvent < 0 && from == graph.Start?.EventIndex)));
            var color = check == null ? 0xFF808080u : check.Safe ? 0xFF50C850u : 0xFF4040E0u;
            var thickness = onRoute ? 3f : 1f;

            if (check is { Path.Count: > 1 })
            {
                for (var i = 1; i < check.Path.Count; i++)
                    draw.AddLine(At(check.Path[i - 1].X, check.Path[i - 1].Z), At(check.Path[i].X, check.Path[i].Z),
                                 color, thickness);
            }
            else if (centres.TryGetValue(from, out var a) && centres.TryGetValue(to, out var b))
            {
                draw.AddLine(At(a.X, a.Z), At(b.X, b.Z), color, thickness);
            }
        }

        var radius = configuration.RoomTriggerRadius * scale;
        foreach (var (eventIndex, centre) in centres)
        {
            var node = graph.Node(eventIndex)!;
            var at = At(centre.X, centre.Z);
            var fill = KindColor(node.Kind);

            draw.AddCircle(at, radius, 0x60FFFFFF);
            draw.AddCircleFilled(at, 6f * ImGuiHelpers.GlobalScale, fill);

            if (route.IsChosen(eventIndex))
                draw.AddCircle(at, 9f * ImGuiHelpers.GlobalScale, 0xFF00D7FF, 0, 2f);
            else if (route.IsPlanned(eventIndex))
                draw.AddCircle(at, 9f * ImGuiHelpers.GlobalScale, 0xFF50C850, 0, 1.5f);

            if (eventIndex == board.CurrentEvent)
                draw.AddCircle(at, 12f * ImGuiHelpers.GlobalScale, 0xFFFFFFFF, 0, 2f);

            draw.AddText(at + new Vector2(10f, -7f) * ImGuiHelpers.GlobalScale, 0xFFD0D0D0,
                         $"{eventIndex} {Name(node.Kind)}");

            if (hovered && Vector2.Distance(mouse, at) <= Math.Max(radius, 8f))
            {
                ImGui.SetTooltip($"Event {eventIndex}, move {node.Move}: {Describe(graph, eventIndex)}\n" +
                                 $"{centre.X:0.0}/{centre.Z:0.0}" +
                                 (graph.OnMove(node.Move).Count > 1 ? "\nClick to pick or unpick." : string.Empty));

                if (clicked && !node.IsStart && graph.OnMove(node.Move).Count > 1)
                    route.Toggle(eventIndex);
            }
        }

        if (board.TriggerPosition is { } trigger)
            draw.AddCircle(At(trigger.X, trigger.Z), 4f * ImGuiHelpers.GlobalScale, 0xFF00FFFF, 0, 2f);

        if (Services.Objects.LocalPlayer is { } player)
            draw.AddCircleFilled(At(player.Position.X, player.Position.Z), 4f * ImGuiHelpers.GlobalScale, 0xFFFFFFFF);

        ImGui.TextDisabled("Grey: scanned floor. Lines: links — green walkable, red unsafe, grey not scanned; " +
                           "thick is the route. Rings: star pick (yellow), planned (green), where the run stands (white).");
    }

    private string Describe(BoardGraph graph, int eventIndex)
    {
        if (graph.Node(eventIndex) is not { } node)
            return "nothing";

        var saved = board.BoardRowId == configuration.LastBoardRowId
                        ? configuration.LastBoard.Rooms.FirstOrDefault(room => room.Index == graph.RoomListIndex(eventIndex)
                                                                               && room.Move == node.Move
                                                                               && room.Kind == (int)node.Kind)
                        : null;

        return saved != null ? saved.Label : $"{Name(node.Kind)} on move {node.Move}";
    }

    private static string Name(BoardRoomKind kind) => kind switch
    {
        BoardRoomKind.Start => "Start",
        BoardRoomKind.Enemy => "Enemy",
        BoardRoomKind.EliteEnemy => "Elite",
        BoardRoomKind.Boss => "Boss",
        BoardRoomKind.Shop => "Shop",
        BoardRoomKind.Campsite => "Campsite",
        BoardRoomKind.Treasure => "Treasure",
        BoardRoomKind.RandomEnemyOrTreasure => "Random",
        _ => kind.ToString(),
    };

    /// <summary>ABGR, as the draw list wants it.</summary>
    private static uint KindColor(BoardRoomKind kind) => kind switch
    {
        BoardRoomKind.Start => 0xFFFFFFFF,
        BoardRoomKind.Enemy => 0xFF4050E0,
        BoardRoomKind.EliteEnemy => 0xFF2090F0,
        BoardRoomKind.Boss => 0xFFC040C0,
        BoardRoomKind.Shop => 0xFF40D0E0,
        BoardRoomKind.Campsite => 0xFF50C850,
        BoardRoomKind.Treasure => 0xFF30C0FF,
        _ => 0xFFA0A0A0,
    };

    public void Dispose() { }
}
