using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Ipc;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace BeastMastr.Automation.Run;

/// <summary>
/// Walks to one room. Never further: a path over several rows is one more chance to cross a platform
/// on the way, and the board is laid out five yalms apart.
///
/// The walk follows the link the terrain scan checked, from wherever the player actually stands. If
/// the straight way from there would come too close to another room, it goes back over the room the
/// run is on first. It stops the moment the room starts — the board marking it, the trigger moving
/// onto it, or one of the room windows opening — and it stops hard when the player comes near a room
/// it was not heading for.
///
/// Your own input wins at once: the walk lets go, and picks up again from where you left the
/// character once you have not touched anything for the resume delay — or ends, if that is the setting.
/// </summary>
public sealed class BoardWalker : IDisposable
{
    public enum Phase
    {
        Idle,
        WaitingForScan,
        Walking,
        Paused,
        Arrived,
        Failed,
    }

    /// <summary>Close enough to the room's centre to call it reached, in yalms.</summary>
    private const float ArrivalDistance = 0.6f;

    private const float Tolerance = 0.3f;
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan ScanWait = TimeSpan.FromSeconds(60);

    private readonly Configuration configuration;
    private readonly BoardModel board;
    private readonly BoardTerrain terrain;
    private readonly ManualInputGuard input;

    private int from = -1;
    private DateTime startedAt;
    private DateTime lastProgressAt;
    private float bestDistance;
    private bool retried;

    public BoardWalker(Configuration configuration, BoardModel board, BoardTerrain terrain, ManualInputGuard input)
    {
        this.configuration = configuration;
        this.board = board;
        this.terrain = terrain;
        this.input = input;
        Services.Framework.Update += OnUpdate;
    }

    public Phase State { get; private set; } = Phase.Idle;

    /// <summary>The event being walked to, or -1.</summary>
    public int Target { get; private set; } = -1;

    public string Status { get; private set; } = "Idle.";

    public bool Busy => State is Phase.WaitingForScan or Phase.Walking or Phase.Paused;

    /// <summary>Walks to <paramref name="eventIndex"/>, which has to be a room the run can step to next.</summary>
    /// <param name="fromEvent">
    /// The room the run has finished, when the caller knows it better than the board does; otherwise
    /// the board's own word is taken.
    /// </param>
    public bool Walk(int eventIndex, int fromEvent = -1)
    {
        Stop(null);

        if (board.Graph is not { IsValid: true } graph)
            return Fail("There is no usable board: " + board.Status);

        if (!board.OnBoard)
            return Fail("Walking only happens on the board.");

        var current = fromEvent >= 0 ? fromEvent : board.CurrentEvent;
        if (current < 0)
            return Fail("Where the run stands is not known yet. Open the Board Layout once.");

        if (!graph.Next(current).Any(next => next.EventIndex == eventIndex))
            return Fail($"Event {eventIndex} is not a room the run can step to from event {current}.");

        if (board.Join is not { IsComplete: true })
            return Fail("The rooms are not placed in the world: " + board.Status);

        from = current;
        Target = eventIndex;
        startedAt = DateTime.Now;
        retried = false;
        input.Reset();

        if (!terrain.IsCurrent)
        {
            if (!terrain.Busy)
                terrain.RequestScan();

            State = Phase.WaitingForScan;
            Status = "Scanning the ground before the first walk.";
            return true;
        }

        return Begin();
    }

    /// <summary>Stops walking. With a reason it counts as a failure and says so.</summary>
    public void Stop(string? reason)
    {
        if (State is Phase.Walking or Phase.Paused)
            NavmeshIpc.Stop();

        if (reason != null && Busy)
        {
            Fail(reason);
            return;
        }

        if (Busy)
        {
            State = Phase.Idle;
            Status = "Stopped.";
        }
    }

    private bool Begin()
    {
        var edge = terrain.Edge(from, Target);
        if (edge == null)
            return Fail($"The way from event {from} to {Target} was not scanned.");

        if (!edge.Safe)
            return Fail($"The way from event {from} to {Target} is unsafe: {edge.Reason}");

        var path = PathFromHere(edge.Path);
        if (path == null)
            return Fail(Status);

        NavmeshIpc.SetTolerance(Tolerance);
        if (!NavmeshIpc.MoveTo(path))
            return Fail($"vnavmesh would not walk: {NavmeshIpc.LastError}");

        State = Phase.Walking;
        startedAt = DateTime.Now;
        lastProgressAt = DateTime.Now;
        bestDistance = float.MaxValue;
        Status = $"Walking to event {Target} ({path.Count} waypoints).";
        Services.Log.Information($"Walking from event {from} to {Target}: {string.Join(" ", path.Select(p => $"{p.X:0.0}/{p.Z:0.0}"))}");
        return true;
    }

    /// <summary>
    /// The checked link, starting from where the player stands. Straight onto the link if that keeps
    /// clear of the other rooms; otherwise over the current room's centre first.
    /// </summary>
    private List<Vector3>? PathFromHere(List<Vector3> edgePath)
    {
        if (Services.Objects.LocalPlayer is not { } player || edgePath.Count < 2)
        {
            Status = "There is no player or no path to follow.";
            return null;
        }

        var here = player.Position;
        var direct = new List<Vector3> { here };
        direct.AddRange(edgePath.Skip(1));

        if (Clearance(direct) >= Keep())
            return direct;

        var viaRoom = new List<Vector3> { here };
        viaRoom.AddRange(edgePath);

        if (Clearance(viaRoom) >= Keep())
            return viaRoom;

        Status = "Even going back over the current room comes too close to another room — walk there by hand once.";
        return null;
    }

