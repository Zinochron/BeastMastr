using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BeastMastr.Ipc;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// The ground the board is walked on, as vnavmesh sees it.
///
/// Three things are measured, all by asking the mesh and never by moving:
///
/// - **Each room's floor.** The icons carry no height, and a room the mesh does not reach cannot be
///   walked to at all — which is the first thing worth knowing.
/// - **A grid of the ground** around the board, half a yalm per cell, so the board tab can show the
///   platforms and the ways between them.
/// - **Every link of the board as a walk.** A straight line where the floor holds all the way, a
///   planned path where it does not — and in both cases a clearance check against every *other* room,
///   because the platforms are five yalms apart and stepping onto the wrong one starts the wrong room.
///   A link that cannot be walked with that clearance is marked unsafe, and the route never takes it.
///
/// Spread over frames: a few hundred mesh queries per frame at most. The result is kept per board and
/// written to the plugin's config folder, so a board is scanned once.
/// </summary>
public sealed class BoardTerrain : IDisposable
{
    public enum Phase
    {
        Idle,
        WaitingForMesh,
        Rooms,
        Grid,
        Edges,
        Done,
        Failed,
    }

    /// <param name="Floor">The mesh's floor at the room, or null when the mesh does not reach it.</param>
    public sealed record RoomPoint(int EventIndex, Vector3 Centre, Vector3? Floor);

    /// <param name="Straight">Walked as a straight line; otherwise the path vnavmesh planned.</param>
    /// <param name="Clearance">How close the walk comes to a room it is not heading for.</param>
    public sealed record EdgeCheck(int From, int To, bool Safe, bool Straight, float Length, float Clearance,
                                   string Reason, List<Vector3> Path);

    /// <summary>Heights per cell, row by row from the origin; NaN where there is no floor.</summary>
    public sealed record TerrainGrid(float OriginX, float OriginZ, float Step, int Width, int Depth, float[] Heights)
    {
        public float At(int x, int z) => Heights[(z * Width) + x];
    }

    public sealed record Scan(uint BoardRowId, string Signature, DateTime ScannedAt, float TriggerRadius,
                              List<RoomPoint> Rooms, TerrainGrid? Grid, List<EdgeCheck> Edges, List<string> Problems)
    {
        public bool AllSafe => Problems.Count == 0 && Edges.All(edge => edge.Safe);
    }

    private const float GridStep = 0.5f;
    private const float GridMargin = 4f;
    private const int QueriesPerFrame = 200;
    private const int SamplesPerEdge = 24;

    /// <summary>A step up or down between two samples of a straight walk that is still walkable.</summary>
    private const float MostStep = 0.8f;

    /// <summary>Kept on top of the trigger radius, so a walk does not graze a platform's edge.</summary>
    private const float ClearanceMargin = 0.5f;

    private static readonly TimeSpan PathfindTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Fields, because <see cref="Vector3"/> keeps its numbers in them; NaN, because the grid marks "no floor" with it.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly Configuration configuration;
    private readonly BoardModel board;

    private Scan? working;
    private int gridCursor;
    private int edgeCursor;
    private List<(int From, int To)> edgeQueue = [];
    private Task<List<Vector3>>? pending;
    private DateTime pendingSince;
    private int pendingAttempt;
    private int avoidEvent = -1;

    /// <summary>The board whose stored scan was last looked for, so the file is read once, not every frame.</summary>
    private uint loadedFor;

    public BoardTerrain(Configuration configuration, BoardModel board)
    {
        this.configuration = configuration;
        this.board = board;
        Services.Framework.Update += OnUpdate;
    }

    public Phase State { get; private set; } = Phase.Idle;

    /// <summary>0 to 1 across the whole scan.</summary>
    public float Progress { get; private set; }

    public string Status { get; private set; } = "Not scanned.";

    /// <summary>The last finished scan of the board being played, or null.</summary>
    public Scan? Current { get; private set; }

    public bool Busy => State is Phase.WaitingForMesh or Phase.Rooms or Phase.Grid or Phase.Edges;

    /// <summary>Links the route must not take.</summary>
    public IReadOnlySet<(int From, int To)> UnsafeEdges =>
        Current?.Edges.Where(edge => !edge.Safe).Select(edge => (edge.From, edge.To)).ToHashSet()
        ?? new HashSet<(int, int)>();

    public EdgeCheck? Edge(int from, int to) =>
        Current?.Edges.FirstOrDefault(edge => edge.From == from && edge.To == to);

    public RoomPoint? Room(int eventIndex) => Current?.Rooms.FirstOrDefault(room => room.EventIndex == eventIndex);

