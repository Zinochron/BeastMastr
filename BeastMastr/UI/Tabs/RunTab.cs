using System;
using System.Collections.Generic;
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
/// Playing a board on its own: start and hold it, what it does in the rooms and in fights, when it lets
/// you take over, and the route. What it is built from — the recorder, the ground scan, the fight's
/// insides — is on the Debug tab.
/// </summary>
public sealed class RunTab : ITab
{
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(0.95f, 0.45f, 0.35f, 1f);
    private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Attention = new(1f, 0.85f, 0.3f, 1f);

    private readonly Configuration configuration;
    private readonly BoardModel board;
    private readonly RouteKeeper route;
    private readonly Automation.Run.BoardRunner runner;
    private readonly LootTracker loot;

    /// <summary>What the last "switch on" press answered, when it was not simply done.</summary>
    private string pluginMessage = string.Empty;

    public RunTab(Configuration configuration, BoardModel board, RouteKeeper route, Automation.Run.BoardRunner runner,
                  LootTracker loot)
    {
        this.configuration = configuration;
        this.board = board;
        this.route = route;
        this.runner = runner;
        this.loot = loot;
    }

    public string Title => "Run";
    public string Id => "run";

    public void Draw()
    {
        var canRun = DrawPlugins();
        ImGuiHelpers.ScaledDummy(2f);
        DrawRun(canRun);
        ImGuiHelpers.ScaledDummy(4f);
        DrawLoot();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRooms();
        ImGuiHelpers.ScaledDummy(4f);
        DrawFighting();
        ImGuiHelpers.ScaledDummy(4f);
        DrawTakingOver();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRoute();
    }