    private float Keep() => configuration.RoomTriggerRadius + 0.5f;

    /// <summary>How close a path comes to a room other than the two it joins.</summary>
    private float Clearance(IReadOnlyList<Vector3> path)
    {
        var best = float.MaxValue;
        foreach (var node in board.Graph!.Nodes)
        {
            if (node.EventIndex == from || node.EventIndex == Target || board.WorldOf(node.EventIndex) is not { } c)
                continue;

            var centre = new Vector2(c.X, c.Z);
            for (var i = 1; i < path.Count; i++)
                best = MathF.Min(best, Distance(centre, Flat(path[i - 1]), Flat(path[i])));
        }

        return best;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!Busy)
            return;

        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Walking failed.");
            Stop($"Walking failed: {ex.Message}");
        }
    }

    private void Tick()
    {
        if (Services.Condition[ConditionFlag.BetweenAreas] || Services.Condition[ConditionFlag.BetweenAreas51] ||
            Services.Condition[ConditionFlag.Unconscious] || !board.InRunZone)
        {
            Stop("The run left the board, or you went down, while walking.");
            return;
        }

        if (DateTime.Now - startedAt > (State == Phase.WaitingForScan ? ScanWait : GiveUpAfter))
        {
            Stop("The walk took far too long.");
            return;
        }

        switch (State)
        {
            case Phase.WaitingForScan:
                if (terrain.Busy)
                {
                    Status = $"Scanning the ground first: {terrain.Status}";
                    return;
                }

                if (!terrain.IsCurrent)
                {
                    Stop($"The ground could not be scanned: {terrain.Status}");
                    return;
                }

                Begin();
                return;

            case Phase.Paused:
                if (input.Holding)
                {
                    Status = $"Paused — you took over ({input.LastInput}). Carrying on {configuration.ResumeDelaySeconds:0.#} s after you let go.";
                    return;
                }

                Status = "Carrying on.";
                Begin();
                return;

            case Phase.Walking:
                Walking();
                return;
        }
    }

    private void Walking()
    {
        if (input.Holding)
        {
            NavmeshIpc.Stop();

            if (configuration.AbortOnManualInput)
            {
                Fail($"You took over ({input.LastInput}); the walk is abandoned.");
                return;
            }

            State = Phase.Paused;
            Services.Log.Information($"Walk paused: {input.LastInput}.");
            return;
        }

        if (RoomStarted())
        {
            Arrive("The room started.");
            return;
        }

        if (Services.Objects.LocalPlayer is not { } player || board.WorldOf(Target) is not { } target)
        {
            Stop("Lost track of the player or the room.");
            return;
        }

        if (NearOtherRoom(player.Position) is { } other)
        {
            Stop($"Came within reach of event {other}, which is not where the walk was going.");
            return;
        }

        var distance = Vector2.Distance(Flat(player.Position), Flat(target));
        if (distance <= ArrivalDistance && !NavmeshIpc.IsRunning())
        {
            Arrive("Reached the room's centre.");
            return;
        }

        if (distance < bestDistance - 0.2f)
        {
            bestDistance = distance;
            lastProgressAt = DateTime.Now;
            return;
        }

        if (DateTime.Now - lastProgressAt < StuckAfter)
            return;

        if (retried)
        {
            Stop($"Stuck {distance:0.0} y short of event {Target}.");
            return;
        }

        retried = true;
        Services.Log.Information($"No progress towards event {Target}; planning the walk once more.");
        NavmeshIpc.Stop();
        Begin();
    }

    /// <summary>Whatever says the room has begun: the board or its trigger on it, a room window up, or a fight.</summary>
    private bool RoomStarted()
    {
        if (board.MarkedEvent == Target || board.TriggerEvent == Target)
            return true;

        // The first sign, about two seconds before any window: "In Event" on the player.
        if (Services.Objects.LocalPlayer is { } player &&
            player.StatusList.Any(status => status.StatusId == XbmColumns.Crucible.InEventStatus))
            return true;

        if (PetPartyReader.Mode() is XbmColumns.PetParty.FightMode or XbmColumns.PetParty.CampsiteMode)
            return true;

        return AddonReader.IsOpen(XbmColumns.RunWindows.ItemShop) ||
               AddonReader.IsOpen(XbmColumns.RunWindows.Treasure) ||
               Services.Condition[ConditionFlag.InCombat];
    }

    private int? NearOtherRoom(Vector3 position)
    {
        foreach (var node in board.Graph!.Nodes)
        {
            if (node.EventIndex == from || node.EventIndex == Target || board.WorldOf(node.EventIndex) is not { } c)
                continue;

            if (Vector2.Distance(Flat(position), Flat(c)) < configuration.RoomTriggerRadius)
                return node.EventIndex;
        }

        return null;
    }

    private void Arrive(string how)
    {
        NavmeshIpc.Stop();
        State = Phase.Arrived;
        Status = $"At event {Target}. {how}";
        Services.Log.Information(Status);
    }

    private bool Fail(string reason)
    {
        if (State is Phase.Walking or Phase.Paused)
            NavmeshIpc.Stop();

        State = Phase.Failed;
        Status = reason;
        Services.Log.Warning($"Walk: {reason}");
        Services.Chat.Print($"[BeastMastr] {reason}");
        return false;
    }

    private static Vector2 Flat(Vector3 point) => new(point.X, point.Z);

    private static float Distance(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + (t * ab));
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        if (State is Phase.Walking or Phase.Paused)
            NavmeshIpc.Stop();
    }
}