    /// <summary>Whether the stored scan still describes the board as it is placed now.</summary>
    public bool IsCurrent => Current != null && Current.BoardRowId == board.BoardRowId &&
                             Current.Signature == Signature();

    public void RequestScan()
    {
        Cancel();

        if (!board.InRunZone)
        {
            Status = "The ground can only be scanned inside the run's zone.";
            return;
        }

        if (board.Graph is not { IsValid: true } graph || board.Join is not { IsComplete: true })
        {
            Status = "The board is not fully placed yet: " + board.Status;
            return;
        }

        working = new Scan(board.BoardRowId, Signature(), DateTime.Now, configuration.RoomTriggerRadius, [], null,
                           [], []);
        edgeQueue = graph.Edges.OrderBy(edge => edge.From).ThenBy(edge => edge.To).ToList();
        gridCursor = 0;
        edgeCursor = 0;
        State = Phase.WaitingForMesh;
        Status = "Waiting for vnavmesh.";
    }

    public void Cancel()
    {
        working = null;
        pending = null;
        avoidEvent = -1;
        pendingAttempt = 0;

        if (Busy)
        {
            State = Phase.Idle;
            Status = "Scan cancelled.";
        }
    }

    /// <summary>Writes vnavmesh's own picture of the reachable ground around the board to <c>captures/</c>.</summary>
    public string WriteMeshImage()
    {
        var rooms = RoomCentres();
        var player = Services.Objects.LocalPlayer;
        if (rooms.Count == 0 || player == null)
            return "Nothing to draw around yet.";

        var (min, max) = Bounds(rooms);
        var path = Path.Combine(CaptureStore.Directory.FullName, $"navmesh-{DateTime.Now:yyyyMMdd-HHmmss}.bmp");
        return NavmeshIpc.BuildBitmapBounded(player.Position, path, 0.25f,
                                             new Vector3(min.X, player.Position.Y - 10f, min.Y),
                                             new Vector3(max.X, player.Position.Y + 10f, max.Y))
                   ? path
                   : NavmeshIpc.LastError;
    }

    private void OnUpdate(IFramework framework)
    {
        if (board.BoardRowId != 0 && board.BoardRowId != loadedFor && !Busy)
        {
            loadedFor = board.BoardRowId;
            Current = Load(board.BoardRowId);
            if (Current != null)
                Status = $"Scanned {Current.ScannedAt:g}{(Current.AllSafe ? string.Empty : ", with problems")}.";
        }

        if (working == null)
            return;

        try
        {
            Step(working);
        }
        catch (Exception ex)
        {
            Fail($"The scan failed: {ex.Message}");
            Services.Log.Error(ex, "The terrain scan failed.");
        }
    }

    private void Step(Scan scan)
    {
        switch (State)
        {
            case Phase.WaitingForMesh:
                if (!NavmeshIpc.IsLoaded)
                {
                    Fail("vnavmesh is not loaded.");
                    return;
                }

                if (!NavmeshIpc.IsReady())
                {
                    var progress = NavmeshIpc.BuildProgress();
                    Status = progress >= 0 ? $"vnavmesh is building the mesh: {progress:P0}" : "Waiting for vnavmesh.";
                    return;
                }

                State = Phase.Rooms;
                return;

            case Phase.Rooms:
                ScanRooms(scan);
                State = Phase.Grid;
                return;

            case Phase.Grid:
                if (ScanGrid(scan))
                    State = Phase.Edges;

                return;

            case Phase.Edges:
                if (ScanEdges(scan))
                    Finish(scan);

                return;
        }
    }

    private void ScanRooms(Scan scan)
    {
        foreach (var (eventIndex, centre) in RoomCentres())
        {
            var floor = NavmeshIpc.PointOnFloor(centre + new Vector3(0f, 3f, 0f), 1.5f);
            scan.Rooms.Add(new RoomPoint(eventIndex, centre, floor));

            if (floor == null)
                scan.Problems.Add($"The mesh does not reach event {eventIndex} at {centre.X:0.0}/{centre.Z:0.0}.");
        }

        Status = scan.Problems.Count == 0
                     ? $"All {scan.Rooms.Count} rooms are on the mesh."
                     : $"{scan.Problems.Count} room(s) are off the mesh — the mesh may belong to another board; try a rebuild.";
        Progress = 0.1f;
    }

