using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Automation.Run;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;

namespace BeastMastr.UI;

/// <summary>
/// The next step of the route, drawn on the board itself: a green ring on the room the route takes
/// next, red rings on the other rooms of that move — the ones not to step on — and the checked way
/// there on the ground. While a walk is under way, its target ring turns yellow.
///
/// Only the next step, and only on the board: the first attempt at world drawing hung a card under
/// every room and was in the way. A ring and a line are there to show where the run is going.
/// </summary>
public sealed class WorldRouteOverlay
{
    private const uint Next = 0xC050E050;
    private const uint Walking = 0xE030D0FF;
    private const uint Avoid = 0xA04040E0;
    private const uint Way = 0xB050E050;
    private const int CircleSegments = 32;
    private const float Step = 0.5f;

    private readonly Configuration configuration;
    private readonly BoardModel board;
    private readonly BoardTerrain terrain;
    private readonly RouteKeeper route;
    private readonly BoardWalker walker;

    public WorldRouteOverlay(Configuration configuration, BoardModel board, BoardTerrain terrain, RouteKeeper route,
                             BoardWalker walker)
    {
        this.configuration = configuration;
        this.board = board;
        this.terrain = terrain;
        this.route = route;
        this.walker = walker;
    }

    public void Draw()
    {
        if (!configuration.ShowRouteInWorld || !board.OnBoard || board.Graph is not { IsValid: true } graph)
            return;

        try
        {
            var from = board.CurrentEvent;
            var next = walker.Busy ? walker.Target : route.NextEvent;
            if (graph.Node(next) is not { } nextNode)
                return;

            var draw = ImGui.GetBackgroundDrawList();
            var radius = configuration.RoomTriggerRadius;

            foreach (var other in graph.OnMove(nextNode.Move).Where(node => node.EventIndex != next))
            {
                if (Floor(other.EventIndex) is { } centre)
                    Ring(draw, centre, radius, Avoid, 2f);
            }

            if (Floor(next) is { } target)
                Ring(draw, target, radius, walker.Busy ? Walking : Next, 3f);

            if (terrain.IsCurrent && terrain.Edge(from, next) is { Path.Count: > 1 } edge)
                Line(draw, edge.Path, Way, 3f);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Drawing the route in the world failed; switched off.");
            configuration.ShowRouteInWorld = false;
            configuration.Save();
        }
    }

    /// <summary>A room's floor: from the scan when there is one, else its centre at the player's height.</summary>
    private Vector3? Floor(int eventIndex) =>
        terrain.IsCurrent && terrain.Room(eventIndex)?.Floor is { } floor ? floor : board.WorldOf(eventIndex);

    private static void Ring(ImDrawListPtr draw, Vector3 centre, float radius, uint colour, float thickness)
    {
        var points = new List<Vector3>(CircleSegments + 1);
        for (var i = 0; i <= CircleSegments; i++)
        {
            var angle = i * MathF.Tau / CircleSegments;
            points.Add(centre + new Vector3(MathF.Cos(angle) * radius, 0.05f, MathF.Sin(angle) * radius));
        }

        Polyline(draw, points, colour, thickness);
    }

    /// <summary>A path on the ground, cut into short pieces so the projection bends with the camera.</summary>
    private static void Line(ImDrawListPtr draw, IReadOnlyList<Vector3> path, uint colour, float thickness)
    {
        var points = new List<Vector3>();
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1] + new Vector3(0f, 0.05f, 0f);
            var b = path[i] + new Vector3(0f, 0.05f, 0f);
            var pieces = Math.Max(1, (int)(Vector3.Distance(a, b) / Step));
            for (var p = 0; p < pieces; p++)
                points.Add(Vector3.Lerp(a, b, p / (float)pieces));

            if (i == path.Count - 1)
                points.Add(b);
        }

        Polyline(draw, points, colour, thickness);
    }

    private static void Polyline(ImDrawListPtr draw, IReadOnlyList<Vector3> points, uint colour, float thickness)
    {
        Vector2? previous = null;
        foreach (var point in points)
        {
            if (!Services.GameGui.WorldToScreen(point, out var screen))
            {
                previous = null;
                continue;
            }

            if (previous is { } last)
                draw.AddLine(last, screen, colour, thickness);

            previous = screen;
        }
    }
}
