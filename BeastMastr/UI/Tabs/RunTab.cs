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
    private readonly Automation.Run.BoardWalker walker;
    private readonly Automation.Run.ManualInputGuard input;
    private readonly Automation.Combat.CombatDriver combat;
    private readonly Automation.Combat.BossModBridge bossMod;
    private readonly BeastmasterJob job;
    private readonly Automation.Run.BoardRunner runner;

    private string presetResult = string.Empty;

    private string imageResult = string.Empty;

    public RunTab(Configuration configuration, BoardModel board, BoardTerrain terrain, RouteKeeper route,
                  Native.RouteOverlay overlay, RunRecorder recorder, Automation.Run.BoardWalker walker,
                  Automation.Run.ManualInputGuard input, Automation.Combat.CombatDriver combat,
                  Automation.Combat.BossModBridge bossMod, BeastmasterJob job,
                  Automation.Run.BoardRunner runner)
    {
        this.runner = runner;
        this.combat = combat;
        this.bossMod = bossMod;
        this.job = job;
        this.walker = walker;
        this.input = input;
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
        DrawRun();
        ImGuiHelpers.ScaledDummy(4f);
        DrawHelpers();
        ImGuiHelpers.ScaledDummy(4f);
        DrawControls();
        ImGuiHelpers.ScaledDummy(4f);
        DrawFighting();
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

    /// <summary>The whole run: start it, hold it, and see what it is doing or waiting for.</summary>
    private void DrawRun()
    {
        if (!ImGui.CollapsingHeader("Run the board", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using (ImRaii.Disabled(runner.Running))
        {
            if (ImGui.Button("Run"))
                runner.Start(configuration.RunCount);
        }

        Widgets.HelpMarker("Plays the board from where you stand to the boss: plans the route, walks one room at a " +
                           "time, calls familiars, fights, picks the most hurt at campsites. Steps whose buttons are " +
                           "not recorded yet are handed to you, and the run carries on once they are done. " +
                           "Same as /beastmastr run.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!runner.Running))
        {
            if (ImGui.Button(runner.Paused ? "Carry on" : "Pause"))
                runner.TogglePause();

            ImGui.SameLine();
            if (ImGui.Button("Continue"))
                runner.Continue();

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
                runner.Stop("Stopped from the Run tab.");
        }

        ImGui.SameLine();
        var runs = configuration.RunCount;
        ImGui.SetNextItemWidth(90f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("boards", ref runs))
        {
            configuration.RunCount = Math.Clamp(runs, 1, 99);
            configuration.Save();
        }

        DrawRoomChoices();

        var failed = runner.State == Automation.Run.BoardRunner.Phase.Failed;
        ImGui.TextColored(failed ? Bad : runner.Running ? Good : Muted,
                          $"{runner.State}{(runner.Paused ? " (paused)" : string.Empty)}: {runner.Status}");

        if (runner.RunsWanted > 0)
            ImGui.TextDisabled($"Board {Math.Min(runner.RunsDone + 1, runner.RunsWanted)} of {runner.RunsWanted}. " +
                               $"Last room done: {runner.DoneEvent}. Heading for: {runner.Target}.");

        if (runner.HandOff.Length > 0)
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), $"Your turn: {runner.HandOff}");

        using var node = ImRaii.TreeNode("What happened");
        if (!node.Success)
            return;

        foreach (var line in runner.Log)
            ImGui.TextDisabled(line);
    }

    /// <summary>What the run does in the rooms that offer a choice.</summary>
    private void DrawRoomChoices()
    {
        using var node = ImRaii.TreeNode("In the rooms");
        if (!node.Success)
            return;

        var pick = configuration.TreasurePick;
        var label = pick switch
        {
            Configuration.TreasureRandomGear => "Random gear",
            Configuration.TreasureByHand => "Let me choose",
            _ => $"Offer {pick + 1}",
        };

        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("Treasure", label))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("Random gear", pick == Configuration.TreasureRandomGear))
                    SetTreasure(Configuration.TreasureRandomGear);

                if (ImGui.Selectable("Let me choose", pick == Configuration.TreasureByHand))
                    SetTreasure(Configuration.TreasureByHand);

                for (var offer = 0; offer < XbmColumns.RunWindows.TreasureOffers; offer++)
                {
                    if (ImGui.Selectable($"Offer {offer + 1}", pick == offer))
                        SetTreasure(offer);
                }
            }
        }

        Widgets.HelpMarker("A treasure coffer offers four items, left to right. The run takes a random piece of " +
                           "gear, the offer set here (the first one if that slot is empty), or hands the choice to you. " +
                           "The spoils after a fight are always taken whole.");

        Toggle("Let me shop myself", configuration.ShopByHand,
               value => configuration.ShopByHand = value);

        using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(configuration.ShopByHand))
        {
            Toggle("Buy Beast Gear in shops", configuration.ShopBuysGear,
                   value => configuration.ShopBuysGear = value);
        }

        Widgets.HelpMarker("The dearest piece the tokens allow first, then the next, never a piece already held. " +
                           "Each purchase is only confirmed when the game's question names the piece meant.");
        Toggle("Rest the most hurt familiars at a campsite (otherwise rest alone)",
               configuration.CampsiteRestFamiliars, value => configuration.CampsiteRestFamiliars = value);
    }

    private void SetTreasure(int pick)
    {
        configuration.TreasurePick = pick;
        configuration.Save();
    }

    /// <summary>What can be set going from here, and how it stops.</summary>
    private void DrawControls()
    {
        if (!ImGui.CollapsingHeader("Walking", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using (ImRaii.Disabled(walker.Busy || route.NextEvent < 0))
        {
            if (ImGui.Button("Walk to the next room"))
                walker.Walk(route.NextEvent);
        }

        Widgets.HelpMarker("Walks one room along the route and stops as the room starts. " +
                           "Same as /beastmastr step. Scans the ground first if it has not been.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!walker.Busy))
        {
            if (ImGui.Button("Stop"))
                walker.Stop(null);
        }

        ImGui.TextColored(walker.State == Automation.Run.BoardWalker.Phase.Failed ? Bad : Muted,
                          $"{walker.State}: {walker.Status}");

        ImGui.TextDisabled(input.LastInput.Length == 0
                               ? "No manual input seen."
                               : $"Last manual input: {input.LastInput}, {input.SecondsSinceInput:0.0} s ago.");

        using var node = ImRaii.TreeNode("When you take over");
        if (!node.Success)
            return;

        var abort = configuration.AbortOnManualInput;
        if (ImGui.Checkbox("Stop for good instead of pausing", ref abort))
        {
            configuration.AbortOnManualInput = abort;
            configuration.Save();
        }

        var delay = configuration.ResumeDelaySeconds;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Carry on after", ref delay, 0.5f, 30f, "%.1f s without input"))
        {
            configuration.ResumeDelaySeconds = delay;
            configuration.Save();
        }

        var menus = configuration.IgnoreMenuInput;
        if (ImGui.Checkbox("Ignore typing and keys used by plugin windows", ref menus))
        {
            configuration.IgnoreMenuInput = menus;
            configuration.Save();
        }

        ImGui.TextUnformatted("What counts as taking over:");

        var movement = configuration.CountMovementInput;
        if (ImGui.Checkbox("Moving (keys, autorun, left stick)", ref movement))
        {
            configuration.CountMovementInput = movement;
            configuration.Save();
        }

        var jump = configuration.CountJumpInput;
        if (ImGui.Checkbox("Jumping", ref jump))
        {
            configuration.CountJumpInput = jump;
            configuration.Save();
        }

        var targeting = configuration.CountTargetingInput;
        if (ImGui.Checkbox("Targeting (keys, or clicking something in the world)", ref targeting))
        {
            configuration.CountTargetingInput = targeting;
            configuration.Save();
        }

        var actions = configuration.CountActionInput;
        if (ImGui.Checkbox("Pressing an action on a hotbar", ref actions))
        {
            configuration.CountActionInput = actions;
            configuration.Save();
        }

        var deadzone = configuration.StickDeadzone * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Stick deadzone", ref deadzone, 5f, 90f, "%.0f%%"))
        {
            configuration.StickDeadzone = deadzone / 100f;
            configuration.Save();
        }
    }

    /// <summary>The rotation and who plays which part of it.</summary>
    private void DrawFighting()
    {
        if (!ImGui.CollapsingHeader("Fighting", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (ImGui.Button(combat.Enabled ? "Stop fighting" : "Fight"))
            combat.Toggle();

        Widgets.HelpMarker("Plays the fight: the three-step combo, the axes against the Heart your familiar's " +
                           "Trick leaves, Tempered Release, Borrow, Rally and the rest. Switched on by hand it only " +
                           "acts once a fight is under way. Same as /beastmastr combat.");

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

        using var node = ImRaii.TreeNode("How to fight");
        if (!node.Success)
            return;

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
                           "Dodging only: BossMod moves — out of AoEs and into range — and BeastMastr presses everything.\n" +
                           "Dodging and rotation: BossMod also presses the combo; BeastMastr keeps the resources.\n" +
                           "Whatever BossMod had active is put back after each fight.");

        ImGui.SameLine();
        ImGui.TextDisabled(BossModIpc.IsLoaded ? bossMod.Status : "BossMod is not loaded, so this is Off.");

        var dodge = configuration.BossModDodgePreset;
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("Dodging preset", ref dodge, 64))
        {
            configuration.BossModDodgePreset = dodge;
            configuration.Save();
        }

        var full = configuration.BossModFullPreset;
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("Dodging and rotation preset", ref full, 64))
        {
            configuration.BossModFullPreset = full;
            configuration.Save();
        }

        using (ImRaii.Disabled(!BossModIpc.IsLoaded))
        {
            if (ImGui.Button("Write both presets to BossMod"))
                presetResult = bossMod.CreatePresets();
        }

        Widgets.HelpMarker("Creates or replaces the two presets in BossMod. They are also created on their own the " +
                           "first time a fight needs one. Edit them in BossMod afterwards if you like.");

        if (presetResult.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(presetResult);
        }

        Toggle("BeastMastr spends the resources while BossMod plays the combo",
               configuration.BeastMastrHandlesResources, value => configuration.BeastMastrHandlesResources = value);

        var spend = configuration.SpendTpAt;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Spend TP without a Heart from", ref spend, Bst.AxeMinimumTp, 255))
        {
            configuration.SpendTpAt = spend;
            configuration.Save();
        }

        Toggle("Summon familiars with the Battlehorns", configuration.UseBattlehorns,
               value => configuration.UseBattlehorns = value);
        Toggle("Send a spent familiar off with Parting Blow", configuration.UsePartingBlow,
               value => configuration.UsePartingBlow = value);

        using (ImRaii.Disabled(!configuration.UsePartingBlow))
        {
            ImGui.Indent();
            var within = configuration.PartingBlowHornWithin;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("…only if another Battlehorn is ready within", ref within, 0f, 30f, "%.0f s"))
            {
                configuration.PartingBlowHornWithin = within;
                configuration.Save();
            }

            var finisher = configuration.PartingBlowFinisherShare * 100f;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("…or the target is below", ref finisher, 0f, 50f, "%.0f%% HP"))
            {
                configuration.PartingBlowFinisherShare = finisher / 100f;
                configuration.Save();
            }

            Widgets.HelpMarker("The last familiar of a cycle is only sent off when the next one can follow soon, " +
                               "or when the blow will finish the target.");
            ImGui.Unindent();
        }
        Toggle("Close gaps with Shield Charge", configuration.UseShieldCharge,
               value => configuration.UseShieldCharge = value);
        DutyActionSettings();
        Toggle("With BossMod off, dodge with BeastMastr", configuration.DodgeWithBeastMastr,
               value => configuration.DodgeWithBeastMastr = value);
        Widgets.HelpMarker("Reads every enemy cast's shape from the game data, the way BossMod does, and steps " +
                           "out of what hits soonest — circles and rings in turn, as Bedrock Uplift needs — " +
                           "without leaving the arena's safe circle. Each dodge is written to the log.");

        if (configuration.DodgeWithBeastMastr)
        {
            var radius = configuration.ArenaSafeRadius;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("Dodge no further from the arena's middle than", ref radius, 10f, 20f, "%.1f y"))
            {
                configuration.ArenaSafeRadius = radius;
                configuration.Save();
            }

            Widgets.HelpMarker("Bleeding started about 20.5 yalms from the middle.");
        }

        Toggle("Walk into reach with vnavmesh", configuration.KeepRangeWithNavmesh,
               value => configuration.KeepRangeWithNavmesh = value);
        Widgets.HelpMarker("BossMod takes a Beastmaster for a ranged job and never walks in. While no enemy casts " +
                           "anything to dodge, BossMod's moving is held and vnavmesh walks in; any such cast hands " +
                           "the moving back to BossMod.");

        var half = configuration.ArenaHalfWidth;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("BossMod stays this close to the arena's middle", ref half, 8f, 20f, "%.1f y"))
        {
            configuration.ArenaHalfWidth = half;
            configuration.Save();
        }

        Widgets.HelpMarker("BossMod does not know where a Crucible arena ends; about 20 yalms from the middle, " +
                           "Bleeding stacks. It may move in a square of this half-width around the middle. " +
                           "Above 14 the square's corners reach past the safe circle.");
        if (!CrucibleArena.SquareIsSafe(half))
        {
            ImGui.SameLine();
            ImGui.TextColored(Bad, "Corners past the safe circle.");
        }
        Toggle("Let BossMod keep dodging while you have taken over", configuration.KeepBossModWhilePaused,
               value => configuration.KeepBossModWhilePaused = value);
    }

    private void DutyActionSettings()
    {
        Toggle("Decide who takes the hits with the duty actions", configuration.UseDutyActions,
               value => configuration.UseDutyActions = value);
        Widgets.HelpMarker("Duty Action I, Challenge: you draw the enemy, and the familiar's cover ends.\n" +
                           "Duty Action II, Snarl: the familiar draws the enemy and takes every hit meant for you, " +
                           "for 45 seconds.\n" +
                           "They share one 15 second recast. Snarl goes out for a hit no position avoids (one aimed at " +
                           "you, or one that fills the arena) and when your HP runs low. Challenge takes the hits back " +
                           "when the familiar runs low. Neither is pressed before the pull.");

        if (!configuration.UseDutyActions)
            return;

        ImGui.Indent();
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("Otherwise, the hits go to", TankName(configuration.DutyTank)))
        {
            if (combo.Success)
            {
                foreach (var tank in Enum.GetValues<DutyTank>())
                {
                    if (ImGui.Selectable(TankName(tank), tank == configuration.DutyTank))
                    {
                        configuration.DutyTank = tank;
                        configuration.Save();
                    }
                }
            }
        }

        var player = configuration.SnarlBelowPlayerHp * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Snarl when your HP is at or below", ref player, 10f, 90f, "%.0f%%"))
        {
            configuration.SnarlBelowPlayerHp = player / 100f;
            configuration.Save();
        }

        var familiar = configuration.ChallengeBelowFamiliarHp * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Challenge when the familiar's HP is at or below", ref familiar, 10f, 90f, "%.0f%%"))
        {
            configuration.ChallengeBelowFamiliarHp = familiar / 100f;
            configuration.Save();
        }

        Widgets.HelpMarker("Your HP does not come back on its own in the Crucible, and a familiar's carries on to " +
                           "the next room; the campsite heals both.");
        ImGui.Unindent();
    }

    private static string TankName(DutyTank tank) => tank switch
    {
        DutyTank.Auto => "Whoever is hit, until low",
        DutyTank.Familiar => "The familiar (Snarl)",
        DutyTank.Player => "You (Challenge)",
        _ => tank.ToString(),
    };

    private void Toggle(string label, bool value, Action<bool> set)
    {
        if (!ImGui.Checkbox(label, ref value))
            return;

        set(value);
        configuration.Save();
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

        Widgets.HelpMarker("In the game's Board Layout window: an arrow on every room the route takes, a star on " +
                           "every room you picked, and a + in the corner of every room on a fork — click it to pick " +
                           "that room.");

        var world = configuration.ShowRouteInWorld;
        if (ImGui.Checkbox("Show the next step on the board", ref world))
        {
            configuration.ShowRouteInWorld = world;
            configuration.Save();
        }

        Widgets.HelpMarker("On the board itself: a green ring on the room the route takes next, red rings on the " +
                           "other rooms of that move, and the way there on the ground. The ring turns yellow while " +
                           "walking. The map further down is this window's own; the game's map is not drawn on.");

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

        ImGui.TextDisabled("Light grey: platforms. Dark grey: the ground below them. " +
                           "Lines: links — green walkable, red unsafe, grey not scanned; " +
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