    private bool ScanGrid(Scan scan)
    {
        if (scan.Grid == null)
        {
            var (min, max) = Bounds(RoomCentres());
            var width = (int)MathF.Ceiling((max.X - min.X) / GridStep) + 1;
            var depth = (int)MathF.Ceiling((max.Y - min.Y) / GridStep) + 1;
            var heights = new float[width * depth];
            Array.Fill(heights, float.NaN);
            working = scan = scan with { Grid = new TerrainGrid(min.X, min.Y, GridStep, width, depth, heights) };
        }

        var grid = scan.Grid!;
        var height = Services.Objects.LocalPlayer?.Position.Y ?? 0f;
        var total = grid.Width * grid.Depth;

        for (var done = 0; done < QueriesPerFrame && gridCursor < total; done++, gridCursor++)
        {
            var x = grid.OriginX + ((gridCursor % grid.Width) * grid.Step);
            var z = grid.OriginZ + ((gridCursor / grid.Width) * grid.Step);
            var floor = NavmeshIpc.PointOnFloor(new Vector3(x, height + 3f, z), grid.Step / 2f);

            // Only floor under this very cell counts; a point snapped in from the side is the
            // neighbour's floor, not this cell's.
            if (floor is { } f && MathF.Abs(f.X - x) <= grid.Step && MathF.Abs(f.Z - z) <= grid.Step)
                grid.Heights[gridCursor] = f.Y;
        }

        Progress = 0.1f + (0.5f * gridCursor / total);
        Status = $"Scanning the ground: {gridCursor} of {total}.";
        return gridCursor >= total;
    }

    /// <summary>One edge per call, possibly across several frames while a planned path is awaited.</summary>
    private bool ScanEdges(Scan scan)
    {
        if (edgeCursor >= edgeQueue.Count)
            return true;

        var (from, to) = edgeQueue[edgeCursor];
        Progress = 0.6f + (0.4f * edgeCursor / Math.Max(1, edgeQueue.Count));
        Status = $"Checking the way from event {from} to {to} ({edgeCursor + 1} of {edgeQueue.Count}).";

        var start = Floor(scan, from);
        var end = Floor(scan, to);
        if (start == null || end == null)
        {
            Add(scan, new EdgeCheck(from, to, false, false, 0f, 0f, "One end is not on the mesh.", []));
            return false;
        }

        var radius = scan.TriggerRadius + ClearanceMargin;

        if (pending == null)
        {
            var straight = new List<Vector3> { start.Value, end.Value };
            if (StraightHolds(start.Value, end.Value))
            {
                var (clearance, offender) = Clearance(scan, straight, from, to);
                if (clearance >= radius)
                {
                    Add(scan, new EdgeCheck(from, to, true, true, Vector3.Distance(start.Value, end.Value), clearance,
                                            "Straight.", straight));
                    return false;
                }

                avoidEvent = offender;
            }

            pending = avoidEvent >= 0 && Floor(scan, avoidEvent) is { } avoid
                          ? NavmeshIpc.PathfindAvoid(start.Value, end.Value, avoid, radius)
                          : NavmeshIpc.Pathfind(start.Value, end.Value);
            pendingSince = DateTime.Now;

            if (pending == null)
            {
                Add(scan, new EdgeCheck(from, to, false, false, 0f, 0f, $"Could not ask for a path: {NavmeshIpc.LastError}",
                                        []));
            }

            return false;
        }

        if (!pending.IsCompleted)
        {
            if (DateTime.Now - pendingSince > PathfindTimeout)
            {
                pending = null;
                Add(scan, new EdgeCheck(from, to, false, false, 0f, 0f, "vnavmesh took too long to plan it.", []));
            }

            return false;
        }

        var path = pending.IsCompletedSuccessfully ? pending.Result : [];
        pending = null;

        if (path.Count < 2)
        {
            Add(scan, new EdgeCheck(from, to, false, false, 0f, 0f, "vnavmesh found no path.", []));
            return false;
        }

        var (pathClearance, pathOffender) = Clearance(scan, path, from, to);
        var length = PathLength(path);

        if (pathClearance >= radius)
        {
            Add(scan, new EdgeCheck(from, to, true, false, length, pathClearance,
                                    avoidEvent >= 0 ? $"Planned around event {avoidEvent}." : "Planned.", path));
            return false;
        }

        // One more try, around the room the first path came too close to.
        if (pendingAttempt == 0 && pathOffender >= 0)
        {
            pendingAttempt = 1;
            avoidEvent = pathOffender;
            return false;
        }

        Add(scan, new EdgeCheck(from, to, false, false, length, pathClearance,
                                $"Every way passes within {pathClearance:0.0} y of event {pathOffender}.", path));
        return false;
    }

    private void Add(Scan scan, EdgeCheck edge)
    {
        scan.Edges.Add(edge);
        edgeCursor++;
        pendingAttempt = 0;
        avoidEvent = -1;
    }