    /// <summary>
    /// What the run needs from other plugins, checked every frame: vnavmesh always, BossMod only while a
    /// BossMod role is chosen. One that is installed but switched off gets a button that switches it on.
    /// Returns whether the run can start — only vnavmesh decides that, since without BossMod the run
    /// falls back to dodging itself.
    /// </summary>
    private bool DrawPlugins()
    {
        var navmesh = PluginPresence.Check(NavmeshIpc.InternalName, "vnavmesh");
        DrawPlugin(navmesh, Bad, "needed to walk the board");

        if (navmesh.State == PluginPresence.State.Loaded)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, NavmeshIpc.IsReady() ? "mesh ready"
                                     : NavmeshIpc.BuildProgress() is var progress and >= 0 ? $"building mesh {progress:P0}"
                                     : "no mesh for this zone yet");
        }

        if (configuration.BossModRole != BossModRole.Off)
            DrawPlugin(PluginPresence.Check(BossModIpc.InternalName, "BossMod"), Attention,
                       "without it BeastMastr dodges itself");

        if (pluginMessage.Length > 0)
            ImGui.TextColored(Muted, pluginMessage);

        return navmesh.State == PluginPresence.State.Loaded;
    }

    private void DrawPlugin(PluginPresence.Status plugin, Vector4 problem, string why)
    {
        if (plugin.State == PluginPresence.State.Loaded)
        {
            ImGui.TextColored(Good, $"{plugin.Name} {plugin.Version}");
            return;
        }

        var what = plugin.State switch
        {
            PluginPresence.State.Off => "is switched off",
            PluginPresence.State.Outdated => "needs an update",
            PluginPresence.State.Unusable => "cannot be loaded",
            _ => "is not installed",
        };

        ImGui.TextColored(problem, $"{plugin.Name} {what} — {why}.");
        ImGui.SameLine();

        if (plugin.State == PluginPresence.State.Off)
        {
            if (ImGui.SmallButton($"Switch on###on{plugin.Name}"))
                pluginMessage = PluginPresence.Enable(plugin.Name) ? string.Empty : "Dalamud did not take /xlenableplugin.";
        }
        else if (ImGui.SmallButton($"Plugin installer###find{plugin.Name}"))
        {
            PluginPresence.ShowInInstaller(plugin.Name);
        }
    }

    /// <summary>The whole run: start it, hold it, and see what it is doing or waiting for.</summary>
    private void DrawRun(bool canRun)
    {
        if (!ImGui.CollapsingHeader("Run the board", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using (ImRaii.Disabled(runner.Running || !canRun))
        {
            if (ImGui.Button("Run"))
                runner.Start(configuration.RunCount);
        }

        Widgets.HelpMarker("On a board's start platform, or anywhere in Central Shroud to start through Lauda. " +
                           "Your own input pauses it. Same as /beastmastr run.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!runner.Running))
        {
            if (ImGui.Button(runner.Paused ? "Carry on" : "Pause"))
                runner.TogglePause();

            ImGui.SameLine();
            if (ImGui.Button("Continue"))
                runner.Continue();

            Widgets.HelpMarker("Marks the current room as done, for when the run is stuck on it.");

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
                runner.Stop("Stopped from the Run tab.");
        }

        var runs = configuration.RunCount;
        ImGui.SetNextItemWidth(90f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("boards", ref runs))
        {
            configuration.RunCount = Math.Clamp(runs, 1, 99);
            configuration.Save();
        }

        Widgets.HelpMarker("Boards in a row. Can be changed mid-run.");

        ImGui.SameLine();
        Toggle("go on after a loss", configuration.ContinueAfterLostBoard,
               value => configuration.ContinueAfterLostBoard = value);
        Widgets.HelpMarker("A wiped board counts as played.");

        ImGui.SameLine();
        Toggle("auto-repair", configuration.RepairBetweenBoards,
               value => configuration.RepairBetweenBoards = value);
        Widgets.HelpMarker("Repairs worn gear at the entrance between boards. Without dark matter or the crafter " +
                           "level it waits for you.");

        DrawBoardChoice();

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("team for each board", TeamName(configuration.RunTeam)))
        {
            if (combo.Success)
            {
                foreach (var mode in Enum.GetValues<RunTeam>())
                {
                    if (ImGui.Selectable(TeamName(mode), mode == configuration.RunTeam))
                    {
                        configuration.RunTeam = mode;
                        configuration.Save();
                    }
                }
            }
        }

        Widgets.HelpMarker("Set before each board:\n" +
                           "Farming: carries only, for the board's bonus.\n" +
                           "Leveling: carries, then the least advanced.\n" +
                           "Keep: as it is.");

        if (configuration.RunTeam != RunTeam.Keep && configuration.CarryBeasts.Count == 0)
            ImGui.TextColored(Attention, "No carries marked (bestiary: right-click → Add as carry).");

        var failed = runner.State == Automation.Run.BoardRunner.Phase.Failed;
        ImGui.TextColored(failed ? Bad : runner.Running ? Good : Muted,
                          $"{runner.State}{(runner.Paused ? " (paused)" : string.Empty)}: {runner.Status}");

        if (runner.RunsWanted > 0)
            ImGui.TextDisabled($"Board {Math.Min(runner.RunsDone + 1, runner.RunsWanted)} of {runner.RunsWanted}.");

        if (runner.HandOff.Length > 0)
            ImGui.TextColored(Attention, $"Your turn: {runner.HandOff}");

        using var node = ImRaii.TreeNode("What happened");
        if (!node.Success)
            return;

        foreach (var line in runner.Log)
            ImGui.TextDisabled(line);
    }

    /// <summary>
    /// Which board is played and on what Crucible mode. Both are used when a board is started from the
    /// entrance: the board is picked in Lauda's list, the mode is set in the board window before it is
    /// challenged.
    /// </summary>
    private void DrawBoardChoice()
    {
        var boards = BoardSheets.Boards();
        var chosen = configuration.RunBoardRow;
        var label = chosen == 0
                        ? configuration.LastBoardRowId == 0
                              ? "the one last played"
                              : $"the one last played ({BoardSheets.Name(configuration.LastBoardRowId)})"
                        : BoardSheets.Name(chosen);

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("board", label))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("the one last played", chosen == 0))
                {
                    configuration.RunBoardRow = 0;
                    configuration.Save();
                }

                foreach (var (row, name) in boards)
                {
                    if (ImGui.Selectable(name, row == chosen))
                    {
                        configuration.RunBoardRow = row;
                        configuration.Save();
                    }
                }
            }
        }

        Widgets.HelpMarker("The board asked of Lauda. \"The one last played\": whichever board window was open last.");

        ImGui.SameLine();

        var modes = CrucibleModeReader.Names();
        var mode = configuration.RunDifficulty;
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("difficulty", mode >= 0 && mode < modes.Count ? modes[mode] : "leave as it is"))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("leave as it is", mode < 0))
                {
                    configuration.RunDifficulty = -1;
                    configuration.Save();
                }

                for (var i = 0; i < modes.Count; i++)
                {
                    if (ImGui.Selectable(modes[i], i == mode))
                    {
                        configuration.RunDifficulty = i;
                        configuration.LastCrucibleMode = i;
                        configuration.Save();
                    }
                }
            }
        }

        Widgets.HelpMarker("Set before each challenge; the game resets it every visit. Unlocks once every board is cleared.");
    }

    /// <summary>Boards finished and the loot rolled for at their end, this session and in all.</summary>
    private void DrawLoot()
    {
        if (!ImGui.CollapsingHeader("Boards and loot", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextUnformatted($"Boards finished: {loot.BoardsThisSession} this session ({loot.WonThisSession} won), " +
                              $"{configuration.BoardsFinished} in all ({configuration.BoardsWon} won).");
        Widgets.HelpMarker("Counted at each result window. Loot is the end-of-board roll, not room spoils.");

        // The speed: how long a board takes, entering to result, and how many an hour with the way back in.
        if (loot.BoardStartedAt is { } started)
            ImGui.TextDisabled($"This board: {Clock(DateTime.Now - started)}.");

        if (loot.BoardTimes.Count > 0)
        {
            var average = TimeSpan.FromSeconds(loot.BoardTimes.Average(time => time.TotalSeconds));
            var fastest = loot.BoardTimes.Min();
            var line = $"Average board this session: {Clock(average)} over {loot.BoardTimes.Count} " +
                       $"(fastest {Clock(fastest)}, last {Clock(loot.BoardTimes[^1])})";

            if (loot.SessionStartedAt is { } first && loot.LastFinishedAt is { } last && last > first)
                line += $", {loot.BoardsThisSession / (last - first).TotalHours:0.0} boards an hour with the way in";

            ImGui.TextUnformatted(line + ".");
        }

        if (configuration.TimedBoards > 0)
            ImGui.TextDisabled($"Average board in all: {Clock(TimeSpan.FromSeconds(configuration.TimedBoardSeconds / configuration.TimedBoards))} " +
                               $"over {configuration.TimedBoards}.");

        if (configuration.LootTotals.Count == 0)
        {
            ImGui.TextDisabled("No loot counted yet.");
            return;
        }

        using (var table = ImRaii.Table("##loot", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("This session", ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("In all", ImGuiTableColumnFlags.WidthFixed, 70f * ImGuiHelpers.GlobalScale);
                ImGui.TableHeadersRow();

                foreach (var (name, total) in configuration.LootTotals.OrderBy(pair => pair.Key))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(loot.ThisSession.GetValueOrDefault(name).ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(total.ToString());
                }
            }
        }

        if (ImGui.SmallButton("Reset the count") && ImGui.GetIO().KeyCtrl)
            loot.Reset();

        Widgets.HelpMarker("Ctrl+click. Clears the saved totals too.");
    }

    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    /// <summary>What the run does in the rooms that offer a choice.</summary>
    private void DrawRooms()
    {
        if (!ImGui.CollapsingHeader("In the rooms"))
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

        Widgets.HelpMarker("Four offers, left to right; an empty slot falls back to the first. Held Beast Gear is " +
                           "skipped. Fight spoils are always taken.");

        var healBelow = configuration.TreasureHealBelow * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Coffer: healing item instead at or below", ref healBelow, 0f, 100f, "%.0f%% HP"))
        {
            configuration.TreasureHealBelow = healBelow / 100f;
            configuration.Save();
        }

        Toggle("Let me shop myself", configuration.ShopByHand, value => configuration.ShopByHand = value);

        using (ImRaii.Disabled(configuration.ShopByHand))
        {
            Toggle("Buy Beast Gear in shops", configuration.ShopBuysGear, value => configuration.ShopBuysGear = value);
            Widgets.HelpMarker("Dearest affordable piece first, never one already held.");
            Toggle("…then healing items with the rest", configuration.ShopBuysPotions,
                   value => configuration.ShopBuysPotions = value);
        }

        Toggle("Campsite: rest the most hurt familiars too",
               configuration.CampsiteRestFamiliars, value => configuration.CampsiteRestFamiliars = value);

        using (ImRaii.Disabled(!configuration.CampsiteRestFamiliars))
        {
            Toggle("…only as many as heal the most", configuration.CampsiteAvoidOverheal,
                   value => configuration.CampsiteAvoidOverheal = value);
        }

        Widgets.HelpMarker("The 90% is shared: alone 90%, with one familiar 45% each, with two 30%. Familiars are " +
                           "only added while that heals more in total.");

        Toggle("Drink Beast Potions and Crucible Ash", configuration.UsePotions, value => configuration.UsePotions = value);
        if (configuration.UsePotions)
        {
            ImGui.Indent();
            var inFight = configuration.PotionInFightBelow * 100f;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("In a fight, the strongest at or below", ref inFight, 10f, 80f, "%.0f%% HP"))
            {
                configuration.PotionInFightBelow = inFight / 100f;
                configuration.Save();
            }

            var onBoard = configuration.PotionOnBoardBelow * 100f;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("On the board, up to", ref onBoard, 10f, 100f, "%.0f%% HP"))
            {
                configuration.PotionOnBoardBelow = onBoard / 100f;
                configuration.Save();
            }

            ImGui.Unindent();
        }

        Toggle("Boss fight: use every useful item once the horns are out", configuration.UseBossItems,
               value => configuration.UseBossItems = value);
        Widgets.HelpMarker("Each useful item once, Beast Potion Kit first; antidotes when poisoned; Fangs and " +
                           "Celestial Sand at the boss. Never: feral potions, smokebombs, spellforge and steelsting " +
                           "tomes, temporal sand, the eyes.");

        Toggle("Throw Fangs and Celestial Sand at adds", configuration.UseAreaItems,
               value => configuration.UseAreaItems = value);
        if (configuration.UseAreaItems)
        {
            ImGui.Indent();
            var adds = configuration.AreaItemAtAdds;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderInt("once this many adds attack you or a familiar", ref adds, 1, 8))
            {
                configuration.AreaItemAtAdds = adds;
                configuration.Save();
            }

            var wait = configuration.AreaItemWaitSeconds;
            ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("…and have been for", ref wait, 0f, 5f, "%.1f s"))
            {
                configuration.AreaItemWaitSeconds = wait;
                configuration.Save();
            }

            Widgets.HelpMarker("Adds arriving together are caught by one throw.");

            ImGui.Unindent();
        }
    }

    private void SetTreasure(int pick)
    {
        configuration.TreasurePick = pick;
        configuration.Save();
    }

    /// <summary>The fight settings a player would want to change; the rest is on the Debug tab.</summary>
    private void DrawFighting()
    {
        if (!ImGui.CollapsingHeader("Fighting"))
            return;

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

            ImGui.Unindent();
        }

        Toggle("Close gaps with Shield Charge", configuration.UseShieldCharge,
               value => configuration.UseShieldCharge = value);

        DrawDutyActions();
    }

    private void DrawDutyActions()
    {
        Toggle("Decide who takes the hits with the duty actions", configuration.UseDutyActions,
               value => configuration.UseDutyActions = value);
        Widgets.HelpMarker("I, Challenge: you take the hits.\n" +
                           "II, Snarl: the familiar covers you for 45 s.\n" +
                           "Snarl on hits aimed at you or low HP; Challenge when the familiar runs low.");

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

        ImGui.Unindent();
    }

    private static string TeamName(RunTeam team) => team switch
    {
        RunTeam.Farming => "Farming (carries only)",
        RunTeam.Leveling => "Leveling (carries + lowest)",
        RunTeam.Keep => "Keep the team",
        _ => team.ToString(),
    };

    private static string TankName(DutyTank tank) => tank switch
    {
        DutyTank.Auto => "Whoever is hit, until low",
        DutyTank.Familiar => "The familiar (Snarl)",
        DutyTank.Player => "You (Challenge)",
        _ => tank.ToString(),
    };

    /// <summary>What counts as you taking over, and what happens then.</summary>
    private void DrawTakingOver()
    {
        if (!ImGui.CollapsingHeader("When you take over"))
            return;

        Toggle("Stop for good instead of pausing", configuration.AbortOnManualInput,
               value => configuration.AbortOnManualInput = value);

        var delay = configuration.ResumeDelaySeconds;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Carry on after", ref delay, 0.5f, 30f, "%.1f s without input"))
        {
            configuration.ResumeDelaySeconds = delay;
            configuration.Save();
        }

        Toggle("Ignore typing and keys used by plugin windows", configuration.IgnoreMenuInput,
               value => configuration.IgnoreMenuInput = value);

        ImGui.TextUnformatted("What counts as taking over:");
        Toggle("Moving (keys, autorun, left stick)", configuration.CountMovementInput,
               value => configuration.CountMovementInput = value);
        Toggle("Jumping", configuration.CountJumpInput, value => configuration.CountJumpInput = value);
        Toggle("Targeting (keys, or clicking something in the world)", configuration.CountTargetingInput,
               value => configuration.CountTargetingInput = value);
        Toggle("Pressing an action on a hotbar", configuration.CountActionInput,
               value => configuration.CountActionInput = value);

        var deadzone = configuration.StickDeadzone * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Stick deadzone", ref deadzone, 5f, 90f, "%.0f%%"))
        {
            configuration.StickDeadzone = deadzone / 100f;
            configuration.Save();
        }
    }

    private void DrawRoute()
    {
        if (!ImGui.CollapsingHeader("Route"))
            return;

        if (board.Graph is not { IsValid: true } graph)
        {
            ImGui.TextDisabled("The route shows once you are on a board.");
            DrawPreferences();
            return;
        }

        var plan = route.Plan;
        ImGui.TextUnformatted(plan.Events.Count == 0
                                  ? "No route."
                                  : $"Next: {BoardNames.Describe(configuration, board, graph, plan.Next)}. " +
                                    $"{plan.Events.Count} rooms to the boss.");

        foreach (var note in plan.Notes)
            ImGui.TextColored(Bad, note);

        Toggle("Mark the route on the board window", configuration.ShowRouteOnBoard,
               value => configuration.ShowRouteOnBoard = value);
        Widgets.HelpMarker("Arrows on the route, stars on your picks, + on fork rooms (click to pick).");

        Toggle("Show the next step on the board", configuration.ShowRouteInWorld,
               value => configuration.ShowRouteInWorld = value);
        Widgets.HelpMarker("Green ring: the next room. Red: its alternatives. The way there on the ground.");

        if (ImGui.SmallButton("Clear picks"))
            route.ClearChoices();

        Widgets.HelpMarker("A pick fixes its move; the rest follows the preferences. Pick again to undo.");

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

                var label = $"{(chosen ? "★ " : planned ? "→ " : "   ")}{BoardNames.Name(room.Kind)}" +
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
            ImGui.TextUnformatted($"{i + 1}. {BoardNames.Name(order[i])}");
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

        Toggle("Avoid elite rooms", configuration.AvoidElite, value => configuration.AvoidElite = value);
    }

    private void Swap(System.Collections.Generic.List<BoardRoomKind> order, int a, int b)
    {
        (order[a], order[b]) = (order[b], order[a]);
        configuration.RouteOrder = order.Select(kind => (int)kind).ToList();
        configuration.Save();
        route.Replan();
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
