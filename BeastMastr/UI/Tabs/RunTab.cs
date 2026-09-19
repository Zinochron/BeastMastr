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

    public RunTab(Configuration configuration, BoardModel board, RouteKeeper route, Automation.Run.BoardRunner runner)
    {
        this.configuration = configuration;
        this.board = board;
        this.route = route;
        this.runner = runner;
    }

    public string Title => "Run";
    public string Id => "run";

    public void Draw()
    {
        DrawRun();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRooms();
        ImGuiHelpers.ScaledDummy(4f);
        DrawFighting();
        ImGuiHelpers.ScaledDummy(4f);
        DrawTakingOver();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRoute();
    }

    /// <summary>The whole run: start it, hold it, and see what it is doing or waiting for.</summary>
    private void DrawRun()
    {
        if (!ImGui.CollapsingHeader("Run the board", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextWrapped("Stand on a Crucible board's start platform and press Run. BeastMastr walks from room to " +
                          "room, fights, shops, rests and takes treasure until the boss is done — and with more " +
                          "than one board, starts the next one from the entrance. Moving, jumping, targeting or " +
                          "pressing an action yourself pauses it; it carries on once you let go.");
        ImGuiHelpers.ScaledDummy(2f);

        if (!NavmeshIpc.IsLoaded)
            ImGui.TextColored(Bad, "vnavmesh is needed to walk the board. Install it from its plugin repository.");
        else if (!NavmeshIpc.IsReady())
            ImGui.TextColored(Muted, NavmeshIpc.BuildProgress() is var progress and >= 0
                                         ? $"vnavmesh is building the mesh: {progress:P0}."
                                         : "vnavmesh has no mesh for this zone yet.");

        using (ImRaii.Disabled(runner.Running))
        {
            if (ImGui.Button("Run"))
                runner.Start(configuration.RunCount);
        }

        Widgets.HelpMarker("Same as /beastmastr run. Steps the run cannot do itself are handed to you, and it " +
                           "carries on once they are done.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!runner.Running))
        {
            if (ImGui.Button(runner.Paused ? "Carry on" : "Pause"))
                runner.TogglePause();

            ImGui.SameLine();
            if (ImGui.Button("Continue"))
                runner.Continue();

            Widgets.HelpMarker("Tells the run the room in hand is finished, for when it waits on something it " +
                               "does not recognise.");

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

        Widgets.HelpMarker("How many boards to play in a row. Can be changed while a run is under way.");

        ImGui.SameLine();
        Toggle("go on after a lost board", configuration.ContinueAfterLostBoard,
               value => configuration.ContinueAfterLostBoard = value);
        Widgets.HelpMarker("When on, a board lost to a wipe counts as played and the next one is started.");

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

        Widgets.HelpMarker("A treasure coffer offers four items, left to right. The run takes a random piece of " +
                           "gear, the offer set here (the first one if that slot is empty), or hands the choice to you. " +
                           "Beast Gear already held is never taken twice. The spoils after a fight are always taken whole.");

        var healBelow = configuration.TreasureHealBelow * 100f;
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Coffers: a healing item instead of gear at or below", ref healBelow, 0f, 100f, "%.0f%% HP"))
        {
            configuration.TreasureHealBelow = healBelow / 100f;
            configuration.Save();
        }

        Toggle("Let me shop myself", configuration.ShopByHand, value => configuration.ShopByHand = value);

        using (ImRaii.Disabled(configuration.ShopByHand))
        {
            Toggle("Buy Beast Gear in shops", configuration.ShopBuysGear, value => configuration.ShopBuysGear = value);
            Widgets.HelpMarker("The dearest piece the tokens allow first, then the next, never a piece already held. " +
                               "Each purchase is only confirmed when the game's question names the piece meant.");
            Toggle("…then healing items with the tokens left", configuration.ShopBuysPotions,
                   value => configuration.ShopBuysPotions = value);
        }

        Toggle("Rest the most hurt familiars at a campsite (otherwise rest alone)",
               configuration.CampsiteRestFamiliars, value => configuration.CampsiteRestFamiliars = value);

        using (ImRaii.Disabled(!configuration.CampsiteRestFamiliars))
        {
            Toggle("…only as many as heal the most", configuration.CampsiteAvoidOverheal,
                   value => configuration.CampsiteAvoidOverheal = value);
        }

        Widgets.HelpMarker("A campsite's 90% is shared: alone you get 90%, with one familiar each gets 45%, with two " +
                           "30%. Whatever heals past full is lost, so familiars are only picked while that adds up " +
                           "to more HP restored in total.");

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
        Widgets.HelpMarker("Duty Action I, Challenge: you draw the enemy, and the familiar's cover ends.\n" +
                           "Duty Action II, Snarl: the familiar draws the enemy and takes every hit meant for you, " +
                           "for 45 seconds.\n" +
                           "Snarl goes out for a hit aimed at you and when your HP runs low. Challenge takes the hits " +
                           "back when the familiar runs low.");

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
        Widgets.HelpMarker("In the game's Board Layout window: an arrow on every room the route takes, a star on " +
                           "every room you picked, and a + in the corner of every room on a fork — click it to pick " +
                           "that room.");

        Toggle("Show the next step on the board", configuration.ShowRouteInWorld,
               value => configuration.ShowRouteInWorld = value);
        Widgets.HelpMarker("A green ring on the room the route takes next, red rings on the other rooms of that " +
                           "move, and the way there on the ground.");

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
