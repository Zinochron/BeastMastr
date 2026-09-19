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
/// The board automation's insides, for testing it: the plugins it leans on, the run recorder, single
/// steps, the fight driver's decisions, BossMod and dodging internals, the ground scan and the map.
/// </summary>
public sealed class RunDebugTab : ITab
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
    private readonly Automation.Run.BoardWalker walker;
    private readonly Automation.Run.ManualInputGuard input;
    private readonly Automation.Combat.CombatDriver combat;
    private readonly Automation.Combat.BossModBridge bossMod;
    private readonly BeastmasterJob job;
    private readonly Automation.Run.BoardRunner runner;

    private string presetResult = string.Empty;
    private string imageResult = string.Empty;

    public RunDebugTab(Configuration configuration, BoardModel board, BoardTerrain terrain, RouteKeeper route,
                       Native.RouteOverlay overlay, RunRecorder recorder, Automation.Run.BoardWalker walker,
                       Automation.Run.ManualInputGuard input, Automation.Combat.CombatDriver combat,
                       Automation.Combat.BossModBridge bossMod, BeastmasterJob job, Automation.Run.BoardRunner runner)
    {
        this.configuration = configuration;
        this.board = board;
        this.terrain = terrain;
        this.route = route;
        this.overlay = overlay;
        this.recorder = recorder;
        this.walker = walker;
        this.input = input;
        this.combat = combat;
        this.bossMod = bossMod;
        this.job = job;
        this.runner = runner;
    }

    public string Title => "Run";
    public string Id => "debug-run";

    public void Draw()
    {
        DrawHelpers();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRunner();
        ImGuiHelpers.ScaledDummy(4f);
        DrawWalking();
        ImGuiHelpers.ScaledDummy(4f);
        DrawFightDriver();
        ImGuiHelpers.ScaledDummy(4f);
        DrawBoard();
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

        var bossModVersion = PluginPresence.Version("BossMod");
        ImGui.TextColored(bossModVersion.Length > 0 ? Good : Muted,
                          bossModVersion.Length > 0 ? $"BossMod {bossModVersion}" : "BossMod is not loaded (optional)");

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

    private void DrawRunner()
    {
        if (!ImGui.CollapsingHeader("Run state", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled($"{runner.State}: {runner.Status}");
        ImGui.TextDisabled($"Board {runner.RunsDone + 1} of {runner.RunsWanted}. Last room done: {runner.DoneEvent}. " +
                           $"Heading for: {runner.Target}.");
        ImGui.TextDisabled($"Route overlay: {overlay.Status}");
        ImGui.TextDisabled($"Most hurt familiar seen: {route.LowestFamiliarHpShare:P0}.");
    }

    private void DrawWalking()
    {
        if (!ImGui.CollapsingHeader("Walking", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using (ImRaii.Disabled(walker.Busy || route.NextEvent < 0))
        {
            if (ImGui.Button("Walk to the next room"))
                walker.Walk(route.NextEvent);
        }

        Widgets.HelpMarker("Walks one room along the route and stops as the room starts. Same as /beastmastr step.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!walker.Busy))
        {
            if (ImGui.Button("Stop walking"))
                walker.Stop(null);
        }

        ImGui.TextColored(walker.State == Automation.Run.BoardWalker.Phase.Failed ? Bad : Muted,
                          $"{walker.State}: {walker.Status}");

        ImGui.TextDisabled(input.LastInput.Length == 0
                               ? "No manual input seen."
                               : $"Last manual input: {input.LastInput}, {input.SecondsSinceInput:0.0} s ago.");
    }

    /// <summary>The fight driver on its own, what it decides, and who dodges.</summary>
    private void DrawFightDriver()
    {
        if (!ImGui.CollapsingHeader("Fighting", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (ImGui.Button(combat.Enabled ? "Stop fighting" : "Fight"))
            combat.Toggle();

        Widgets.HelpMarker("The fight driver without a run. Switched on by hand it only acts once a fight is under " +
                           "way. Same as /beastmastr combat.");

        ImGui.SameLine();
        ImGui.TextColored(combat.Enabled ? Good : Muted, combat.Status);

        if (combat.LastDecision.Length > 0)
            ImGui.TextDisabled($"Next: {combat.LastDecision}");

        if (combat.LastUsed.Length > 0)
            ImGui.TextDisabled($"Last pressed: {combat.LastUsed}");

        var gauge = GaugeReader.Read();
        ImGui.TextDisabled(gauge == null
                               ? "Not on Beastmaster."
                               : $"Gauge: TP {gauge.PlayerTp}, familiar TP {gauge.PetTp}, last familiar action " +
                                 $"{gauge.LastPetActionTp}, summoned {gauge.SummonedBeast}");

        foreach (var problem in job.Problems)
            ImGui.TextColored(Bad, problem);

        using var node = ImRaii.TreeNode("Dodging and BossMod");
        if (!node.Success)
            return;

        Toggle("With BossMod off, dodge with BeastMastr", configuration.DodgeWithBeastMastr,
               value => configuration.DodgeWithBeastMastr = value);

        if (configuration.DodgeWithBeastMastr)
        {
            var radius = configuration.ArenaSafeRadius;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("Dodge no further from the arena's middle than", ref radius, 10f, 20f, "%.1f y"))
            {
                configuration.ArenaSafeRadius = radius;
                configuration.Save();
            }
        }

        Toggle("Walk into reach with vnavmesh", configuration.KeepRangeWithNavmesh,
               value => configuration.KeepRangeWithNavmesh = value);

        var spend = configuration.SpendTpAt;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Spend TP without a Heart from", ref spend, Bst.AxeMinimumTp, 255))
        {
            configuration.SpendTpAt = spend;
            configuration.Save();
        }

        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("BossMod", RoleName(configuration.BossModRole)))
        {
            if (combo.Success)
            {
                foreach (var role in Enum.GetValues<BossModRole>())
                {
                    if (ImGui.Selectable(RoleName(role), role == configuration.BossModRole))
                    {
                        configuration.BossModRole = role;
                        configuration.Save();
                    }
                }
            }
        }

        Widgets.HelpMarker("Off: BeastMastr presses everything, dodges and walks into reach itself.\n" +
                           "Dodging only: BossMod moves, BeastMastr presses everything.\n" +
                           "Dodging and rotation: BossMod also presses the combo; BeastMastr keeps the resources.\n" +
                           "BossMod has no modules for the Crucible, so Off dodges better.");

        ImGui.SameLine();
        ImGui.TextDisabled(BossModIpc.IsLoaded ? bossMod.Status : "BossMod is not loaded, so this is Off.");

        var dodgePreset = configuration.BossModDodgePreset;
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("Dodging preset", ref dodgePreset, 64))
        {
            configuration.BossModDodgePreset = dodgePreset;
            configuration.Save();
        }

        var fullPreset = configuration.BossModFullPreset;
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("Dodging and rotation preset", ref fullPreset, 64))
        {
            configuration.BossModFullPreset = fullPreset;
            configuration.Save();
        }

        using (ImRaii.Disabled(!BossModIpc.IsLoaded))
        {
            if (ImGui.Button("Write both presets to BossMod"))
                presetResult = bossMod.CreatePresets();
        }

        if (presetResult.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(presetResult);
        }

        Toggle("BeastMastr spends the resources while BossMod plays the combo",
               configuration.BeastMastrHandlesResources, value => configuration.BeastMastrHandlesResources = value);

        var half = configuration.ArenaHalfWidth;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("BossMod stays this close to the arena's middle", ref half, 8f, 20f, "%.1f y"))
        {
            configuration.ArenaHalfWidth = half;
            configuration.Save();
        }

        if (!CrucibleArena.SquareIsSafe(half))
        {
            ImGui.SameLine();
            ImGui.TextColored(Bad, "Corners past the safe circle.");
        }

        Toggle("Let BossMod keep dodging while you have taken over", configuration.KeepBossModWhilePaused,
               value => configuration.KeepBossModWhilePaused = value);
    }

    private static string RoleName(BossModRole role) => role switch
    {
        BossModRole.Off => "Off (BeastMastr dodges)",
        BossModRole.DodgeOnly => "Dodging only",
        BossModRole.DodgeAndRotation => "Dodging and rotation",
        _ => role.ToString(),
    };

    private void DrawBoard()
    {
        if (!ImGui.CollapsingHeader("Board and ground"))
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

        Widgets.HelpMarker("Asks vnavmesh about the floor under every room and every link. Moves nothing. Kept per board.");

        if (terrain.Busy)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                terrain.Cancel();
        }

        ImGui.SameLine();
        if (ImGui.Button("Rebuild the mesh"))
            NavmeshIpc.Rebuild();

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
    }

    /// <summary>
    /// The board from above: the scanned ground, the rooms, the links as they would be walked, the
    /// route, and where the player and the room trigger stand. A room on a fork is picked by clicking it.
    /// </summary>
    private void DrawMap()
    {
        if (!ImGui.CollapsingHeader("Map"))
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
            // The platforms are what is walked on; the ground two and a half yalms below them is what
            // a step off a platform lands on. Told apart by height against the rooms' own floor.
            var floor = terrain.Current!.Rooms.Where(room => room.Floor != null)
                               .Select(room => room.Floor!.Value.Y)
                               .DefaultIfEmpty(0f)
                               .Average();

            for (var gz = 0; gz < grid.Depth; gz++)
            {
                for (var gx = 0; gx < grid.Width; gx++)
                {
                    var height = grid.At(gx, gz);
                    if (float.IsNaN(height))
                        continue;

                    var corner = At(grid.OriginX + ((gx - 0.5f) * grid.Step), grid.OriginZ + ((gz - 0.5f) * grid.Step));
                    var colour = MathF.Abs(height - floor) < 0.6f ? 0xFF5A5A5Au : 0xFF2A2A2Au;
                    draw.AddRectFilled(corner, corner + new Vector2(grid.Step * scale), colour);
                }
            }
        }

        foreach (var (from, to) in graph.Edges)
        {
            var check = terrain.IsCurrent ? terrain.Edge(from, to) : null;
            var onRoute = route.IsPlanned(to) && (route.IsPlanned(from) || from == board.CurrentEvent ||
                                                  (board.CurrentEvent < 0 && from == graph.Start?.EventIndex));
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

            draw.AddCircle(at, radius, 0x60FFFFFF);
            draw.AddCircleFilled(at, 6f * ImGuiHelpers.GlobalScale, BoardNames.Color(node.Kind));

            if (route.IsChosen(eventIndex))
                draw.AddCircle(at, 9f * ImGuiHelpers.GlobalScale, 0xFF00D7FF, 0, 2f);
            else if (route.IsPlanned(eventIndex))
                draw.AddCircle(at, 9f * ImGuiHelpers.GlobalScale, 0xFF50C850, 0, 1.5f);

            if (eventIndex == board.CurrentEvent)
                draw.AddCircle(at, 12f * ImGuiHelpers.GlobalScale, 0xFFFFFFFF, 0, 2f);

            draw.AddText(at + new Vector2(10f, -7f) * ImGuiHelpers.GlobalScale, 0xFFD0D0D0,
                         $"{eventIndex} {BoardNames.Name(node.Kind)}");

            if (hovered && Vector2.Distance(mouse, at) <= Math.Max(radius, 8f))
            {
                ImGui.SetTooltip($"Event {eventIndex}, move {node.Move}: " +
                                 $"{BoardNames.Describe(configuration, board, graph, eventIndex)}\n" +
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

        ImGui.TextDisabled("Light grey: platforms. Dark grey: the ground below them. " +
                           "Lines: links — green walkable, red unsafe, grey not scanned; " +
                           "thick is the route. Rings: star pick (yellow), planned (green), where the run stands (white).");
    }

    private void Toggle(string label, bool value, Action<bool> set)
    {
        if (!ImGui.Checkbox(label, ref value))
            return;

        set(value);
        configuration.Save();
    }

    public void Dispose() { }
}
