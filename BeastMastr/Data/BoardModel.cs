using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Rules;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// Everything known about the board being played, in one place: its graph, where each room is in the
/// world, where the run stands, and — while the board window is up — where each room is drawn.
///
/// Three sources, each answering what it can:
///
/// - The board window names the board (its row id) and marks the event the run is on. Only while it
///   is open, so both are remembered.
/// - The sheets turn the row id into the graph, anywhere.
/// - The map's room icons give each room a place in the world. They only exist in the run's zone.
///
/// Out in the run, with no window open, the room trigger object sitting on the current platform is a
/// second opinion on where the run stands.
/// </summary>
public sealed class BoardModel : IDisposable
{
    /// <summary>Frames between looks. The board changes when a room is entered, not per frame.</summary>
    private const int Interval = 15;

    /// <summary>How far from a room's centre the trigger object may sit and still name that room.</summary>
    private const float TriggerMatchRadius = 3f;

    /// <summary>
    /// Where the start sits relative to the first room, in world Z, when it has not been measured. The
    /// start has no icon; one capture had the player standing there 5 yalms short of move 1.
    /// </summary>
    private const float EstimatedStartOffset = 5f;

    private readonly Configuration configuration;
    private int ticks;
    private string markerSignature = string.Empty;
    private Vector3? measuredStart;

