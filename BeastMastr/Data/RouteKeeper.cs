using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// The route: which rooms were picked by hand, and the plan that follows from them.
///
/// Picks are stored per board, as event indices. Picking a room replaces any other pick on the same
/// move, and picking it again takes it back — the same toggle a pin on the board does. Everything not
/// picked is left to <see cref="RoutePlanner"/> and the preferences, planned again every look, so the
/// plan moves with the run and with the team's health.
/// </summary>
public sealed class RouteKeeper : IDisposable
{
    private const int Interval = 15;

    private readonly Configuration configuration;
    private readonly BeastCatalog catalog;
    private readonly BoardModel board;
    private readonly BoardTerrain terrain;
    private int ticks;

    public RouteKeeper(Configuration configuration, BeastCatalog catalog, BoardModel board, BoardTerrain terrain)
    {
        this.configuration = configuration;
        this.catalog = catalog;
        this.board = board;
        this.terrain = terrain;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>The plan from where the run stands, or from the start while that is not known.</summary>
    public RoutePlan Plan { get; private set; } = new([], new HashSet<int>(), []);

    /// <summary>
    /// The most hurt familiar's share of HP, as the team list last showed it. 1 until it has shown
    /// any, which leaves campsites to the preference order.
    /// </summary>
    public float LowestFamiliarHpShare { get; private set; } = 1f;

    public IReadOnlySet<int> Chosen =>
        configuration.ChosenRooms.TryGetValue(board.BoardRowId, out var picks) ? picks.ToHashSet() : new HashSet<int>();

    public bool IsChosen(int eventIndex) => Chosen.Contains(eventIndex);

    public bool IsPlanned(int eventIndex) => Plan.Events.Contains(eventIndex);

    /// <summary>Picks a room, or takes the pick back. Another pick on the same move is replaced.</summary>
    public void Toggle(int eventIndex)
    {
        if (board.Graph?.Node(eventIndex) is not { IsStart: false } node)
            return;

        if (!configuration.ChosenRooms.TryGetValue(board.BoardRowId, out var picks))
            configuration.ChosenRooms[board.BoardRowId] = picks = [];

        if (!picks.Remove(eventIndex))
        {
            picks.RemoveAll(other => board.Graph.Node(other)?.Move == node.Move);
            picks.Add(eventIndex);
        }

        configuration.Save();
        Replan();
    }

    public void ClearChoices()
    {
        if (configuration.ChosenRooms.Remove(board.BoardRowId))
            configuration.Save();

        Replan();
    }

    /// <summary>The room the run should enter next, or -1.</summary>
    public int NextEvent => Plan.Next;

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        try
        {
            WatchHealth();
            Replan();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Planning the route failed.");
        }
    }

    public void Replan()
    {
        if (board.Graph is not { IsValid: true } graph)
        {
            Plan = new RoutePlan([], new HashSet<int>(), [board.Status]);
            return;
        }

        var from = board.CurrentEvent >= 0 ? board.CurrentEvent : graph.Start?.EventIndex ?? 0;
        Plan = RoutePlanner.Plan(graph, from, Chosen, configuration.BuildRoutePreferences(), LowestFamiliarHpShare,
                                 terrain.UnsafeEdges);
    }

    /// <summary>
    /// Whenever the team list shows HP, remember the worst of it. A familiar at zero is left out: it is
    /// not hurt, it is out, and a campsite cannot bring it back.
    /// </summary>
    private void WatchHealth()
    {
        if (!PetPartyReader.IsOpen)
            return;

        var shares = PetPartyReader.Read(catalog)
                                   .Where(slot => slot.MaxHp > 0 && slot.Hp > 0)
                                   .Select(slot => slot.HealthShare)
                                   .ToList();

        if (shares.Count > 0)
            LowestFamiliarHpShare = shares.Min();
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
