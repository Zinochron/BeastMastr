using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace BeastMastr.Ipc;

/// <summary>
/// vnavmesh, over its IPC. Every signature here was read off the installed plugin (1.2.3.14) rather
/// than off documentation: a call whose types do not match fails at runtime, not at build time.
///
/// Nothing here throws. A failed call returns the type's empty value and leaves the reason in
/// <see cref="LastError"/>, because a plugin that is not loaded and a path that came back empty must
/// not look the same to whoever is deciding whether to walk.
/// </summary>
public static class NavmeshIpc
{
    public const string InternalName = "vnavmesh";
    private const string Prefix = "vnavmesh.";

    public static string LastError { get; private set; } = string.Empty;

    public static bool IsLoaded => PluginPresence.IsLoaded(InternalName);

    public static string Version => PluginPresence.Version(InternalName);

    // ---- Nav --------------------------------------------------------------

    /// <summary>Whether a mesh for the current zone is loaded and can be asked anything.</summary>
    public static bool IsReady() => Func<bool>("Nav.IsReady");

    /// <summary>How far building the zone's mesh is, 0 to 1; negative when nothing is being built.</summary>
    public static float BuildProgress() => Func("Nav.BuildProgress", -1f);

    /// <summary>Throws the cached mesh away and builds it from the zone again.</summary>
    public static bool Rebuild() => Func<bool>("Nav.Rebuild");

    public static Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly = false) =>
        Call(() => Services.PluginInterface
                           .GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>(Prefix + "Nav.Pathfind")
                           .InvokeFunc(from, to, fly), "Nav.Pathfind");

    /// <summary>A path that keeps clear of one circle — a room's platform that must not be crossed.</summary>
    public static Task<List<Vector3>>? PathfindAvoid(Vector3 from, Vector3 to, Vector3 avoidCentre, float avoidRadius,
                                                     bool fly = false) =>
        Call(() => Services.PluginInterface
                           .GetIpcSubscriber<Vector3, Vector3, bool, Vector3, float, Task<List<Vector3>>>(
                               Prefix + "Nav.PathfindAvoid")
                           .InvokeFunc(from, to, fly, avoidCentre, avoidRadius), "Nav.PathfindAvoid");

    /// <summary>Writes the reachable mesh around a point to an image, for looking at.</summary>
    public static bool BuildBitmapBounded(Vector3 start, string file, float pixelSize, Vector3 min, Vector3 max) =>
        Act(() => Services.PluginInterface
                          .GetIpcSubscriber<Vector3, string, float, Vector3, Vector3, ValueTuple<Vector3, Vector3>>(
                              Prefix + "Nav.BuildBitmapBounded")
                          .InvokeFunc(start, file, pixelSize, min, max), "Nav.BuildBitmapBounded");

    // ---- Query ------------------------------------------------------------

    /// <summary>The floor under or near a point, or null when there is none within reach.</summary>
    public static Vector3? PointOnFloor(Vector3 point, float halfExtentXz, bool allowUnlandable = false) =>
        Call(() => Services.PluginInterface
                           .GetIpcSubscriber<Vector3, bool, float, Vector3?>(Prefix + "Query.Mesh.PointOnFloor")
                           .InvokeFunc(point, allowUnlandable, halfExtentXz), "Query.Mesh.PointOnFloor");

    public static Vector3? NearestPoint(Vector3 point, float halfExtentXz, float halfExtentY) =>
        Call(() => Services.PluginInterface
                           .GetIpcSubscriber<Vector3, float, float, Vector3?>(Prefix + "Query.Mesh.NearestPoint")
                           .InvokeFunc(point, halfExtentXz, halfExtentY), "Query.Mesh.NearestPoint");

    public static bool IsPointOnMesh(Vector3 point, float halfExtentY, bool allowUnreachable = false) =>
        Call(() => Services.PluginInterface
                           .GetIpcSubscriber<Vector3, float, bool, bool>(Prefix + "Query.Mesh.IsPointOnMesh")
                           .InvokeFunc(point, halfExtentY, allowUnreachable), "Query.Mesh.IsPointOnMesh");

    // ---- Path -------------------------------------------------------------

    /// <summary>Follows these waypoints. Nothing is planned: they are walked as given.</summary>
    public static bool MoveTo(List<Vector3> waypoints, bool fly = false) =>
        Act(() => Services.PluginInterface
                          .GetIpcSubscriber<List<Vector3>, bool, object>(Prefix + "Path.MoveTo")
                          .InvokeAction(waypoints, fly), "Path.MoveTo");

    public static bool Stop() =>
        Act(() => Services.PluginInterface.GetIpcSubscriber<object>(Prefix + "Path.Stop").InvokeAction(),
            "Path.Stop");

    public static bool IsRunning() => Func<bool>("Path.IsRunning");

    public static int NumWaypoints() => Func<int>("Path.NumWaypoints");

    public static bool SetTolerance(float tolerance) =>
        Act(() => Services.PluginInterface.GetIpcSubscriber<float, object>(Prefix + "Path.SetTolerance")
                          .InvokeAction(tolerance), "Path.SetTolerance");

    /// <summary>Plans a path and walks it until within <paramref name="range"/> of the destination.</summary>
    public static bool PathfindAndMoveCloseTo(Vector3 destination, float range, bool fly = false) =>
        Call(() => Services.PluginInterface
                           .GetIpcSubscriber<Vector3, bool, float, bool>(Prefix + "SimpleMove.PathfindAndMoveCloseTo")
                           .InvokeFunc(destination, fly, range), "SimpleMove.PathfindAndMoveCloseTo");

    public static bool PathfindInProgress() => Func<bool>("SimpleMove.PathfindInProgress");

    // ---- Plumbing ---------------------------------------------------------

    private static T Func<T>(string name, T fallback = default!) =>
        Call(() => Services.PluginInterface.GetIpcSubscriber<T>(Prefix + name).InvokeFunc(), name, fallback);

    private static T Call<T>(Func<T> call, string name, T fallback = default!)
    {
        try
        {
            var result = call();
            LastError = string.Empty;
            return result;
        }
        catch (Exception ex)
        {
            Fail(name, ex);
            return fallback;
        }
    }

    private static bool Act(Action call, string name)
    {
        try
        {
            call();
            LastError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            Fail(name, ex);
            return false;
        }
    }

    private static void Fail(string name, Exception ex)
    {
        var reason = IsLoaded ? $"{name} failed: {ex.GetType().Name} {ex.Message}" : "vnavmesh is not loaded";
        if (reason != LastError)
            Services.Log.Warning($"vnavmesh: {reason}");

        LastError = reason;
    }
}