    public BoardModel(Configuration configuration)
    {
        this.configuration = configuration;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>The board's row, from the window or remembered. 0 when no board has been seen.</summary>
    public uint BoardRowId => configuration.LastBoardRowId;

    public BoardGraph? Graph { get; private set; }

    public BoardJoinResult? Join { get; private set; }

    /// <summary>The window's snapshot from the last look, or null while no board window is up.</summary>
    public BoardEventMapReader.Snapshot? Window { get; private set; }

    /// <summary>Grid, screen and world, while the board window is up.</summary>
    public PreviewProjection? Projection { get; private set; }

    /// <summary>The event the board window last marked, or -1. Kept while the run's zone is.</summary>
    public int MarkedEvent { get; private set; } = -1;

    public DateTime MarkedAt { get; private set; } = DateTime.MinValue;

    /// <summary>The room the trigger object sits on, or -1 when there is none nearby.</summary>
    public int TriggerEvent { get; private set; } = -1;

    public Vector3? TriggerPosition { get; private set; }

    /// <summary>
    /// Where the run stands: what the window marked, else the room the trigger sits on. -1 when
    /// neither has said anything.
    /// </summary>
    public int CurrentEvent => MarkedEvent >= 0 ? MarkedEvent : TriggerEvent;

    public bool InRunZone => Services.ClientState.TerritoryType == XbmColumns.Crucible.RunTerritory;

    public bool InRun => InRunZone || AddonReader.IsOpen(XbmColumns.ContentsMainHUD.Addon);

    /// <summary>What the model is working from, in words.</summary>
    public string Status { get; private set; } = "Nothing read yet.";

    /// <summary>
    /// A room's position in the world at the player's height, or null when its icon is not known. The
    /// start has no icon; it is placed from the measured start or estimated from the first room.
    /// </summary>
    public Vector3? WorldOf(int eventIndex)
    {
        var height = Services.Objects.LocalPlayer?.Position.Y ?? 0f;

        if (Join != null && Join.ByEvent.TryGetValue(eventIndex, out var icon))
            return new Vector3(icon.WorldX, height, icon.WorldZ);

        if (Graph?.Node(eventIndex) is not { IsStart: true })
            return null;

        if (measuredStart is { } start)
            return start;

        var first = Graph.Next(eventIndex).Select(node => WorldOf(node.EventIndex)).FirstOrDefault(p => p != null);
        var column = Join?.ByEvent.Where(pair => Graph.Node(pair.Key)?.Column == 0)
                         .Select(pair => pair.Value.WorldX)
                         .DefaultIfEmpty(float.NaN)
                         .Average() ?? float.NaN;

        if (first is not { } firstRoom)
            return null;

        return new Vector3(float.IsNaN(column) ? firstRoom.X : column, height, firstRoom.Z + EstimatedStartOffset);
    }

    public bool StartIsEstimated => measuredStart == null;

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            Status = $"Reading the board failed: {ex.Message}";
            Services.Log.Error(ex, "Reading the board failed.");
        }
    }

    private void Refresh()
    {
        if (!InRun)
        {
            MarkedEvent = -1;
            TriggerEvent = -1;
            TriggerPosition = null;
            measuredStart = null;
        }

        var window = BoardEventMapReader.Read();
        Window = window is { Loaded: true, Cells.Count: > 0 } ? window : null;

        if (Window != null && Window.BoardRowId != 0 && Window.BoardRowId != configuration.LastBoardRowId)
        {
            configuration.LastBoardRowId = Window.BoardRowId;
            configuration.Save();
            Join = null;
            markerSignature = string.Empty;
            measuredStart = null;
            Services.Log.Information($"Board {Window.BoardRowId} is the one being played.");
        }

        Graph = BuildGraph();

        if (Window is { MarksCurrent: true } && InRun)
        {
            if (Window.CurrentEventIndex != MarkedEvent)
                Services.Log.Information($"The board marks event {Window.CurrentEventIndex}.");

            MarkedEvent = Window.CurrentEventIndex;
            MarkedAt = DateTime.Now;

            if (MarkedEvent == 0 && Services.Objects.LocalPlayer is { } player)
                measuredStart ??= player.Position;
        }

        if (InRunZone && Graph != null)
            RefreshJoin(Graph);

        RefreshTrigger();
        Projection = Window != null && Graph != null ? Fit(Window) : null;
        Status = Describe();
    }

    /// <summary>The window's own cells when it is up — they are what is drawn — else the sheet's.</summary>
    private BoardGraph? BuildGraph()
    {
        var row = configuration.LastBoardRowId;
        if (row == 0)
            return null;

        if (Window != null && Window.BoardRowId == row)
        {
            var events = BoardSheets.Events(row);
            var live = BoardGraph.Build(Window.Cells.Select(cell => new BoardCell(cell.X, cell.Y, cell.Type,
                                                                                  cell.EventIndex,
                                                                                  cell.LinkedEventIndex)),
                                        events);
            if (live.IsValid)
                return live;
        }

        return BoardSheets.Graph(row);
    }

    private void RefreshJoin(BoardGraph graph)
    {
        var icons = MapMarkerReader.ReadRooms();
        var signature = string.Join(";", icons.Select(icon => $"{icon.IconId}@{icon.MapX}/{icon.MapY}"));
        if (signature == markerSignature && Join != null)
            return;

        markerSignature = signature;

        var points = icons.Where(icon => icon.World != null)
                          .Select(icon => new MapPoint(icon.MapX, icon.MapY,
                                                       icon.Kind is { } kind ? (BoardRoomKind)kind : null,
                                                       icon.World!.Value.X, icon.World.Value.Z))
                          .ToList();

        Join = BoardJoin.Join(graph, points);

        if (Join.IsComplete)
            Services.Log.Information($"Board {BoardRowId}: all {Join.ByEvent.Count} rooms placed in the world.");
        else
            Services.Log.Information($"Board {BoardRowId}: rooms not placed — {string.Join(" | ", Join.Problems)}");
    }

    private void RefreshTrigger()
    {
        TriggerEvent = -1;
        TriggerPosition = null;

        if (!InRunZone || Graph == null || Join == null)
            return;

        foreach (var obj in Services.Objects)
        {
            if (obj.ObjectKind != ObjectKind.EventObj || obj.BaseId != XbmColumns.Crucible.RoomTriggerDataId)
                continue;

            TriggerPosition = obj.Position;

            var nearest = Join.ByEvent
                              .Select(pair => (Event: pair.Key,
                                               Distance: Vector2.Distance(new Vector2(pair.Value.WorldX, pair.Value.WorldZ),
                                                                          new Vector2(obj.Position.X, obj.Position.Z))))
                              .OrderBy(pair => pair.Distance)
                              .FirstOrDefault();

            if (nearest.Distance <= TriggerMatchRadius && Join.ByEvent.Count > 0)
                TriggerEvent = nearest.Event;

            return;
        }
    }

    private PreviewProjection? Fit(BoardEventMapReader.Snapshot window)
    {
        var tiles = new List<(float, float, Vector2)>();
        foreach (var tile in window.Tiles)
        {
            if (!tile.Visible || tile.CellIndex < 0 || tile.CellIndex >= window.Cells.Count)
                continue;

            var cell = window.Cells[tile.CellIndex];
            if (cell.IsRoom)
                tiles.Add((cell.X, cell.Y, tile.Centre));
        }

        var anchors = new List<(float, float, float, float)>();
        if (Join != null && Graph != null)
        {
            foreach (var (eventIndex, icon) in Join.ByEvent)
            {
                if (Graph.Node(eventIndex) is { } node)
                    anchors.Add((node.X, node.Y, icon.WorldX, icon.WorldZ));
            }
        }

        return PreviewProjection.Fit(tiles, anchors);
    }

    private string Describe()
    {
        if (BoardRowId == 0)
            return "No board seen yet — open the Board Layout once.";

        if (Graph == null)
            return $"Board {BoardRowId} is not in the game data.";

        var parts = new List<string>
        {
            $"Board {BoardRowId}: {Graph.Nodes.Count - 1} rooms over {Graph.LastMove} moves" +
            (Graph.IsValid ? string.Empty : $", {Graph.Problems.Count} problem(s)"),
        };

        if (!InRunZone)
            parts.Add("rooms are placed once in the run");
        else if (Join == null)
            parts.Add("no room icons read yet");
        else
            parts.Add(Join.IsComplete ? "all rooms placed" : $"rooms not placed: {Join.Problems.FirstOrDefault()}");

        parts.Add(CurrentEvent < 0
                      ? "position unknown"
                      : $"at event {CurrentEvent} ({(MarkedEvent >= 0 ? "marked by the board" : "trigger object")})");

        return string.Join("; ", parts) + ".";
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}