    private bool StraightHolds(Vector3 start, Vector3 end)
    {
        var previous = start.Y;
        for (var i = 1; i < SamplesPerEdge; i++)
        {
            var point = Vector3.Lerp(start, end, i / (float)SamplesPerEdge);
            var floor = NavmeshIpc.PointOnFloor(point + new Vector3(0f, 2f, 0f), 0.3f);
            if (floor is not { } f || MathF.Abs(f.Y - previous) > MostStep)
                return false;

            previous = f.Y;
        }

        return true;
    }

    /// <summary>The closest the path comes to any room other than its two ends, and which room that is.</summary>
    private static (float Clearance, int Room) Clearance(Scan scan, IReadOnlyList<Vector3> path, int from, int to)
    {
        var best = float.MaxValue;
        var room = -1;

        foreach (var other in scan.Rooms)
        {
            if (other.EventIndex == from || other.EventIndex == to)
                continue;

            var centre = new Vector2(other.Centre.X, other.Centre.Z);
            for (var i = 1; i < path.Count; i++)
            {
                var distance = SegmentDistance(centre, new Vector2(path[i - 1].X, path[i - 1].Z),
                                               new Vector2(path[i].X, path[i].Z));
                if (distance < best)
                {
                    best = distance;
                    room = other.EventIndex;
                }
            }
        }

        return (best, room);
    }

    private static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + (t * ab));
    }

    private static float PathLength(IReadOnlyList<Vector3> path)
    {
        var length = 0f;
        for (var i = 1; i < path.Count; i++)
            length += Vector3.Distance(path[i - 1], path[i]);

        return length;
    }

    private static Vector3? Floor(Scan scan, int eventIndex) =>
        scan.Rooms.FirstOrDefault(room => room.EventIndex == eventIndex)?.Floor;

    private void Finish(Scan scan)
    {
        var unsafeCount = scan.Edges.Count(edge => !edge.Safe);
        if (unsafeCount > 0)
            scan.Problems.Add($"{unsafeCount} of {scan.Edges.Count} links cannot be walked safely.");

        Current = scan;
        working = null;
        State = Phase.Done;
        Progress = 1f;
        Status = scan.AllSafe
                     ? $"Scanned: {scan.Rooms.Count} rooms, every one of {scan.Edges.Count} links walkable."
                     : $"Scanned with problems: {string.Join(" ", scan.Problems)}";

        Save(scan);
        Services.Log.Information($"Terrain of board {scan.BoardRowId}: {Status}");
    }

    private void Fail(string reason)
    {
        working = null;
        pending = null;
        State = Phase.Failed;
        Status = reason;
    }

    private List<(int EventIndex, Vector3 Centre)> RoomCentres()
    {
        var centres = new List<(int, Vector3)>();
        if (board.Graph == null)
            return centres;

        foreach (var node in board.Graph.Nodes)
        {
            if (board.WorldOf(node.EventIndex) is { } world)
                centres.Add((node.EventIndex, world));
        }

        return centres;
    }

    private static (Vector2 Min, Vector2 Max) Bounds(IReadOnlyList<(int EventIndex, Vector3 Centre)> rooms)
    {
        var min = new Vector2(rooms.Min(room => room.Centre.X), rooms.Min(room => room.Centre.Z));
        var max = new Vector2(rooms.Max(room => room.Centre.X), rooms.Max(room => room.Centre.Z));
        return (min - new Vector2(GridMargin), max + new Vector2(GridMargin));
    }

    /// <summary>The board and where its icons stand. A scan of a different placement is not this board's.</summary>
    private string Signature()
    {
        if (board.Join == null)
            return string.Empty;

        return $"{board.BoardRowId}:" + string.Join(";", board.Join.ByEvent.OrderBy(pair => pair.Key)
                                                              .Select(pair => $"{pair.Key}@{pair.Value.WorldX:0.0}/{pair.Value.WorldZ:0.0}"));
    }

    // ---- Storage ----------------------------------------------------------

    private static string FileOf(uint boardRowId) =>
        Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, $"terrain-board{boardRowId}.json");

    private static void Save(Scan scan)
    {
        try
        {
            Services.PluginInterface.ConfigDirectory.Create();
            File.WriteAllText(FileOf(scan.BoardRowId), JsonSerializer.Serialize(scan, Json));
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not save the terrain scan.");
        }
    }

    private static Scan? Load(uint boardRowId)
    {
        try
        {
            var file = FileOf(boardRowId);
            return File.Exists(file) ? JsonSerializer.Deserialize<Scan>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"The stored terrain of board {boardRowId} could not be read: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Cancel();
    }
}
