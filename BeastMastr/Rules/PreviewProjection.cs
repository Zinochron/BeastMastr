using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BeastMastr.Rules;

/// <summary>
/// Between three spaces: the world the player walks in, the board's own grid of cells, and the screen
/// where the board window draws that grid.
///
/// Grid to screen is linear on both axes — the window draws cells on a regular grid, and that is fitted
/// from the tiles it actually drew. World to grid is linear across the columns but **piecewise down the
/// rows**: the platforms are not evenly spaced (7.5 and 9 yalms alternate on the first board), so each
/// pair of neighbouring rows gets its own segment, fitted from the rooms whose icons are known.
/// </summary>
public sealed class PreviewProjection
{
    private readonly (float Offset, float Scale) screenX;
    private readonly (float Offset, float Scale) screenY;
    private readonly (float Offset, float Scale)? gridX;
    private readonly List<(float WorldZ, float GridY)> rows;

    private PreviewProjection((float, float) screenX, (float, float) screenY, (float, float)? gridX,
                              List<(float, float)> rows)
    {
        this.screenX = screenX;
        this.screenY = screenY;
        this.gridX = gridX;
        this.rows = rows;
    }

    /// <summary>Whether world positions can be placed at all. Screen placement of cells always can.</summary>
    public bool KnowsWorld => gridX != null && rows.Count >= 2;

    /// <param name="tiles">Cells the window drew: grid position and the centre it drew them at.</param>
    /// <param name="anchors">Rooms whose world position is known: grid position and world X/Z.</param>
    public static PreviewProjection? Fit(IReadOnlyList<(float GridX, float GridY, Vector2 Screen)> tiles,
                                         IReadOnlyList<(float GridX, float GridY, float WorldX, float WorldZ)> anchors)
    {
        if (tiles.Count < 2)
            return null;

        var sx = Line(tiles.Select(tile => (tile.GridX, tile.Screen.X)).ToList());
        var sy = Line(tiles.Select(tile => (tile.GridY, tile.Screen.Y)).ToList());

        // A board drawn as a single column still has square cells: borrow the other axis's scale.
        if (sx == null && sy != null)
            sx = (tiles.Average(tile => tile.Screen.X) - (sy.Value.Scale * tiles.Average(tile => tile.GridX)),
                  sy.Value.Scale);

        if (sy == null && sx != null)
            sy = (tiles.Average(tile => tile.Screen.Y) - (sx.Value.Scale * tiles.Average(tile => tile.GridY)),
                  sx.Value.Scale);

        if (sx == null || sy == null)
            return null;

        var gx = Line(anchors.Select(anchor => (anchor.WorldX, anchor.GridX)).ToList());

        var rowList = anchors.GroupBy(anchor => anchor.GridY)
                             .Select(row => (WorldZ: row.Average(anchor => anchor.WorldZ), GridY: row.Key))
                             .OrderBy(row => row.WorldZ)
                             .ToList();

        return new PreviewProjection(sx.Value, sy.Value, gx, rowList);
    }

    public Vector2 GridToScreen(float gridX, float gridY) =>
        new(screenX.Offset + (screenX.Scale * gridX), screenY.Offset + (screenY.Scale * gridY));

    public Vector2 ScreenToGrid(Vector2 screen) =>
        new((screen.X - screenX.Offset) / screenX.Scale, (screen.Y - screenY.Offset) / screenY.Scale);

    /// <summary>Screen pixels per grid cell, horizontally. What a marker drawn on a cell is sized against.</summary>
    public float CellSize => Math.Abs(screenX.Scale);

    public Vector2? WorldToGrid(float worldX, float worldZ)
    {
        if (gridX is not { } gx || rows.Count < 2)
            return null;

        return new Vector2(gx.Offset + (gx.Scale * worldX), RowOf(worldZ));
    }

    public Vector2? WorldToScreen(float worldX, float worldZ) =>
        WorldToGrid(worldX, worldZ) is { } grid ? GridToScreen(grid.X, grid.Y) : null;

    /// <summary>Piecewise between the known rows, extended past either end along the nearest segment.</summary>
    private float RowOf(float worldZ)
    {
        var segment = 0;
        while (segment < rows.Count - 2 && worldZ > rows[segment + 1].WorldZ)
            segment++;

        var (z0, y0) = rows[segment];
        var (z1, y1) = rows[segment + 1];
        return Math.Abs(z1 - z0) < 0.001f ? y0 : y0 + ((worldZ - z0) * (y1 - y0) / (z1 - z0));
    }

    /// <summary>Least squares <c>y = offset + scale · x</c>, or null when x does not vary.</summary>
    private static (float Offset, float Scale)? Line(IReadOnlyList<(float X, float Y)> points)
    {
        if (points.Count < 2)
            return null;

        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);
        var sxx = points.Sum(point => (point.X - meanX) * (point.X - meanX));

        if (sxx < 1e-6f)
            return null;

        var sxy = points.Sum(point => (point.X - meanX) * (point.Y - meanY));
        var scale = sxy / sxx;
        return (meanY - (scale * meanX), scale);
    }
}
